package com.rumble

import grails.converters.JSON
import groovy.json.JsonSlurper
import org.springframework.util.MimeTypeUtils
import com.mongodb.BasicDBObject
import org.bson.types.ObjectId

class PlayerController {
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

        //TODO: Change this to an upsert for race conditions
        def id
        def player = playerService.exists(manifest.identity.installId)
        if(!player) {
            id = playerService.create(manifest.identity.installId, manifest.identity)
        } else {
            def conflict = false
            id = player.getObjectId("_id")

            //TODO: Check for account conflict

            //TODO: Check for install conflict
            if(player.identity.installId != manifest.identity.installId) {
                conflict = true
                responseData.success = false
                responseData.errorCode = "installConflict"
            }

            //TODO: Check for version conflict
            if(player.identity.clientVersion != manifest.identity.clientVersion) {
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
            def c = playerService.getComponentData(id, component)
            if(!c) {
                playerService.saveComponentData(id, component, request.getParameter(component))
            } else {
                content = c
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
