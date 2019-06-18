package com.rumble.api.controllers

import grails.converters.JSON

class AdminPlayerController {
    def accountService
    def profileService

    def search() {
        def results = accountService.find(params.s)
        def responseData = [
                'results': results
        ]
        render responseData as JSON
    }

    def details(){
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
