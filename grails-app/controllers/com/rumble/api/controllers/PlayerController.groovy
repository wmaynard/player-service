package com.rumble.api.controllers

import com.amazonaws.AmazonServiceException
import com.amazonaws.SdkClientException
import com.amazonaws.services.s3.AmazonS3
import com.amazonaws.services.s3.AmazonS3ClientBuilder
import com.amazonaws.services.s3.model.GetObjectRequest
import com.amazonaws.services.s3.model.ObjectMetadata
import com.rumble.api.services.AccountService
import com.rumble.api.services.ChecksumService
import com.rumble.api.services.ProfileTypes
import com.rumble.geoip.GeoLookupService
import com.rumble.platform.exception.ApplicationException
import com.rumble.platform.services.DynamicConfigService
import grails.converters.JSON
import groovy.json.JsonSlurper
import groovy.json.JsonOutput
import org.apache.commons.io.comparator.LastModifiedFileComparator
import org.springframework.util.MimeTypeUtils

class PlayerController {
    def accessTokenService
    def accountService
    def authService
    def dynamicConfigService = new DynamicConfigService()
    def geoLookupService
    def logger = new com.rumble.Log(this.class)
    def mongoService
    def paramsService
    def profileService

    def game = System.getProperty("GAME_GUKEY") ?: System.getenv('GAME_GUKEY')

    def index(){
        redirect(action: "save", params: params)
    }

