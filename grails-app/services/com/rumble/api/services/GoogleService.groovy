package com.rumble.api.services

import com.google.api.client.http.javanet.NetHttpTransport
import com.google.api.client.json.jackson2.JacksonFactory
import groovyx.net.http.ContentType
import groovyx.net.http.HTTPBuilder
import groovyx.net.http.Method

import com.google.api.client.googleapis.auth.oauth2.GoogleIdToken;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdToken.Payload;
import com.google.api.client.googleapis.auth.oauth2.GoogleIdTokenVerifier;

class GoogleService {

    def logger = new com.rumble.platform.common.Log(this.class)

    static private GOOGLE_CLIENT_ID = System.getProperty("GOOGLE_CLIENT_ID") ?: System.getenv("GOOGLE_CLIENT_ID")

    def validateAccount(token) {
        if(!token?.idToken) {
            return false
        }
        /*
        // https://developers.google.com/identity/sign-in/android/backend-auth
         */
        GoogleIdTokenVerifier verifier = new GoogleIdTokenVerifier.Builder
                (new NetHttpTransport(), new JacksonFactory()).build();

        GoogleIdToken idToken = verifier.verify(token.idToken);

        if (!idToken) {
            logger.warn("Failed to verify google id token", [
                clientId: GOOGLE_CLIENT_ID,
                idToken: token.idToken
            ])
            return null
        } else if ((idToken.payload.azp != GOOGLE_CLIENT_ID) && (idToken.payload.aud != GOOGLE_CLIENT_ID)) {
            logger.warn("Google play client id mismatch", [
                    clientId: GOOGLE_CLIENT_ID,
                    aud: idToken.payload.aud,
                    azp: idToken.payload.azp
            ])
            return null
        } else {
            return idToken.payload.getSubject()
        }
    }
}
