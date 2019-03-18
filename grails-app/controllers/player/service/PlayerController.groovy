package player.service

import org.springframework.util.MimeTypeUtils

class PlayerController {

    def index() {
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
        out.write(sampleResponseBody)
        request.parameterNames.each { name->
            sendFile(out, boundary, name, request.getParameter(name))
        }
        sendFile(out, boundary, 'sample', sampleResponseBody)
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
        out.write('.json"')
        out.write('\r\n')
        out.write('\r\n')
        out.write(content)
    }

    def sampleResponseBody = '{\n' +
            '\t"success": true,\n' +
            '\t"requestId": "03B31646-611E-4BC3-AF92-8B2982443799",\n' +
            '\t"remoteAddr": "192.168.1.27",\n' +
            '\t"geoipAddr": "55.66.77.88",\n' +
            '\t"country": "US",\n' +
            '\t"accountId": "CB600CC6-FC0C-44EF-A51B-776585E8CBB7",\n' +
            '\t"dateCreated": "13826116102651",\n' +
            '\t"accessToken": "FBDECEFC-AA43-4703-8DDD-4E6EC181BB8F",\n' +
            '\t"assetPath": "https://rumble-game-alliance-dist.s3.amazonaws.com/client/",\n' +
            '\t"clientvars":\n' +
            '\t{\n' +
            '\t\t...\n' +
            '\t}\n' +
            '}'
}
