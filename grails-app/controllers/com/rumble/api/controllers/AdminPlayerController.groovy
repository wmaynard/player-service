package com.rumble.api.controllers

import grails.converters.JSON

class AdminPlayerController {
    def accountService
    def paramsService
    def profileService

    def search() {
        paramsService.require(params, 's')

        def results = accountService.find(params.s)
        def responseData = [:]
        if(results) {
            responseData.results = results
        } else {
            responseData.errorText = "Player '${params.s}' not found."
        }
        render responseData as JSON
    }

    def details(){
        paramsService.require(params, 'id')

        def account = accountService.find(params.id)?.first()
        def components = accountService.getDetails(params.id)
        def profiles = profileService.getProfilesForAccount(params.id)

        def responseData = [
                'data': [
                        account: account,
                        components: components,
                        profiles: profiles
                ]
        ]

        render responseData as JSON
    }
}
