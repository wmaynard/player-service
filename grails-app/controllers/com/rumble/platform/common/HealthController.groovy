package com.rumble.platform.common

import grails.converters.JSON

class HealthController {

    def index() {
        render ([healthy: true] as JSON)
    }
}
