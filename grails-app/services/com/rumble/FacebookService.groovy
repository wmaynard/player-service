package com.rumble

import groovyx.net.http.ContentType
import groovyx.net.http.HTTPBuilder
import groovyx.net.http.Method

class FacebookService {
    static private FB_VALIDATE_TOKEN_URL = System.getProperty("FB_VALIDATE_TOKEN_URL") ?: System.getenv("FB_VALIDATE_TOKEN_URL")

    def validateAccount(token) {
        def http = new HTTPBuilder(FB_VALIDATE_TOKEN_URL)
        http.client.params.setParameter("http.connection.timeout", 5000)
        http.client.params.setParameter("http.socket.timeout", 5000)
        http.request(Method.GET, ContentType.JSON) {
            uri.query = [
                    fields: "id",
                    access_token: token,
            ]

            /*  Facebook Response:
            *   {
                    "id": ""
                }
            * */
            response.success = { resp, reader ->
                return reader.id
            }

            response.failure = { resp, reader ->
                // TODO: Log error
                return false
            }
        }
    }
}
