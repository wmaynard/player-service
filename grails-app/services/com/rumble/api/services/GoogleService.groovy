package com.rumble.api.services

import groovyx.net.http.ContentType
import groovyx.net.http.HTTPBuilder
import groovyx.net.http.Method

class GoogleService {

    def logger = new com.rumble.platform.common.Log(this.class)

    static private VALIDATE_TOKEN_URL = System.getProperty("GOOGLE_VALIDATE_TOKEN_URL") ?: System.getenv("GOOGLE_VALIDATE_TOKEN_URL")
    static private APP_ID = System.getProperty("GOOGLE_APP_ID") ?: System.getenv("GOOGLE_APP_ID")
    def validateAccount(token) {
        if(!token?.idToken) {
            return false
        }
        def http = new HTTPBuilder(VALIDATE_TOKEN_URL + APP_ID + "/verify/")
        http.client.params.setParameter("http.connection.timeout", 5000)
        http.client.params.setParameter("http.socket.timeout", 5000)
        http.request(Method.GET, ContentType.JSON) {
            headers.Authorization = "OAuth ${token.idToken}"

            // example response:
            // {{
            //  "kind": "games#applicationVerifyResponse",
            //  "player_id": "g11282747315603104675"
            // }}
            response.success = { resp, reader ->
                return reader.player_id
            }

            response.failure = { resp, reader ->
                logger.error("Google Play verify request failed", reader?.error)
                return false
            }
        }
    }
}
