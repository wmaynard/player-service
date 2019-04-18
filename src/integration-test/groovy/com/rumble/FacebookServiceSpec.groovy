package com.rumble

import grails.testing.mixin.integration.Integration
import org.springframework.beans.factory.annotation.Autowired
import spock.lang.Specification

@Integration
class FacebookServiceSpec extends Specification {

    @Autowired
    FacebookService service

    def setup() {
    }

    def cleanup() {
    }

    def "test validateAccount"() {
        def valid = service.validateAccount("EAAFGCUWA9z0BAIY8vZCbKlK9X4hQiQkWVA9eYFw4GvudXmhUjUvrZBt5AbWOIZCC1bK3wI9BThBlcb28ZCqZAWNInVy6nzpJsdIVLz4gZBLmhXYX4jIiCa5R2PQaGEULVXRokRJy8lrRdcCScC2n9cQsvTP1BkBjU8vjJyGwbF3lS7kKBubxJixi23EjE98HI2ZA2bewWU7Q5Bh7z15Fq1V")

        expect:
            valid == "106804767185697"
    }
}
