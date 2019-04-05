package player.service

import grails.converters.JSON
import groovy.json.JsonSlurper
import org.springframework.util.MimeTypeUtils
import com.mongodb.*
import com.mongodb.BasicDBObject

class PlayerController {
    def mongoService
    def playerService

    def index() {
        def manifest = JSON.parse(params.manifest)
        def responseData = [
            success: true,
            requestId: '03B31646-611E-4BC3-AF92-8B2982443799',
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
            id = player.getObjectId("_id")
        }

        responseData.accountId = id.toString()
        out.write((responseData as JSON).toString()) // actual response

        // Send component responses based on entries in manifest
        manifest.entries.each { component, data ->
            saveComponentData(id, component, request.getParameter(component))

            // Don't send anything if successful
            sendFile(out, boundary, component, "")
            //TODO: Send conflicting data
        }

        out.write('\r\n')
        out.write('--')
        out.write(boundary)
        out.write('--')
        return false
    }

    //TODO: This shouldn't be here.
    def saveComponentData(accountId, String collection, data) {
        System.out.println("saveComponentData")
        def coll = mongoService.collection(collection)
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject("accountId", accountId)
                .append("data", jsonSlurper.parseText(data))
        System.out.println(doc.toString())
        coll.insert(doc)
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
        out.write(content)
    }
}
