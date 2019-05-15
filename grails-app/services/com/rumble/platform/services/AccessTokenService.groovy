package com.rumble.platform.services

import com.nimbusds.jose.JWSAlgorithm
import com.nimbusds.jose.JWSHeader
import com.nimbusds.jose.crypto.RSASSASigner
import com.nimbusds.jose.crypto.RSASSAVerifier
import com.nimbusds.jose.jwk.JWKSet
import com.nimbusds.jose.jwk.RSAKey
import com.nimbusds.jwt.JWTClaimsSet
import com.nimbusds.jwt.SignedJWT

class AccessTokenService {

    def dynamicConfigService

    def getJWK(String gameId, String keyId) {

        // TODO: cache maybe, though this parsing *is* really fast (0-2ms)

        def json = (String)dynamicConfigService.getGameConfig(gameId).get('jwtKeys')

        return (RSAKey)JWKSet.parse(json).getKeyByKeyId(keyId)
    }

    def generateAccessToken(String gameId, String accountId) {

        try { // TODO: take this try/catch out once this can be trusted

        def keyId = 'jwt'

        def rsaJWK = getJWK(gameId, keyId)

        def signer = new RSASSASigner(rsaJWK);

        def now = new Date()

        def claimsSet = new JWTClaimsSet.Builder()
                .issuer("Rumble Player Service")
                .subject(accountId)
                .audience(gameId)
                .issueTime(now)
                .expirationTime(new Date(now.getTime() + 60L * 60L * 1000L))
                .claim("key", keyId)
                .build();

        def signedJWT = new SignedJWT(
                new JWSHeader.Builder(JWSAlgorithm.RS256).keyID(rsaJWK.getKeyID()).build(),
                claimsSet);

        signedJWT.sign(signer);

        return signedJWT.serialize()

        } catch (Exception e) {
            e.printStackTrace()
            return accountId
        }
    }

    def validateAccessToken(String accessToken) {

        def signedJWT = SignedJWT.parse(accessToken);

        def claims = signedJWT.getJWTClaimsSet()

        def rsaJWK = getJWK(claims.getAudience().getAt(0), claims.getStringClaim('key'))

        def verifier = new RSASSAVerifier(rsaJWK);

        if (signedJWT.verify(verifier) && signedJWT.getJWTClaimsSet().getExpirationTime().compareTo(new Date()) > 0) {
            return [
                    gameId: claims.getAudience().getAt(0),
                    accountId: claims.getSubject()
            ]
        } else {
            return [:]
        }
    }
}
