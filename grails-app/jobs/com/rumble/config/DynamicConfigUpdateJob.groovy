package com.rumble.config

class DynamicConfigUpdateJob {

    def dynamicConfigService

    static triggers = {
      simple repeatInterval: 5000L
    }

    def execute() {
        dynamicConfigService.updateConfigs()
    }
}
