package com.rumble

import com.rumble.platform.services.DynamicConfigService
import groovy.json.JsonSlurper
import org.springframework.util.MimeTypeUtils

class PlayerController {
    def accountService
    def dynamicConfigService = new DynamicConfigService()
    def mongoService
    def profileService
    def accessTokenService

    def game = System.getProperty("GAME_GUKEY") ?: System.getenv('GAME_GUKEY')

    def index(){
        redirect(action: "save", params: params)
    }

    def save() {
        def manifest
        def responseData = [
                success: true,
                remoteAddr: request.remoteAddr,
                geoipAddr: request.remoteAddr,
                country: 'US',
                dateCreated: '\'' + System.currentTimeMillis() + '\'',
                assetPath: 'https://rumble-game-alliance-dist.s3.amazonaws.com/client/',
                clientvars: [:]
        ]

        def boundary = MimeTypeUtils.generateMultipartBoundaryString()
        response.setContentType('multipart/related; boundary="' + boundary + '"')
        def out = response.writer
        out.write('--')
        out.write(boundary)
        out.write('\r\n')
        out.write('Content-Type: text/html')
        out.write('\r\n')
        out.write('Content-Disposition: inline')
        out.write('\r\n')
        out.write('\r\n')

        if(!params.manifest) {
            responseData.success = false
            responseData.errorCode = "authError"
            out.write(JsonService.toJson(responseData))
            out.write('\r\n')
            out.write('--')
            out.write(boundary)
            out.write('--')
            if(mongoService.hasClient()) {
                mongoService.client().close()
            }
            return false
        } else {
            def slurper = new JsonSlurper()
            manifest = slurper.parseText(params.manifest)
            def requestId = manifest.identity?.requestId ?: UUID.randomUUID().toString()
            responseData.requestId = requestId
            responseData.accountId = manifest.identity.installId
        }

        //TODO: Validate checksums
        def validChecksums = true
        if(ChecksumService.generateMasterChecksum(manifest.entries, manifest.identity.installId) != manifest.checksum) {
            validChecksums = false
        } else {
            manifest.entries.each { entry ->
                if (validChecksums) {
                    def cs = ChecksumService.generateComponentChecksum(params.get(entry.name), manifest.identity.installId)
                    if (entry.checksum != cs) {
                        validChecksums = false
                    }
                }
            }
        }

        if(!validChecksums) {
            responseData.success = false
            responseData.errorCode = "invalidData"
            out.write(JsonService.toJson(responseData))
            out.write('\r\n')
            out.write('--')
            out.write(boundary)
            out.write('--')
            if(mongoService.hasClient()) {
                mongoService.client().close()
            }
            return false
        }

        def channel = manifest.identity.channel ?: ""
        def channelScope = "channel:${channel}"
        def channelConfig = dynamicConfigService.getConfig(channelScope)

        //Map channel-specific game identifier to game gukey
        if(manifest.identity.gameGukey) {
            game = manifest.identity.gameGukey
        }
        String gameGukey = channelConfig["game.${game}.gukey"] ?: game
        def gameScope = "game:${gameGukey}"
        def gameConfig = dynamicConfigService.getGameConfig(gameGukey)

        //This looks for variables with a certain prefix (eg_ kr:clientvars:) and puts them in the client_vars structure
        //The prefixes are in a json list, and will be applied in order, overlaying any variable that collides
        def clientVarPrefixes = gameConfig.list("clientVarPrefixes")
        def clientvars = [:]
        clientVarPrefixes.each {
            l ->
                def defaultVar = l + "default:"
                def versionVar = l + manifest.identity.clientVersion + ":"
                channelConfig.each {
                    k, v ->
                        if (k.startsWith(defaultVar)) clientvars.put(k.replace(defaultVar, ''), v)
                        if (k.startsWith(versionVar)) clientvars.put(k.replace(versionVar, ''), v)
                }
                gameConfig.each {
                    k, v ->
                        if (k.startsWith(defaultVar)) clientvars.put(k.replace(defaultVar, ''), v)
                        if (k.startsWith(versionVar)) clientvars.put(k.replace(versionVar, ''), v)
                }
        }
        if(clientvars) {
            responseData.clientvars = clientvars
        }

        def player = accountService.exists(manifest.identity.installId, manifest.identity)
        if(!player) {
            //TODO: Error 'cause upsert failed
            responseData.success = false
            responseData.errorCode = "dbError"
            out.write(JsonService.toJson(responseData))
            out.write('\r\n')
            out.write('--')
            out.write(boundary)
            out.write('--')
            if(mongoService.hasClient()) {
                mongoService.client().close()
            }
            return false
        }

        def conflict = false
        def id = player.getObjectId("_id")

        responseData.accessToken = accessTokenService.generateAccessToken(gameGukey, id.toString())

        //TODO: Validate account
        def validProfiles = profileService.validateProfile(manifest.identity)
        /* validProfiles = [
         *   facebook: FACEBOOK_ID,
         *   gameCenter: GAMECENTER_ID,
         *   googlePlay: GOOGLEPLAY_ID
         * ]
         */
        if(params.mergeToken) {
            // Validate merge token
            if(accountService.validateMergeToken(id, params.mergeToken)) {
                responseData.accountId = id.toString()
                responseData.createdDate = player.cd.toString()
                out.write(JsonService.toJson(responseData))

                profileService.saveInstallIdProfile(id.toString(), manifest.identity.installId, manifest.identity)

                // Save over data
                validProfiles.each { profile, profileData ->
                    profileService.mergeProfile(profile, id.toString(), profileData)
                }

                def updatedAccount = accountService.updateAccountData(id.toString(), manifest.identity, manifest.manifestVersion, true)

                def entries = [:]
                def entriesChecksums = []

                // Send component responses based on entries in manifest
                manifest.entries.each { component ->
                    accountService.saveComponentData(id, component.name, request.getParameter(component.name))

                    // Don't send anything if successful
                    entries[component.name] = ""

                    //TODO: Generate new checksums
                    def cs = [
                            "name": component.name,
                            "checksum": ChecksumService.generateComponentChecksum(component, manifest.identity.installId) ?: "placeholder"
                    ]
                    entriesChecksums.add(cs)
                }

                // Recreate manifest to send back to the client
                def mani = [
                        "identity": manifest.identity,
                        "entries": entriesChecksums,
                        "manifestVersion": updatedAccount?.mv ?: "placeholder", //TODO: Save manifestVersion
                        "checksum": ChecksumService.generateMasterChecksum(entriesChecksums, manifest.identity.installId) ?: "placeholder"
                ]

                sendFile(out, boundary, "manifest", JsonService.toJson(mani))
                entries.each { name, data ->
                    sendFile(out, boundary, name, data)
                }
            } else {
                responseData.success = false
                responseData.errorCode = "mergeConflict"
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
                    responseData.success = false
                    responseData.errorCode = "accountConflict"
                    //TODO: Include which accounts are conflicting? Security concerns?
                    def conflictingAccountIds = conflictProfiles.collect{
                        if(it.aid.toString() != id.toString()) { return it.aid }
                    } ?: "placeholder"
                    if(conflictingAccountIds.size() > 0) {
                        responseData.conflictingAccountId = conflictingAccountIds.first()
                    }
                }
            }

            //TODO: Check for install conflict
            if (!conflict && player.lsi != manifest.identity.installId) {
                conflict = true
                responseData.success = false
                responseData.errorCode = "installConflict"
            }

            //TODO: Check for version conflict
            if (!conflict && player.dv > manifest.identity.dataVersion) {
                conflict = true
                responseData.success = false
                responseData.errorCode = "versionConflict"
            }

            def updatedAccount
            if (conflict) {
                //TODO: Generate merge token
                responseData.mergeToken = accountService.generateMergeToken(id)
            } else {
                // If we've gotten this far, there should be no conflicts, so save all the things
                // Save Install ID profile
                profileService.saveInstallIdProfile(id.toString(), manifest.identity.installId, manifest.identity)

                // Save social profiles
                validProfiles.each { profile, profileData ->
                    profileService.saveProfile(profile, id.toString(), profileData)
                }

                updatedAccount = accountService.updateAccountData(id.toString(), manifest.identity, manifest.manifestVersion)
                responseData.createdDate = updatedAccount.cd?.toString() ?: null
            }

            responseData.accountId = id.toString()
            out.write(JsonService.toJson(responseData)) // actual response

            def entries = [:]
            def entriesChecksums = []
            // Send component responses based on entries in manifest
            manifest.entries.each { component ->
                def content = ""
                if (responseData.errorCode) {
                    // Return the data in the format that the client expects it (which is really just the embedded data field)
                    def c = accountService.getComponentData(responseData.conflictingAccountId ?: id, component.name)?.first()
                    content = (c) ? c.data ?: c : false
                } else {
                    accountService.saveComponentData(id, component.name, request.getParameter(component.name))
                }

                // Don't send anything if successful
                entries[component.name] = content

                def cs = [
                        "name": component.name,
                        "checksum": ChecksumService.generateComponentChecksum(content.toString(), manifest.identity.installId) ?: "placeholder"
                ]
                entriesChecksums.add(cs)
            }

            // Recreate manifest to send back to the client
            def mani = [
                    "identity": manifest.identity,
                    "entries": entriesChecksums,
                    "manifestVersion": updatedAccount?.mv.toString() ?: "placeholder",
                    "checksum": ChecksumService.generateMasterChecksum(entriesChecksums, manifest.identity.installId) ?: "placeholder"
            ]

            sendFile(out, boundary, "manifest", JsonService.toJson(mani))
            entries.each { name, data ->
                sendFile(out, boundary, name, data)
            }
        }

        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('--')
        if(mongoService.hasClient()) {
            mongoService.client().close()
        }
        return false
    }

    def summary(){
        def responseData = [
                success: true
        ]

        if(!params.accounts && !params.facebook) {
            responseData = [
                    success: false,
                    errorCode: "invalidRequest"
            ]
        }

        def accounts = []
        def slurper = new JsonSlurper()
        if(params.accounts) {
            def a = slurper.parseText(params.accounts)
            accounts = AccountService.validateAccountId(a)
        }


        if(params.facebook) {
            def facebookIds = slurper.parseText(params.facebook)
            def profiles = profileService.getProfilesFromList(ProfileTypes.FACEBOOK, facebookIds)
            accounts += profiles.collect{ it.aid.toString() }
        }

        def uniqueAccountIds = accounts.unique()
        def summaries = accountService.getComponentData(uniqueAccountIds, "summary")
        responseData.accounts = summaries

        render JsonService.toJson(responseData)
    }

    def sendFile(out, boundary, name, content) {
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
    }
}
