package com.rumble.api.services

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec


class ChecksumService {

    static def generateMasterChecksum(data, String deviceId) {
        def str = ''
        data.sort({ it.name }).each{ obj ->
            str <<= obj.name
            str <<= obj.checksum
        }

        return generateComponentChecksum(str.toString(), deviceId)
    }

    // Source: https://stackoverflow.com/questions/39355241/compute-hmac-sha512-with-secret-key-in-java
    static def generateComponentChecksum(data, String deviceId) {
        String key =  deviceId + "saltyMcSalterSon"
        byte [] byteKey = key.getBytes("UTF-8")
        final String HMAC_SHA512 = "HmacSHA512"
        def sha512_HMAC = Mac.getInstance(HMAC_SHA512)
        SecretKeySpec keySpec = new SecretKeySpec(byteKey, HMAC_SHA512)
        sha512_HMAC.init(keySpec)

        byte [] mac_data = sha512_HMAC.doFinal(data.bytes)

        def result = toHexString(mac_data)
        return result.replaceAll('-','').toLowerCase()
    }

    private static String toHexString(byte[] bytes) {
        Formatter formatter = new Formatter()
        for (byte b : bytes) {
            formatter.format("%02x", b)
        }
        return formatter.toString()
    }
}
