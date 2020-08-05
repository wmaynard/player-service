package com.rumble.api.controllers

import com.mongodb.MongoCommandException
import com.mongodb.MongoException
import com.rumble.api.services.ProfileTypes
import com.rumble.platform.exception.AuthException
import com.rumble.platform.exception.ApplicationException
import com.rumble.platform.exception.BadRequestException
import com.rumble.platform.exception.PlatformException
import grails.converters.JSON
import groovy.json.JsonSlurper
import groovy.json.JsonOutput
import org.bson.json.JsonMode
import org.bson.json.JsonWriterSettings
import org.slf4j.MDC
import org.springframework.util.MimeTypeUtils

import java.nio.charset.StandardCharsets

class PlayerController {
    def accessTokenService
    def accountService
    def authService
    def dynamicConfigService
    def geoLookupService
    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService
    def paramsService
    def profileService
    def checksumService
    def itemService

    def game = System.getProperty("GAME_GUKEY")

    /**
     * Used for server-authoritative games.
     */
    def launch() {
        mongoService.runTransactionWithRetry({ launchTransaction() }, 1)
    }

    def launchTransaction() {

        def requestData = request.JSON

        if (!requestData.installId) {
            throw new BadRequestException('Required parameter installId was not provided.')
        }

        def responseData = [
                success   : false,
                remoteAddr: request.remoteAddr,
                geoipAddr : request.remoteAddr,
                country   : 'US',
                serverTime: '\'' + System.currentTimeMillis() + '\'',
                assetPath : 'https://rumble-game-alliance-dist.s3.amazonaws.com/client/',
                clientvars: [:]
        ]

        def clientRequestId = requestData?.requestId
        def requestId = clientRequestId ?: UUID.randomUUID().toString()
        if (clientRequestId) {
            MDC.put("clientRequestId", clientRequestId)
        }
        responseData.requestId = requestId
        responseData.accountId = requestData.installId
        if(requestData) {
            if(requestData.installId) {
                MDC.put('installId', requestData.installId)
            }
            if(requestData.clientVersion) {
                MDC.put('clientVersion', requestData.clientVersion)
            }
        }

        def ipAddr = geoLookupService.getIpAddress(request)

        if (ipAddr) {
            responseData.remoteAddr = ipAddr
            responseData.geoipAddr = ipAddr

            def loc
            try {
                loc = geoLookupService.getLocation(ipAddr)
                if (loc) {
                    responseData.country = loc.getCountry()?.getIsoCode()
                    logger.info("GeoIP lookup results", [ipAddr: ipAddr, country: loc.country.isoCode])
                } else {
                    logger.info("GeoIP lookup failed", [ipAddr: ipAddr])
                }
            } catch (e) {
                logger.warn("Exception looking up geo location for IP Address", all, [ipAddr: ipAddr])
            }
        }

        def channel = requestData.channel ?: ""
        def channelScope = "channel:${channel}"
        def channelConfig = dynamicConfigService.getConfig(channelScope)

        //Map channel-specific game identifier to game gukey
        if (requestData.gameGukey) {
            game = requestData.gameGukey
        }
        String gameGukey = channelConfig["game.${game}.gukey"] ?: game
        def gameConfig = dynamicConfigService.getGameConfig(gameGukey)

        //This looks for variables with a certain prefix (eg_ kr:clientvars:) and puts them in the client_vars structure
        //The prefixes are in a json list, and will be applied in order, overlaying any variable that collides
        def clientVersion = requestData.clientVersion
        def prefixes = gameConfig.list("clientVarPrefixes")
        def configs = [channelConfig, gameConfig]
        def clientvars = extractClientVars(clientVersion, prefixes, configs)
        if (clientvars) {
            responseData.clientvars = clientvars
        }

        // Blacklist countries
        if (dynamicConfigService.getConfig('canvas').list('blacklistCountries').contains(responseData.country as String)) {
            responseData.errorCode = "geoblocked"
            responseData.supportUrl = gameConfig['supportUrl']
            render(responseData as JSON)
            return false
        }

        def conflict = false
        def clientSession
        try {
            try {
                clientSession = mongoService.client().startSession()
                clientSession.startTransaction()
                def player = accountService.exists(clientSession, requestData.installId, requestData)
                if (!player) {
                    // Error 'cause upsert failed
                    responseData.errorCode = "dbError"
                    responseData.debugText = err.response?.errmsg
                    logger.error("Probably impossible dbError")
                    throw new PlatformException('dbError', 'Probably impossible dbError', null, responseData)
                }

                def id = player.getObjectId("_id")
                MDC.put('accountId', id?.toString())

                def authHeader = request.getHeader('Authorization')
                try {
                    if (authHeader?.startsWith('Bearer ')) {
                        def accessToken = authHeader.substring(7)
                        def tokenAuth = accessTokenService.validateAccessToken(accessToken, false, false)
                        if ((tokenAuth.aud == game) && (tokenAuth.sub == id.toString())) {
                            def replaceAfter = tokenAuth.exp - gameConfig.long('auth:minTokenLifeSeconds', 172800L)
                            // 2d
                            if (System.currentTimeMillis() / 1000L < replaceAfter) {
                                responseData.accessToken = accessToken
                            }
                        }
                    }
                } catch (Exception e) {
                    logger.error("Exception examining authorization header", e, [header: authHeader])
                }

                if (!responseData.accessToken) {
                    responseData.accessToken = accessTokenService.generateAccessToken(
                            gameGukey, id.toString(), null, gameConfig.long('auth:maxTokenLifeSeconds', 345600L)) // 4d
                }

                //TODO: Validate account
                def validProfiles = profileService.validateProfile(requestData)
                /* validProfiles = [
                 *   facebook: FACEBOOK_ID,
                 *   gameCenter: GAMECENTER_ID,
                 *   googlePlay: GOOGLEPLAY_ID
                 * ]
                 */

                if (requestData.mergeToken) {
                    // Validate merge token
                    def mergeAccountId = requestData.mergeAccountId
                    if (!mergeAccountId) {
                        throw new BadRequestException('Required parameter mergeAccountId was not provided.')
                    }
                    if (accountService.validateMergeToken(id as String, requestData.mergeToken, mergeAccountId)) {
                        logger.info("Merge token accepted", [accountId: id, mergeAccountId: responseData.mergeAccountId])
                        id = mergeAccountId
                        responseData.accountId = id.toString()
                        responseData.createdDate = player.cd.toString()

                        profileService.mergeInstallIdProfile(clientSession, id.toString(), requestData.installId, requestData)

                        // Save over data
                        validProfiles.each { profile, profileData ->
                            profileService.mergeProfile(clientSession, profile, id.toString(), profileData)
                        }

                        accountService.updateAccountData(clientSession, id.toString(), requestData, null, true)

                    } else {
                        responseData.errorCode = "mergeConflict"
                        throw new PlatformException('mergeConflict', null, null, responseData)
                    }
                } else {
                    if (validProfiles) {
                        def conflictProfiles = []
                        // Get profiles attached to player we found
                        def playerProfiles = profileService.getAccountsFromProfiles(validProfiles)

                        if (playerProfiles) {
                            // Assuming there is only one type of profile for each account, check to see if they conflict
                            // For each valid profile, we need to grab all the accounts that are attached to it
                            // and then compare those Account IDs with the Account ID that matches the Install ID
                            playerProfiles.each { profile ->
                                //TODO: Check for profile conflict
                                if (id.toString() != profile.aid.toString()) {
                                    conflictProfiles << profile
                                }
                            }
                        }

                        if (conflictProfiles && conflictProfiles.size() > 0) {
                            conflict = true
                            responseData.errorCode = "accountConflict"
                            logger.info("Account conflict", [accountId: id.toString()])
                            //TODO: Include which accounts are conflicting? Security concerns?
                            def conflictingAccountIds = conflictProfiles.collect {
                                if (it.aid.toString() != id.toString()) {
                                    return it.aid
                                }
                            } ?: "placeholder"
                            if (conflictingAccountIds.size() > 0) {
                                responseData.conflictingAccountId = conflictingAccountIds.first().toString()
                            }
                        }
                    }

                    // Check for install conflict
                    if (!conflict && accountService.hasInstallConflict(player, requestData)) {
                        conflict = true
                        responseData.errorCode = "installConflict"
                        responseData.conflictingAccountId = id.toString()
                    }

                    def updatedAccount
                    if (conflict) {
                        // Generate merge token
                        responseData.mergeToken = accountService.generateMergeToken(clientSession, id as String, responseData.conflictingAccountId)
                        logger.info("Merge token generated", [errorCode: responseData.errorCode, accountId: id, mergeAccountId: responseData.conflictingAccountId])
                    } else {
                        // If we've gotten this far, there should be no conflicts, so save all the things
                        // Save Install ID profile
                        profileService.saveInstallIdProfile(clientSession, id.toString(), requestData.installId, requestData)

                        // Save social profiles
                        validProfiles.each { profile, profileData ->
                            profileService.saveProfile(clientSession, profile, id.toString(), profileData)
                        }

                        // do we really need this update? maybe not on a new player?
                        updatedAccount = accountService.updateAccountData(clientSession, id.toString(), requestData, null)
                        responseData.createdDate = updatedAccount.cd?.toString() ?: null
                    }

                    responseData.accountId = id.toString()
                }
            } catch (MongoCommandException e) {
                clientSession.abortTransaction()
                throw e
            } catch (all) {
                clientSession?.abortTransaction()
                throw all
            }

            mongoService.commitWithRetry(clientSession, 1)
        } catch (MongoException err) {
            responseData.errorCode = "dbError"
            responseData.debugText = err.response?.errmsg
            render(responseData as JSON)
            logger.error("MongoDB Error", err)
            return false
        } catch (PlatformException err) {
            responseData.errorCode = err.getErrorCode()
            render(responseData as JSON)
            logger.error(err.getMessage(), err)
            return false
        } catch (all) {
            responseData.errorCode = "error"
            render(responseData as JSON)
            logger.error("Unexpected error exception", all)
            return false
        } finally {
            clientSession?.close()
        }

        responseData.success = !(conflict || responseData.mergeToken)
        render(responseData as JSON)

        return false
    }

