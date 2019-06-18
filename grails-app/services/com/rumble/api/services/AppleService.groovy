package com.rumble.api.services

import java.security.InvalidKeyException
import java.security.NoSuchAlgorithmException
import java.security.PublicKey
import java.security.Signature
import java.security.SignatureException
import java.security.cert.CertificateFactory
import java.security.cert.X509Certificate
import java.nio.ByteBuffer
import org.apache.commons.codec.binary.Base64

class AppleService {

    def validateAccount(tokenData) {
        if(!tokenData.playerId && !tokenData.publicKeyUrl && !tokenData.signature && !tokenData.salt && !tokenData.timestamp && !tokenData.bundleId) {
            return false
        }

        def signedData = generateEncodedSignedDataForAppleVerify(tokenData.playerId, tokenData.bundleId, tokenData.timestamp, tokenData.salt)

        return verifyGamecenterUser(tokenData.publicKeyUrl, signedData, tokenData.signature)
    }

    // Source: https://stackoverflow.com/questions/26900116/documentation-on-public-key-returned-by-gklocalplayer-generateidentityverifica
    static String generateEncodedSignedDataForAppleVerify(String player_id, String bundle_id, String timestamp, String salt){
        def decoder = java.util.Base64.getDecoder()
        return new String(Base64.encodeBase64(concat(player_id.getBytes(),
                bundle_id.getBytes(),
                ByteBuffer.allocate(8).putLong(Long.parseLong(timestamp)).array(),
                decoder.decode(salt))))
    }

    static def getCertificate(publicKeyUrl) {
        // TODO: Cache certificate

        URL url = new URL(publicKeyUrl)

        HttpURLConnection urlConn = (HttpURLConnection) url.openConnection()
        HttpURLConnection httpConn = (HttpURLConnection) urlConn
        httpConn.setAllowUserInteraction(false)
        httpConn.connect()

        InputStream inputStream = httpConn.getInputStream()
        CertificateFactory cf = CertificateFactory.getInstance("X509")
        X509Certificate certificate = (X509Certificate) cf.generateCertificate(inputStream)
        inputStream.close()
        httpConn.disconnect()

        return certificate
    }

    static boolean verifyGamecenterUser(String publicKeyUrl, String signedData, String signature){
        try{
            // Get certificate from apple
            def c = getCertificate(publicKeyUrl)

            // Extract Public Key from certificate
            PublicKey key22 = c.getPublicKey()

            byte[] result = Base64.decodeBase64(signedData)
            byte[] decodedSignature = Base64.decodeBase64(signature)

            // Prepare signature
            Signature sig
            try {
                sig = Signature.getInstance("SHA256withRSA")
                sig.initVerify(key22)
                sig.update(result)
                if (!sig.verify(decodedSignature)) {
                    return false
                } else {
                    return true
                }
            } catch (NoSuchAlgorithmException e) {
                e.printStackTrace()
            } catch (InvalidKeyException e) {
                e.printStackTrace()
            } catch (SignatureException e) {
                e.printStackTrace()
            }
        } catch(Exception e){
            e.printStackTrace()
        }

        return false
    }

    static byte[] concat(byte[]...arrays)
    {
        // Determine the length of the result array
        int totalLength = 0
        for (int i = 0; i < arrays.length; i++) {
            totalLength += arrays[i].length
        }

        // create the result array
        byte[] result = new byte[totalLength]

        // copy the source arrays into the result array
        int currentIndex = 0
        for (int i = 0; i < arrays.length; i++) {
            System.arraycopy(arrays[i], 0, result, currentIndex, arrays[i].length)
            currentIndex += arrays[i].length
        }

        return result
    }
}
