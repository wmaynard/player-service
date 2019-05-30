package com.rumble

import grails.testing.services.ServiceUnitTest
import groovy.json.JsonOutput
import org.bson.types.ObjectId
import spock.lang.Specification

class JsonServiceSpec extends Specification implements ServiceUnitTest<JsonService>{

    def setup() {
    }

    def cleanup() {
    }

    void "test Object IDs"() {
        def id = new ObjectId()
        def json = JsonService.toJson([ "id": id ] )
        println JsonOutput.prettyPrint(json)
        expect:
            json == "{\"id\":\"${id.toString()}\"}"
    }
}
