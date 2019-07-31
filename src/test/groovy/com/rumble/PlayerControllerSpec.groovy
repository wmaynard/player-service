package com.rumble

import com.rumble.api.controllers.PlayerController
import grails.converters.JSON
import grails.testing.web.controllers.ControllerUnitTest
import spock.lang.Specification

class PlayerControllerSpec extends Specification implements ControllerUnitTest<PlayerController> {

    def setup() {
    }

    def cleanup() {
    }

    void "test clientvar extraction"() {
        System.out.println PlayerController.extractClientVars("a.b.c", ["pfxa:","pfxb:"],
                [
                        [
                            'pfxa:a:test1':'ok',
                            'pfxa:a.b:test2':'ok',
                            'pfxa:a.b.c:test3':'ok',
                            'pfxa:a:test4':'bad',
                            'pfxa:a.b:test4':'ok',
                            'pfxa:a:test5':'bad',
                            'pfxa:a.b.c:test5':'ok',
                            'pfxa:a:test6':'bad',
                            'pfxa:a.b:test6':'bad',
                            'pfxa:a.b.c:test6':'ok',
                        ]
                ]
        ) as JSON
        expect: true
    }
}
