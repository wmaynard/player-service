package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.MongoException
import com.mongodb.session.ClientSession
import com.rumble.api.controllers.PlayerController
import com.rumble.api.services.AccountService
import com.rumble.api.services.ChecksumService
import com.rumble.api.services.ProfileService
import com.rumble.platform.services.GeoLookupService
import com.rumble.platform.services.AccessTokenService
import com.rumble.platform.services.DynamicConfigService
import com.rumble.platform.services.MongoService
import com.rumble.platform.services.ParamsService
import grails.converters.JSON
import grails.http.client.HttpClientResponse
import grails.http.client.test.TestAsyncHttpBuilder
import grails.testing.web.controllers.ControllerUnitTest
import org.bson.Document
import org.bson.types.ObjectId

import javax.mail.internet.MimeMultipart
import javax.mail.util.ByteArrayDataSource
import spock.lang.Specification

class PlayerControllerSpec extends Specification implements ControllerUnitTest<PlayerController> {

    def setup() {
        controller.accessTokenService = Stub(AccessTokenService)
        controller.accountService = Stub(AccountService) {
            exists(_ as ClientSession, _ as String, *_) >> new Document().append("_id", new ObjectId())
            hasInstallConflict(_,_) >> false
            hasVersionConflict(_,_) >> false
        }
        controller.checksumService = Stub(ChecksumService) {
            validateChecksum(_, _) >> true
        }
        controller.dynamicConfigService = Stub(DynamicConfigService)
        controller.paramsService = Stub(ParamsService)
        controller.geoLookupService = Stub(GeoLookupService)
        controller.mongoService = Spy(MongoService)
        controller.profileService = Stub(ProfileService)

        request.method = 'POST'
        params['manifest'] = '''{
\t"identity":
\t{
\t    "clientVersion": "12345",
        "deviceClass": "IPHONE_X",
        "deviceType": "iPhone",
        "osVersion": "12.1.4",
        "systemLanguage": "English",
\t\t"installId": "INSTALL_ID",
\t\t"advertisingId": "ADVERTISING_ID",
\t\t"facebook":
\t\t{
\t\t\t"accessToken": "ACCESS_TOKEN"
\t\t},
\t\t"gameCenter":
\t\t{
\t\t    "playerId": "",
\t\t\t"publicKeyUrl": "https://static.gc.apple.com/public-key/gc-prod-4.cer",
\t\t\t"signature": "G:1870391344",
\t\t\t"salt": "...",
\t\t\t"timestamp": "1382621610281",
\t\t\t"bundleId": "com.rumble.monsterball"
\t\t},
\t\t"googlePlay":
\t\t{
\t\t\t"idToken": ""
\t\t}
\t},
\t"entries":
\t[
\t    {
\t        "name": "info",
\t        "checksum": ""
\t    },
\t\t{
\t\t    "name": "account",
\t\t\t"checksum": "CHECKSUM"
\t\t},
\t\t{
\t\t    "name": "wallet",
\t\t\t"checksum": "CHECKSUM"
\t\t}
\t],
\t"manifestVersion": 4,
\t"checksum": "MASTER_CHECKSUM"
}'''
    }

    def cleanup() {
    }

    def "save"(){
        when:
        controller.save()

        then:
        def json = parseMultipartResponse(response)
        response.status == 200
        //TODO: json.success == true
    }

    def "install conflict"(){
        given:
        controller.accountService = Stub(AccountService) {
            exists(_ as ClientSession, _ as String, *_) >> new Document().append("_id", new ObjectId())
            hasInstallConflict(_,_) >> true
            hasVersionConflict(_,_) >> false
        }

        when:
        controller.save()

        then:
        def json = parseMultipartResponse(response)
        response.status == 200
        json.errorCode == "installConflict"
    }

    def "version conflict"(){
        given:
        controller.accountService = Stub(AccountService) {
            exists(_ as ClientSession, _ as String, *_) >> new Document().append("_id", new ObjectId())
            hasInstallConflict(_,_) >> false
            hasVersionConflict(_,_) >> true
        }

        when:
        controller.save()

        then:
        def json = parseMultipartResponse(response)
        response.status == 200
        json.errorCode == "versionConflict"
    }

    def "merge conflict"(){
        given:
        params['mergeToken'] = "MERGE_TOKEN"
        controller.accountService = Stub(AccountService) {
            exists(_ as ClientSession, _ as String, *_) >> new Document().append("_id", new ObjectId())
            validateMergeToken(_,_) >> false
        }

        when:
        controller.save()

        then:
        def json = parseMultipartResponse(response)
        response.status == 200
        json.errorCode == "mergeConflict"
    }

    def "mongo exception"(){
        given:
        controller.accountService = Stub(AccountService) {
            exists(_ as ClientSession, _ as String, *_) >> new Document().append("_id", new ObjectId())
            hasInstallConflict(_,_) >> false
            hasVersionConflict(_,_) >> false
            saveComponentData(_ as ClientSession, _, _ as String, _) >> { throw new MongoException("test") }
        }

        when:
        controller.save()

        then:
        def json = parseMultipartResponse(response)
        response.status == 200
        json.success == false
        json.errorCode == "transactionError"
    }

    void "test clientvar extraction"() {
        System.out.println controller.extractClientVars("a.b.c", ["pfxa:","pfxb:"],
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

    def parseMultipartResponse(response){
        def r = response.contentAsString.split(System.getProperty("line.separator"))
        JSON.parse(r[4])
    }
}
