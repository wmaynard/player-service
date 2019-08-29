package com.rumble.geoip

class GeoLookupUpdateJob {

    def geoLookupService

    static triggers = {
      simple startDelay: 24L*60L*60L*1000L, repeatInterval: 24L*60L*60L*1000L
    }

    def execute() {
        geoLookupService.update()
    }
}