    def update() {

        mongoService.runTransactionWithRetry({ updateTransaction() }, 1)
    }

    /**
     * Input data looks like this:
     *
     *   {
     *      "components":
     *       {
     *           "account":
     *           {
     *               "data": "{\"foo\":\"bar\"}"
     *           },
     *           "wallet":
     *           {
     *               "data":
     *               {
     *                   "foo" : "bar"
     *               }
     *           },
     *           "deprecated":
     *           {
     *               "delete": true
     *           }
     *       },
     *       "items":
     *       {
     *           "8e6e8be9-8d66-4c54-914c-28a9664e8ff3":
     *           {
     *               "type": "hero",
     *               "info":
     *               {
     *                   "anything": "the game needs"
     *               }
     *           },
     *           "d037804d-de74-4b08-a939-25cba6e8ef9b":
     *           {
     *               "delete": true
     *           }
     *       }
     *   }
     *
     * Note:
     *  - Only components present in the request will be updated (missing ones will not be deleted).
     *  - A component will be deleted if instead of data, the incoming data contains "delete": true
     *  - Component data can either be an embedded list or serialized JSON.
     *  - Attempts to update/delete items that belong to another player will be ignored.
     *  - Deletes of non-existent components and items are ignored. (This API is idempotent.)
     *
     * TODO:
     *  - Record a change log, whether in MongoDB or somewhere else, with automated retention management.
     *  - Add optimistic concurrency control with versioning? Revoke prior auth tokens?
     */
    def updateTransaction() {

        if (!request.getHeader("content-type") == "application/json") {
            throw new BadRequestException("expected content type application/json")
        }

        def accountId = authService.requireClientAuth(request)

        MDC.put('accountId', accountId)

        def requestData = request.JSON

        def responseData = [
                success   : false,
                serverTime: '\'' + System.currentTimeMillis() + '\''
        ]

        def clientSession
        try {
            try {
                clientSession = mongoService.client().startSession()
                clientSession.startTransaction()

                requestData.components?.each { component ->
                    if (component.value.delete == true) {
                        accountService.deleteComponentData(clientSession, accountId, component.key)
                    } else {
                        accountService.saveComponentData(clientSession, accountId, component.key, component.value.data)
                    }
                }

                requestData.items?.each { item ->
                    if (item.value.delete == true) {
                        itemService.deleteItem(clientSession, accountId, item.key)
                    } else {
                        itemService.saveItem(clientSession, accountId, item.key, item.value)
                    }
                }
            } catch (MongoCommandException e) {
                clientSession.abortTransaction()
                throw e
            } catch (all) {
                clientSession?.abortTransaction()
                throw all
            }

            mongoService.commitWithRetry(clientSession, 1)
        } catch (MongoException err) {
            throw new PlatformException('dbError', err.response?.errmsg)
        } finally {
            clientSession?.close()
        }

        responseData.success = true

        render(responseData as JSON)

        return false
    }

