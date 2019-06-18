package com.rumble

import com.rumble.api.services.ChecksumService
import grails.testing.services.ServiceUnitTest
import spock.lang.Specification

class ChecksumServiceSpec extends Specification implements ServiceUnitTest<ChecksumService>{

    ChecksumService service = new ChecksumService()

    def setup() {
    }

    def cleanup() {
    }

    void "test generateComponentChecksum"() {
        def data = "{\n" +
                "    \"componentVersion\": 2,\n" +
                "    \"screenName\": \"\",\n" +
                "    \"avatar\": \"\",\n" +
                "    \"mapLevel\": \"\",\n" +
                "    \"mapProgressIndex\": 9,\n" +
                "    \"eventProgressIndex\": 1,\n" +
                "    \"eventLevelExpiration\": \"132013152000000000\",\n" +
                "    \"seenCurrentEvent\": false\n" +
                "}"

        def result = service.generateComponentChecksum(data, "18e6ac7b555154bdf9dbe6ccae9bacca")
        expect:
            result == "bb0d9cdea1494bbcbef6f6768c4ba76db8cff5d7baf66c79418ec033df8d8e0eb7b427f950734daa760ad678d322aa81ea15475f394aa38c052d6864829a6ff3"
    }

    void "test generateMasterChecksum"() {
        def checksums = [
                [
                    "name": "account",
                    "checksum": "bb0d9cdea1494bbcbef6f6768c4ba76db8cff5d7baf66c79418ec033df8d8e0eb7b427f950734daa760ad678d322aa81ea15475f394aa38c052d6864829a6ff3"
                ],
                [
                    "name": "wallet",
                    "checksum": "584d3f7a729d7bf4ff8920d705ccc6bf9d2e34ab92021ef64c362a7093012069901e4ce6f8be8c7bb44b159cd3f90fe70362432b3487ed3a191c2aeaac1a7265"
                ],
                [
                    "name": "heroes",
                    "checksum": "fb5f85a793b179e833c77f183b93237a5eab5f47a36ca5fb1f5cbf995486763895e9763a574385b73c136f255fa28f8284cf6e24f24ba4bfdf92745e692ff604"
                ],
                [
                    "name": "chests",
                    "checksum": "c4f92fe69b5585e60a6c499b0527c4e2e5727078513de094a5538e064688378dddf58b9b271435a4d26d4e5264fb87e92f70dfe06f84210e4de0281b01abb737"
                ],
                [
                    "name": "tutorials",
                    "checksum": "73847a8f70bd819bf085e3de68cb3b422f485d463ca7d366b8377171ee0a98314738507f31294ed14453cb272ad095e89ba1d828f9b2dd2c02f47004f70c1ce7"
                ]
        ]

        def result = service.generateMasterChecksum(checksums, "18e6ac7b555154bdf9dbe6ccae9bacca")
        expect:
        result == "b545bf77b1f30d893f0d582abda34d200b497eee8dda84a546c7dfe2e6a5dc4d2da2590352ec70db6037c944ff7c2ce97287169446b1d4af16108d301f1d2017"
    }
}
