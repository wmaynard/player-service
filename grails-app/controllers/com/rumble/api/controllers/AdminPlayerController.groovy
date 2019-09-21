package com.rumble.api.controllers

import com.rumble.api.services.ProfileTypes
import grails.converters.JSON

class AdminPlayerController {
    def accountService
    def authService
    def paramsService
    def profileService

    def search() {

        authService.checkServerAuth(request)

        paramsService.require(params, 's')

        def results
        def responseData = [:]
        if(params.profileType) { // Facebook ID
            if(params.profileType != ProfileTypes.FACEBOOK && params.profileType != ProfileTypes.INSTALL_ID) {
                responseData.errorText = "Invalid profile type '${params.profileType}'."
            } else {
                /* profiles = {
                *   "facebook" : "1234567890"
                *  } */
                def query = [:]
                query[params.profileType] = params.s
                def profiles = profileService.getAccountsFromProfiles(query)

                // Get account data from this list
                def p = profiles.collect{ it.aid }
                if(p.size() == 1) {
                    results = accountService.find(p.first())
                } else {
                    results = accountService.findMulti(p)
                }
            }
        } else { // Account ID or screen name
            results = accountService.find(params.s)
        }

        if(results) {
            responseData.results = results
        } else if(responseData.errorText != null) {
            responseData.errorText = "Player '${params.s}' not found."
        }

        render responseData as JSON
    }

    def details(){

        authService.checkServerAuth(request)

        paramsService.require(params, 'id')

        def account
        def accounts = accountService.find(params.id)
        if(accounts && accounts.size() > 0) {
            account = accounts.first()
        }

        def responseData = [:]
        if(account) {
            def components = accountService.getDetails(params.id)
            def profiles = profileService.getProfilesForAccount(params.id)

            responseData = [
                    success: true,
                    'data': [
                            account   : account,
                            components: components,
                            profiles  : profiles
                    ]
            ]
        } else {
            responseData = [
                    success: false,
                    errorCode: "notFound",
                    errorText: "Player '${params.id}' not found"
            ]
        }

        render responseData as JSON
    }
}