    /**
     * Used for client-authoritative games. Supports game component data handling, and reports data version conflicts
     * between devices connected to the same account. Uses multipart request/response to transmit component data.
     */
    def save() {
        mongoService.runTransactionWithRetry({ saveTransaction() }, 1)
    }

    def saveTransaction() {

        def responseData = [
                success   : false,
                remoteAddr: request.remoteAddr,
                geoipAddr : request.remoteAddr,
                country   : 'US',
                serverTime: '\'' + System.currentTimeMillis() + '\'',
                assetPath : 'https://rumble-game-alliance-dist.s3.amazonaws.com/client/',
                clientvars: [:]
        ]

        def boundary = MimeTypeUtils.generateMultipartBoundaryString()
        response.setContentType('multipart/related; boundary="' + boundary + '"')
        response.characterEncoding = StandardCharsets.UTF_8.name()
        def out = response.writer

        def manifest

        if (!params.manifest) {
            responseData.errorCode = "authError"
            sendError(out, boundary, responseData)
            return false
        }

        def slurper = new JsonSlurper()
        manifest = slurper.parseText(params.manifest)

        def identity = manifest.identity ?: [:]

        if (!identity?.installId) {
            throw new BadRequestException('Required parameter identity.installId was not provided.')
        }

        def clientRequestId = identity?.requestId
            def requestId = clientRequestId ?: UUID.randomUUID().toString()
        if (clientRequestId) {
            MDC.put("clientRequestId", clientRequestId)
        }
        responseData.requestId = requestId
        responseData.accountId = identity.installId
        if(identity) {
            if(identity.installId) {
                MDC.put('installId', identity.installId)
            }
            if(identity.clientVersion) {
                MDC.put('clientVersion', identity.clientVersion)
            }
        }

        def mac = checksumService.getChecksumGenerator(identity.installId)
        //TODO: Validate checksums
        def validChecksums = true
        if (!checksumService.validateChecksum(manifest.checksum, checksumService.generateMasterChecksum(manifest.entries, mac))) {
            validChecksums = false
        } else {
            manifest.entries.each { entry ->
                if (validChecksums) {
                    def cs = checksumService.generateComponentChecksum(params.get(entry.name), mac)
                    if (!checksumService.validateChecksum(cs, entry.checksum)) {
                        validChecksums = false
                    }
                }
            }
        }

        if (!validChecksums) {
            responseData.errorCode = "invalidData"
            sendError(out, boundary, responseData)
            return false
        }
        def ipAddr = geoLookupService.getIpAddress(request)

        if (ipAddr) {
            responseData.remoteAddr = ipAddr
            responseData.geoipAddr = ipAddr

            def loc
            try {
                loc = geoLookupService.getLocation(ipAddr)
                if (loc) {
                    responseData.country = loc.getCountry()?.getIsoCode()
                    logger.info("GeoIP lookup results", [ipAddr: ipAddr, country: loc.country.isoCode])
                } else {
                    logger.info("GeoIP lookup failed", [ipAddr: ipAddr])
                }
            } catch (e) {
                logger.warn("Exception looking up geo location for IP Address", all, [ipAddr: ipAddr])
            }
        }


        //TODO: Remove, for testing only
        //manifest.identity.facebook.accessToken = "EAAGfjfXqzCIBAG57lgP2LHg91j96mw1a0kXWXWo9OqzqKGB0VDqQLkOFibrt86fRybpZBHuMZCJ6P7h03KT75wnwLUQPROjyE98iLincC0ZCRAfCvubC77cPoBtE0PGV2gsFjKnMMHKBDrwhGfeN3FZAoiZCEeNWlg91UR6njFZALQOEab7EAuyyH6WkKevYrCIiN5hnOtcGVZCEC82nGH4"

        def channel = identity.channel ?: ""
        def channelScope = "channel:${channel}"
        def channelConfig = dynamicConfigService.getConfig(channelScope)

        //Map channel-specific game identifier to game gukey
        if (identity.gameGukey) {
            game = identity.gameGukey
        }
        String gameGukey = channelConfig["game.${game}.gukey"] ?: game
        def gameConfig = dynamicConfigService.getGameConfig(gameGukey)

        //This looks for variables with a certain prefix (eg_ kr:clientvars:) and puts them in the client_vars structure
        //The prefixes are in a json list, and will be applied in order, overlaying any variable that collides
        def clientVersion = identity.clientVersion
        def prefixes = gameConfig.list("clientVarPrefixes")
        def configs = [channelConfig, gameConfig]
        def clientvars = extractClientVars(clientVersion, prefixes, configs)
        if (clientvars) {
            responseData.clientvars = clientvars
        }

        // Blacklist countries
        if (dynamicConfigService.getConfig('canvas').list('blacklistCountries').contains(responseData.country as String)) {
            responseData.errorCode = "geoblocked"
            responseData.supportUrl = gameConfig['supportUrl']
            sendError(out, boundary, responseData)
            return false
        }

        def conflict = false
        def mani = []
        def entries = [:]
        def entriesChecksums = []
        def clientSession
        try {
            try {
                clientSession = mongoService.client().startSession()
                clientSession.startTransaction()
                def player = accountService.exists(clientSession, identity.installId, identity)
                if (!player) {
                    // Error 'cause upsert failed
                    responseData.errorCode = "dbError"
                    responseData.debugText = err.response?.errmsg
                    logger.error("Probably impossible dbError")
                    throw new PlatformException('dbError', 'Probably impossible dbError', null, responseData)
                }

                def id = player.getObjectId("_id")
                MDC.put('accountId', id?.toString())

                def authHeader = request.getHeader('Authorization')
                try {
                    if (authHeader?.startsWith('Bearer ')) {
                        def accessToken = authHeader.substring(7)
                        def tokenAuth = accessTokenService.validateAccessToken(accessToken, false, false)
                        if ((tokenAuth.aud == game) && (tokenAuth.sub == id.toString())) {
                            def replaceAfter = tokenAuth.exp - gameConfig.long('auth:minTokenLifeSeconds', 172800L)
                            // 2d
                            if (System.currentTimeMillis() / 1000L < replaceAfter) {
                                responseData.accessToken = accessToken
                            }
                        }
                    }
                } catch (Exception e) {
                    logger.error("Exception examining authorization header", e, [header: authHeader])
                }

                if (!responseData.accessToken) {
                    responseData.accessToken = accessTokenService.generateAccessToken(
                            gameGukey, id.toString(), null, gameConfig.long('auth:maxTokenLifeSeconds', 345600L)) // 4d
                }

                //TODO: Validate account
                def validProfiles = profileService.validateProfile(identity)
                /* validProfiles = [
                 *   facebook: FACEBOOK_ID,
                 *   gameCenter: GAMECENTER_ID,
                 *   googlePlay: GOOGLEPLAY_ID
                 * ]
                 */

                if (params.mergeToken) {
                    // Validate merge token
                    def mergeAccountId = params.mergeAccountId
                    if (!mergeAccountId) {
                        throw new BadRequestException('Required parameter mergeAccountId was not provided.')
                    }
                    if (accountService.validateMergeToken(id as String, params.mergeToken, mergeAccountId)) {
                        logger.info("Merge token accepted", [accountId: id, mergeAccountId: responseData.mergeAccountId])
                        id = mergeAccountId
                        responseData.accountId = id.toString()
                        responseData.createdDate = player.cd.toString()

                        profileService.mergeInstallIdProfile(clientSession, id.toString(), identity.installId, identity)

                        // Save over data
                        validProfiles.each { profile, profileData ->
                            profileService.mergeProfile(clientSession, profile, id.toString(), profileData)
                        }

                        def updatedAccount = accountService.updateAccountData(clientSession, id.toString(), identity, manifest.manifestVersion, true)

                        // Send component responses based on entries in manifest
                        manifest.entries.each { component ->
                            accountService.saveComponentData(clientSession, id, component.name, request.getParameter(component.name))

                            // Don't send anything if successful
                            entries[component.name] = ""

                            //TODO: Generate new checksums
                            def cs = [
                                    "name"    : component.name,
                                    "checksum": checksumService.generateComponentChecksum(component, mac) ?: "placeholder"
                            ]
                            entriesChecksums.add(cs)
                        }

                        // Recreate manifest to send back to the client
                        mani = [
                                "identity"       : identity,
                                "entries"        : entriesChecksums,
                                "manifestVersion": updatedAccount?.mv ?: "placeholder", //TODO: Save manifestVersion
                                "checksum"       : checksumService.generateMasterChecksum(entriesChecksums, mac) ?: "placeholder"
                        ]
                    } else {
                        responseData.errorCode = "mergeConflict"
                        throw new PlatformException('mergeConflict', null, null, responseData)
                    }
                } else {
                    if (validProfiles) {
                        def conflictProfiles = []
                        // Get profiles attached to player we found
                        def playerProfiles = profileService.getAccountsFromProfiles(validProfiles)

                        if (playerProfiles) {
                            // Assuming there is only one type of profile for each account, check to see if they conflict
                            // For each valid profile, we need to grab all the accounts that are attached to it
                            // and then compare those Account IDs with the Account ID that matches the Install ID
                            playerProfiles.each { profile ->
                                //TODO: Check for profile conflict
                                if (id.toString() != profile.aid.toString()) {
                                    conflictProfiles << profile
                                }
                            }
                        }

                        if (conflictProfiles && conflictProfiles.size() > 0) {
                            conflict = true
                            responseData.errorCode = "accountConflict"
                            logger.info("Account conflict", [accountId: id.toString()])
                            //TODO: Include which accounts are conflicting? Security concerns?
                            def conflictingAccountIds = conflictProfiles.collect {
                                if (it.aid.toString() != id.toString()) {
                                    return it.aid
                                }
                            } ?: "placeholder"
                            if (conflictingAccountIds.size() > 0) {
                                responseData.conflictingAccountId = conflictingAccountIds.first().toString()
                            }
                        }
                    }

                    // Check for install conflict
                    if (!conflict && accountService.hasInstallConflict(player, manifest.identity)) {
                        conflict = true
                        responseData.errorCode = "installConflict"
                        responseData.conflictingAccountId = id.toString()
                    }

                    // Check for version conflict
                    if (!conflict && accountService.hasVersionConflict(player, manifest.identity)) {
                        conflict = true
                        responseData.errorCode = "versionConflict"
                        responseData.conflictingAccountId = id.toString()
                    }

                    def updatedAccount
                    if (conflict) {
                        // Generate merge token
                        responseData.mergeToken = accountService.generateMergeToken(clientSession, id as String, responseData.conflictingAccountId)
                        logger.info("Merge token generated", [errorCode: responseData.errorCode, accountId: id, mergeAccountId: responseData.conflictingAccountId])
                    } else {
                        // If we've gotten this far, there should be no conflicts, so save all the things
                        // Save Install ID profile
                        profileService.saveInstallIdProfile(clientSession, id.toString(), identity.installId, identity)

                        // Save social profiles
                        validProfiles.each { profile, profileData ->
                            profileService.saveProfile(clientSession, profile, id.toString(), profileData)
                        }

                        // do we really need this update? maybe not on a new player?
                        updatedAccount = accountService.updateAccountData(clientSession, id.toString(), identity, manifest.manifestVersion)
                        responseData.createdDate = updatedAccount.cd?.toString() ?: null
                    }

                    responseData.accountId = id.toString()

                    if (conflict || responseData.mergeToken) {
                        def jsonWriterSettings = new JsonWriterSettings(JsonMode.RELAXED)
                        // Send component responses based on entries in manifest
                        manifest.entries.each { component ->
                            def content = ""
                            // Return the data in the format that the client expects it (which is really just the embedded data field)
                            def c = accountService.getComponentData(responseData.conflictingAccountId ?: id, component.name)
                            if (c && c.size() > 0) {
                                c = c.first()
                            }
                            content = (c) ? c.data.toJson(jsonWriterSettings) ?: c : ""

                            entries[component.name] = content

                            def cs = [
                                    "name"    : component.name,
                                    "checksum": checksumService.generateComponentChecksum(content.toString(), mac) ?: "placeholder"
                            ]
                            entriesChecksums.add(cs)
                        }

                        // Recreate manifest to send back to the client
                        mani = [
                                "identity"       : identity,
                                "entries"        : entriesChecksums,
                                "manifestVersion": updatedAccount?.mv.toString() ?: "placeholder",
                                "checksum"       : checksumService.generateMasterChecksum(entriesChecksums, mac) ?: "placeholder"
                        ]
                    } else {
                        if (manifest.entries) {
                            manifest.entries.each { component ->
                                accountService.saveComponentData(clientSession, id, component.name, request.getParameter(component.name))
                            }
                        }
                    }
                }
            } catch (MongoCommandException e) {
                clientSession.abortTransaction()
                throw e
            } catch (all) {
                clientSession?.abortTransaction()
                throw all
            }

            mongoService.commitWithRetry(clientSession, 1)
        } catch (MongoException err) {
            responseData.errorCode = "dbError"
            responseData.debugText = err.response?.errmsg
            sendError(out, boundary, responseData)
            logger.error("MongoDB Error", err)
            return false
        } catch (PlatformException err) {
            responseData.errorCode = err.getErrorCode()
            sendError(out, boundary, responseData)
            logger.error(err.getMessage(), err)
            return false
        } catch (all) {
            responseData.errorCode = "error"
            sendError(out, boundary, responseData)
            logger.error("Unexpected error exception", all)
            return false
        } finally {
            clientSession?.close()
        }

        responseData.success = !(conflict || responseData.mergeToken)

        out.write('--')
        out.write(boundary)
        out.write('\r\n')
        out.write('Content-Type: application/json')
        out.write('\r\n')
        out.write('Content-Disposition: inline')
        out.write('\r\n')
        out.write('\r\n')
        out.write(JsonOutput.toJson(responseData)) // actual response
        out.flush()

        // Only send the manifest out if there is a conflict or a mergeToken is present
        // Manifest is needed in merges for checksum verification on the client
        if (conflict || responseData.mergeToken) {
            sendFile(out, boundary, "manifest", JsonOutput.toJson(mani))
            entries.each { name, data ->
                sendFile(out, boundary, name, data)
            }
        }

        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('--')
        out.flush()

        return false
    }

