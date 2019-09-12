package com.rumble.api.services

import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec


class ChecksumService {
    final String HMAC_ALGO = "HmacSHA512"

    def mac = new ThreadLocal<Mac> () {
        @Override
        protected Mac initialValue() {
            return Mac.getInstance(HMAC_ALGO)
        }
    }

    def getChecksumGenerator(String deviceId) {
        String key =  deviceId + "saltyMcSalterSon"
        byte [] byteKey = key.getBytes("UTF-8")
        SecretKeySpec keySpec = new SecretKeySpec(byteKey, HMAC_ALGO)
        def mac = mac.get()
        mac.init(keySpec)
        return mac
    }

    def generateMasterChecksum(data, mac) {
        def str = ''
        data.sort({ it.name }).each{ obj ->
            str <<= obj.name
            str <<= obj.checksum
        }

        return generateComponentChecksum(str.toString(), mac)
    }

    def generateComponentChecksum(data, mac) {
        byte [] mac_data = mac.doFinal(data.bytes)
        def result = toHexString(mac_data)
        return result.replaceAll('-','').toLowerCase()
    }

    /**
     * Convenience method to force outcome during tests
     *
     * @param given
     * @param generated
     * @return boolean
     */
    boolean validateChecksum(given, generated) {
        return (given == generated)
    }

    private static String toHexString(byte[] bytes) {
        Formatter formatter = new Formatter()
        for (byte b : bytes) {
            formatter.format("%02x", b)
        }
        return formatter.toString()
    }
}
