package com.rumble.platform.controllers

import grails.converters.JSON

class HealthController {

    def index() {
        render ([healthy: true] as JSON)
    }
}