    private extractClientVars(clientVersion, List<String> prefixes, configs) {

        def clientVersions = []
        if (clientVersion) {
            clientVersions += [clientVersion]
            while (clientVersion.lastIndexOf('.') > 0) {
                clientVersions += clientVersion = clientVersion.substring(0, clientVersion.lastIndexOf('.'))
            }
        }

        def clientvars = [:]
        prefixes.each { prefix ->
            def defaultVar = prefix + "default:"
            def versionVars = clientVersions.collect { prefix + it + ":" }
            configs.each { config ->
                config.each {
                    k, v ->
                        if (k.startsWith(defaultVar)) clientvars.put(k.replace(defaultVar, ''), v)
                        versionVars.each {
                            if (k.startsWith(it)) clientvars.put(k.replace(it, ''), v)
                        }
                }
            }
        }
        return clientvars
    }

    def read() {

        def accountId = authService.requireClientAuth(request)

        def components = accountService.getDetails(accountId)

        def responseData = [
                success: true,
                data: components.collectEntries {
                    [(it.key): [ data: it.value.data ] ]
                }
        ]

        render responseData as JSON
    }

    def summary() {

        authService.requireClientAuth(request)

        def facebookProfiles
        def responseData = [
                success: true
        ]

        if (!params.accounts && !params.facebook) {
            responseData = [
                    success  : false,
                    errorCode: "invalidRequest"
            ]
        }

        def accounts = []
        def slurper = new JsonSlurper()
        if (params.accounts) {
            def a = slurper.parseText(params.accounts)
            accounts = accountService.validateAccountId(a)
        }


        if (params.facebook) {
            def facebookIds = slurper.parseText(params.facebook)
            facebookProfiles = profileService.getProfilesFromList(ProfileTypes.FACEBOOK, facebookIds)
            accounts += accountService.validateAccountId(facebookProfiles.collect { it.aid })
        }

        if (accounts) {
            def uniqueAccountIds = accounts.unique()
            def summaries = accountService.getComponentData(uniqueAccountIds, "summary")

            // Format summary data and map with fb ids
            def formattedSummaries = summaries.collect { s ->
                def f = [
                        id  : s.aid,
                        fb  : (facebookProfiles.find { s.aid == it.aid })?.pid ?: "",
                        data: s.data
                ]
                return f
            }
            responseData.accounts = formattedSummaries
        } else {
            responseData.accounts = []
        }

        render responseData as JSON
    }

    private def sendError(out, boundary, responseData) {
        out.write('--')
        out.write(boundary)
        out.write('\r\n')
        out.write('Content-Type: application/json')
        out.write('\r\n')
        out.write('Content-Disposition: inline')
        out.write('\r\n')
        out.write('\r\n')
        out.write(JsonOutput.toJson(responseData))
        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('--')
        out.flush()
    }

    private def sendFile(out, boundary, name, content) {
        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('\r\n')
        out.write('Content-Type: application/json')
        out.write('\r\n')
        out.write('Content-Disposition: attachment; name="')
        out.write(name)
        out.write('"; filename="')
        out.write(name)
        out.write('.dat"')
        out.write('\r\n')
        out.write('\r\n')
        out.write(content.toString())
        out.flush()
    }

    def logout() {

        def account = authService.requireClientAuth(request, params.account)

        paramsService.require(params, 'account', 'type')

        def type = params.type

        profileService.deleteProfilesForAccount(account, type)

        render([success: true] as JSON)
    }
}
