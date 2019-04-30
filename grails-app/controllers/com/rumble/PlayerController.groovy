package com.rumble

import groovy.json.JsonOutput
import groovy.json.JsonSlurper
import org.springframework.util.MimeTypeUtils

class PlayerController {
    def accountService
    def mongoService
    def profileService

    def index() {
        def manifest
        def responseData = [
                success: true,
                remoteAddr: request.remoteAddr,
                geoipAddr: request.remoteAddr,
                country: 'US',
                dateCreated: '\'' + System.currentTimeMillis() + '\'',
                accessToken: UUID.randomUUID().toString(),
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
        responseData.accessToken = id.toString()

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
                    profileService.saveProfile(profile, id.toString(), profileData)
                }

                def updatedAccount = accountService.updateAccountData(id.toString(), manifest.identity)


                def entries = [:]
                def entriesChecksums = []

                // Send component responses based on entries in manifest
                manifest.entries.each { component, data ->
                    accountService.saveComponentData(id, component, request.getParameter(component))

                    // Don't send anything if successful
                    entries[component.name] = ""

                    //TODO: Generate new checksums
                    def cs = [
                            "name": component.name,
                            "checksum": "placeholder"
                    ]
                    entriesChecksums.add(cs)
                }

                // Recreate manifest to send back to the client
                def mani = [
                        "identity": manifest.identity,
                        "entries": entriesChecksums,
                        "manifestVersion": "placeholder", //TODO: Save manifestVersion
                        "checksum": "placeholder" //TODO: master checksum
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
                def conflictProfiles = [:]
                // Get profiles attached to player we found
                def playerProfiles = profileService.getProfiles(player.getObjectId("_id"))

                if (playerProfiles) {
                    // Assuming there is only one type of profile for each account, check to see if they conflict
                    validProfiles.each { profile, profileData ->
                        //TODO: Check for profile conflict
                        if (profileData != playerProfiles[profile]) {
                            conflictProfiles[profile] = profileData
                        }
                    }
                }

                if (conflictProfiles.size() > 0) {
                    conflict = true
                    responseData.success = false
                    responseData.errorCode = "accountConflict"
                    //TODO: Include which accounts are conflicting? Security concerns?
                    responseData.conflictingAccountId = "placeholder"
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

                def updatedAccount = accountService.updateAccountData(id.toString(), manifest.identity)
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
                    def c = accountService.getComponentData(id, component.name)
                    content = (c) ? c.data ?: c : false
                } else {
                    accountService.saveComponentData(id, component.name, request.getParameter(component.name))
                }

                // Don't send anything if successful
                entries[component.name] = content

                //TODO: Generate checksums
                def cs = [
                        "name": component.name,
                        "checksum": "placeholder"
                ]
                entriesChecksums.add(cs)
            }

            // Recreate manifest to send back to the client
            def mani = [
                    "identity": manifest.identity,
                    "entries": entriesChecksums,
                    "manifestVersion": "placeholder", //TODO: Save manifestVersion
                    "checksum": "placeholder" //TODO: master checksum
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
