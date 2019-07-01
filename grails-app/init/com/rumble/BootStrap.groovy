package com.rumble

import com.mongodb.DBObject
import grails.converters.JSON
import org.bson.types.ObjectId

class BootStrap {

    def init = { servletContext ->
        requireSystemProperty('RUMBLE_CONFIG_SERVICE_URL')
        requireSystemProperty('RUMBLE_KEY')

        // MongoDB
        requireSystemProperty('MONGODB_URI')
        requireSystemProperty('MONGODB_NAME')

        // Facebook
        requireSystemProperty('FB_VALIDATE_TOKEN_URL')
        requireSystemProperty('FB_APP_ID')
        requireSystemProperty('FB_APP_SECRET')

        // Google Play
        requireSystemProperty('GOOGLE_VALIDATE_TOKEN_URL')
        requireSystemProperty('GOOGLE_APP_ID')
        requireSystemProperty('GOOGLE_APP_SECRET')

        // geo-ip
        requireSystemProperty('GEO_IP_S3_REGION')
        requireSystemProperty('GEO_IP_S3_BUCKET')
        requireSystemProperty('GEO_IP_S3_KEY')

        JSON.registerObjectMarshaller(ObjectId) {
            return it.toString()
        }
    }

    def destroy = {
    }

    def requireSystemProperty(name) {
        if (!System.properties.get(name)) {
            throw new RuntimeException("Required system property ${name} is not set.")
        }
    }
}