    def save() {
        logger.trace("PlayerController:save()")
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

        paramsService.require(params, 'manifest')

        if(!params.manifest) {
            responseData.success = false
            responseData.errorCode = "authError"
            out.write(JsonOutput.toJson(responseData))
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
            out.write(JsonOutput.toJson(responseData))
            out.write('\r\n')
            out.write('--')
            out.write(boundary)
            out.write('--')
            if(mongoService.hasClient()) {
                mongoService.client().close()
            }
            return false
        }

        initGeoIpDb()

        def ipAddr = geoLookupService.getIpAddress(request)
        logger.info("clientIp", [ipAddr: ipAddr])

        if (ipAddr.contains(':')) {
            ipAddr = ipAddr.substring(0, ipAddr.indexOf(':')) // remove port
        }

        if(ipAddr) {
            responseData.remoteAddr = ipAddr
            responseData.geoipAddr = ipAddr

            def loc
            try {
                loc = geoLookupService.getLocation(ipAddr)
                if (loc) {
                    responseData.country = loc.getCountry()?.getIsoCode()
                    logger.info("getLocation", [loc: loc])
                } else {
                    logger.info("Failed to look up geo location for IP Address", [ipAddr: ipAddr])
                }
            } catch (all) {
                logger.warn("Exception looking up geo location for IP Address", all, [ipAddr: ipAddr])
            }
        }

        //TODO: Remove, for testing only
        //manifest.identity.facebook.accessToken = "EAAGfjfXqzCIBAG57lgP2LHg91j96mw1a0kXWXWo9OqzqKGB0VDqQLkOFibrt86fRybpZBHuMZCJ6P7h03KT75wnwLUQPROjyE98iLincC0ZCRAfCvubC77cPoBtE0PGV2gsFjKnMMHKBDrwhGfeN3FZAoiZCEeNWlg91UR6njFZALQOEab7EAuyyH6WkKevYrCIiN5hnOtcGVZCEC82nGH4"

        def channel = manifest.identity.channel ?: ""
        def channelScope = "channel:${channel}"
        def channelConfig = dynamicConfigService.getConfig(channelScope)

        //Map channel-specific game identifier to game gukey
        if(manifest.identity.gameGukey) {
            game = manifest.identity.gameGukey
        }
        String gameGukey = channelConfig["game.${game}.gukey"] ?: game
        def gameConfig = dynamicConfigService.getGameConfig(gameGukey)

        //This looks for variables with a certain prefix (eg_ kr:clientvars:) and puts them in the client_vars structure
        //The prefixes are in a json list, and will be applied in order, overlaying any variable that collides
        def clientVersion = manifest.identity.clientVersion
        def prefixes = gameConfig.list("clientVarPrefixes")
        def configs = [channelConfig, gameConfig]
        def clientvars = extractClientVars(clientVersion, prefixes, configs)
        if(clientvars) {
            responseData.clientvars = clientvars
        }

        // Blacklist countries
        if (dynamicConfigService.getConfig('canvas').list('blacklistCountries').contains(responseData.country as String)) {
            responseData.success = false
            responseData.errorCode = "geoblocked"
            responseData.supportUrl = gameConfig['supportUrl']
            out.write(JsonOutput.toJson(responseData))
            out.write('\r\n')
            out.write('--')
            out.write(boundary)
            out.write('--')
            if(mongoService.hasClient()) {
                mongoService.client().close()
            }
            return false
        }

        def player = accountService.exists(manifest.identity.installId, manifest.identity)
        if(!player) {
            //TODO: Error 'cause upsert failed
            responseData.success = false
            responseData.errorCode = "dbError"
            out.write(JsonOutput.toJson(responseData))
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

        def authHeader = request.getHeader('Authorization')
        try {
            if (authHeader?.startsWith('Bearer ')) {
                def accessToken = authHeader.substring(7)
                def tokenAuth = accessTokenService.validateAccessToken(accessToken, false, false)
                if ((tokenAuth.aud == game) && (tokenAuth.sub == id.toString())) {
                    def replaceAfter = tokenAuth.exp - gameConfig.long('auth:minTokenLifeSeconds',300L)
                    if (System.currentTimeMillis()/1000L < replaceAfter) {
                        responseData.accessToken = accessToken
                    }
                }
            }
        } catch (Exception e) {
            logger.error("Exception examining authorization header", e, [header: authHeader])
        }

        if (!responseData.accessToken) {
            responseData.accessToken = accessTokenService.generateAccessToken(
                    gameGukey, id.toString(), null, gameConfig.long('auth:maxTokenLifeSeconds',172800L))
        }

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
                out.write(JsonOutput.toJson(responseData))

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

                sendFile(out, boundary, "manifest", JsonOutput.toJson(mani))
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
                    logger.info("Account conflict", [accountId:id.toString()])
                    //TODO: Include which accounts are conflicting? Security concerns?
                    def conflictingAccountIds = conflictProfiles.collect{
                        if(it.aid.toString() != id.toString()) { return it.aid }
                    } ?: "placeholder"
                    if(conflictingAccountIds.size() > 0) {
                        responseData.conflictingAccountId = conflictingAccountIds.first()
                        logger.info("Conflicting Account ID", [conflictingAccountId:responseData.conflictingAccountId])
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

                // do we really need this update? maybe not on a new player?
                updatedAccount = accountService.updateAccountData(id.toString(), manifest.identity, manifest.manifestVersion)
                responseData.createdDate = updatedAccount.cd?.toString() ?: null
            }

            responseData.accountId = id.toString()
            out.write(JsonOutput.toJson(responseData)) // actual response

            def entries = [:]
            def entriesChecksums = []
            // Send component responses based on entries in manifest
            manifest.entries.each { component ->
                def content = ""
                if (responseData.errorCode) {
                    // Return the data in the format that the client expects it (which is really just the embedded data field)
                    def c = accountService.getComponentData(responseData.conflictingAccountId ?: id, component.name)
                    if(c && c.size() > 0) {
                        c = c.first()
                    }
                    content = (c) ? c.data ?: c : ""
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

            sendFile(out, boundary, "manifest", JsonOutput.toJson(mani))
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

    static extractClientVars(clientVersion, List<String> prefixes, configs) {
        def clientVersions = [clientVersion]
        while (clientVersion.lastIndexOf('.') > 0) {
            clientVersions += clientVersion = clientVersion.substring(0, clientVersion.lastIndexOf('.'))
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

    def summary(){
        //TODO: authService.checkClientAuth(request)

        def facebookProfiles
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
            accounts = accountService.validateAccountId(a)
        }


        if(params.facebook) {
            def facebookIds = slurper.parseText(params.facebook)
            facebookProfiles = profileService.getProfilesFromList(ProfileTypes.FACEBOOK, facebookIds)
            accounts += accountService.validateAccountId(facebookProfiles.collect{ it.aid })
        }

        def uniqueAccountIds = accounts.unique()
        def summaries = accountService.getComponentData(uniqueAccountIds, "summary")

        // Format summary data and map with fb ids
        def formattedSummaries = summaries.collect{ s ->
            def f = [
                    id: s.aid,
                    fb: (facebookProfiles.find{ s.aid == it.aid })?.pid ?: "",
                    data: s.data
            ]
            return f
        }
        responseData.accounts = formattedSummaries

        render responseData as JSON
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
    }

    private long geoIpInitialized = 0

    private void initGeoIpDb() {

        if (geoIpInitialized > System.currentTimeMillis()-24L*60L*60L*1000L) return;

        String clientRegion = System.getProperty("GEO_IP_S3_REGION") ?: System.getenv("GEO_IP_S3_REGION")
        String bucketName = System.getProperty("GEO_IP_S3_BUCKET") ?: System.getenv("GEO_IP_S3_BUCKET")
        String s3Key = System.getProperty("GEO_IP_S3_KEY") ?: System.getenv("GEO_IP_S3_KEY")

        if (!clientRegion || !bucketName || !s3Key) {
            throw new ApplicationException(null, "Missing environment variable(s)", null, [
                    clientRegion: clientRegion ?: null,
                    bucketName  : bucketName ?: null,
                    s3Key       : s3Key ?: null
            ])
        }

        // Sometimes an existing Lambda instance is being used so check if geo file already exists
        File geoIpDbFile = new File("/var/cache/tomcat8/temp/geo-ip.mmdb")
        AmazonS3 s3Client = AmazonS3ClientBuilder.standard()
                .withRegion(clientRegion)
                //.withCredentials(new ProfileCredentialsProvider())
                .build()

        File folder = new File("/var/cache/tomcat8/temp");
        File[] listOfFiles = folder.listFiles(new FilenameFilter() {
            @Override
            boolean accept(File dir, String name) {
                return name.startsWith("geo-ip") && name.endsWith(".mmdb")
            }
        });
        logger.trace("listOfFiles", [files: listOfFiles])

        try {
            if (listOfFiles?.length > 0) {
                logger.info("Geo IP DB File(s) exists")

                listOfFiles = (LastModifiedFileComparator.LASTMODIFIED_COMPARATOR).sort(listOfFiles);

                // Check if file on S3 is newer
                ObjectMetadata s3MetaData = s3Client.getObjectMetadata(bucketName, s3Key)
                Date s3LastModified = s3MetaData.getLastModified()
                boolean newer = false;
                for (int i = 0; i < listOfFiles.length; i++) {
                    Date geoLastModified = new Date(listOfFiles[i].lastModified())
                    if (i == 0 && geoLastModified.after(s3LastModified)) {
                        geoIpDbFile = listOfFiles[i];
                        logger.info("Using existing db file", [
                                geoIpDbFile: geoIpDbFile
                        ])
                        newer = true;
                    } else {
                        logger.info("Geo IP DB File is newer on S3", [
                                delete: listOfFiles[i].name
                        ])
                        listOfFiles[i].delete()
                    }
                }

                if (!newer) {
                    // Lambda only has permissions to create files in the /tmp folder
                    geoIpDbFile = File.createTempFile("geo-ip", ".mmdb")
                    logger.info("Created temp geoIpDbFile", [
                            geoIpDbFile: geoIpDbFile
                    ])
                    ObjectMetadata metadataObj = s3Client.getObject(new GetObjectRequest(bucketName, s3Key), geoIpDbFile)
                }
            } else {
                logger.info("Geo IP DB File does not exist. Attempting download.")

                // Lambda only has permissions to create files in the /tmp folder
                geoIpDbFile = File.createTempFile("geo-ip", ".mmdb")
                logger.info("Created temp geoIpDbFile", [
                        geoIpDbFile: geoIpDbFile
                ])
                ObjectMetadata metadataObj = s3Client.getObject(new GetObjectRequest(bucketName, s3Key), geoIpDbFile)
            }
        } catch (AmazonServiceException e) {
            // The call was transmitted successfully, but Amazon S3 couldn't process
            // it, so it returned an error response.
            logger.error("Failed downloading Geo IP DB", e,[
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
            throw new ApplicationException(null, "Failed downloading Geo IP DB", e, [
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
        } catch (SdkClientException e) {
            // Amazon S3 couldn't be contacted for a response, or the client
            // couldn't parse the response from Amazon S3.
            logger.error("Failed downloading Geo IP DB", e,[
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
            throw new ApplicationException(null, "Failed connecting to Geo IP DB", e, [
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
        } catch (all) {
            logger.error(all.getMessage(), all,[
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
            throw new ApplicationException(null, all.getMessage(), all, [
                    bucketName : bucketName,
                    s3Key      : s3Key,
                    geoIpDbFile: geoIpDbFile
            ])
        }

        geoLookupService = new GeoLookupService()
        geoLookupService.init(geoIpDbFile)
    }
}
