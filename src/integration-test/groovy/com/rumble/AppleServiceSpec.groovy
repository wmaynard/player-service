package com.rumble

import grails.testing.mixin.integration.Integration
import org.springframework.beans.factory.annotation.Autowired
import spock.lang.Specification

@Integration
class AppleServiceSpec extends Specification {

    @Autowired
    AppleService service

    static def APPLE_PUBLIC_KEY_URL = "https://static.gc.apple.com/public-key/gc-prod-4.cer"

    def setup() {
    }

    def cleanup() {
    }

    void "test encoded signed data"() {
        def data = service.generateEncodedSignedDataForAppleVerify("12345", "com.rumble.monsterball", "1382621610281", "dGVzdA==")

        expect:
            data == "MTIzNDVjb20ucnVtYmxlLm1vbnN0ZXJiYWxsAAABQeqrgSl0ZXN0"
    }

    void "test GameCenter user verify"() {
        def signedData = service.generateEncodedSignedDataForAppleVerify("12345", "com.rumble.monsterball", "1382621610281", "dGVzdA==")
        def signature = "G:1870391344"
        def valid = service.verifyGamecenterUser(APPLE_PUBLIC_KEY_URL, signedData, signature)

        expect:
            valid == false
    }
}
