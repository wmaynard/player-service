package player.service

import grails.converters.JSON
import org.springframework.util.MimeTypeUtils

class PlayerController {

    def index() {
        def responseData = [
            success: true,
            requestId: '03B31646-611E-4BC3-AF92-8B2982443799',
            remoteAddr: request.remoteAddr,
            geoipAddr: request.remoteAddr,
            country: 'US',
            accountId: JSON.parse(params.manifest).identity.installId,
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
        out.write((responseData as JSON).toString())
        request.parameterNames.each { name->
            sendFile(out, boundary, name, request.getParameter(name))
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
        out.write(content)
    }
}
