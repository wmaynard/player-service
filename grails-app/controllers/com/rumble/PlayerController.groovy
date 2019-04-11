package com.rumble

import grails.converters.JSON
import groovy.json.JsonSlurper
import org.springframework.util.MimeTypeUtils
import com.mongodb.BasicDBObject
import org.bson.types.ObjectId

class PlayerController {
    def profileService
    def mongoService
    def playerService

    def index() {
        def manifest = JSON.parse(params.manifest)
        def requestId = manifest.identity?.requestId ?: UUID.randomUUID().toString()
        def responseData = [
            success: true,
            requestId: requestId,
            remoteAddr: request.remoteAddr,
            geoipAddr: request.remoteAddr,
            country: 'US',
            accountId: manifest.identity.installId,
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

        //TODO: Validate checksums

        def id
        def player = playerService.exists(manifest.identity.installId, manifest.identity)
        def conflict = false
        if(!player) {
            //TODO: Error 'cause upsert failed
            return false
        } else {

            id = player.getObjectId("_id")

            //TODO: Validate account
            def validProfiles = profileService.validateProfile(manifest.identity)
            /* validProfiles = [
             *   facebook: FACEBOOK_ID,
             *   gameCenter: GAMECENTER_ID,
             *   googlePlay: GOOGLEPLAY_ID
             * ]
             */
            if(validProfiles) {
                def conflictProfiles = [:]
                // Get profiles attached to player we found
                def playerProfiles = profileService.getProfiles(player.getObjectId("_id"))

                if(playerProfiles) {
                    // Assuming there is only one type of profile for each account, check to see if they conflict
                    validProfiles.each { profile, profileData ->
                        //TODO: Check for profile conflict
                        if (profileData != playerProfiles[profile]) {
                            conflictProfiles[profile] = profileData
                        }
                    }
                }

                if(conflictProfiles.size() > 0) {
                    conflict = true
                    responseData.success = false
                    responseData.errorCode = "accountConflict"
                    //TODO: Include which accounts are conflicting? Security concerns?
                }
            }

            //TODO: Check for install conflict
            if(!conflict && player.lsi != manifest.identity.installId) {
                conflict = true
                responseData.success = false
                responseData.errorCode = "installConflict"
            }

            //TODO: Check for version conflict
            if(!conflict && player.clientVersion != manifest.identity.clientVersion) {
                conflict = true
                responseData.success = false
                responseData.errorCode = "versionConflict"
            }

            if(conflict) {
                //TODO: Generate merge token
                responseData.mergeToken = playerService.generateMergeToken(id)
            }
        }

        responseData.accountId = id.toString()
        out.write((responseData as JSON).toString()) // actual response

        // Send component responses based on entries in manifest
        manifest.entries.each { component, data ->
            def content = ""
            if (responseData.errorCode) {
            def c = playerService.getComponentData(id, component)
                content = c
            } else {
                playerService.saveComponentData(id, component, request.getParameter(component))
            }

            // Don't send anything if successful
            sendFile(out, boundary, component, content)
        }

        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('--')
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
