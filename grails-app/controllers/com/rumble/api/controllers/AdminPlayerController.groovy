package com.rumble.api.controllers

import com.rumble.api.services.ProfileTypes
import grails.converters.JSON
import groovy.json.JsonOutput
import groovy.json.JsonSlurper

class AdminPlayerController {
    def accountService
    def authService
    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService
    def paramsService
    def profileService
    def itemService

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

        logger.info("Player search", params + responseData)
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
            def components = accountService.getDetails(params.id, null)
            def profiles = profileService.getProfilesForAccount(params.id)
            def items = itemService.getItems(params.id, null)

            responseData = [
                    success: true,
                    'data': [
                            account   : account,
                            components: components,
                            profiles  : profiles,
                            items     : items
                    ]
            ]
        } else {
            responseData = [
                    success: false,
                    errorCode: "notFound",
                    errorText: "Player '${params.id}' not found"
            ]
        }

        logger.info("Player details", params + responseData)
        render responseData as JSON
    }

    def updateAccount() {
        authService.checkServerAuth(request)

        paramsService.require(params, 'aid', 'data')

        def id = params.aid.trim()
        def data = params.data.trim()
        def forceConflict = params.forceConfict?.trim() ?: true
        def responseData = [:]
        def clientSession
        try {
            clientSession = mongoService.client().startSession()
            clientSession.startTransaction()

            def jsonSlurper = new JsonSlurper()
            data = jsonSlurper.parseText(data)

            // Since this is raw data from publishing-app, we have to map it
            def identityData = [:]
            data.each{ k, v ->
                switch(k){
                    case "cv":
                        identityData.clientVersion = v
                        break
                    case "dv":
                        identityData.dataVersion = v
                        break
                    case "dt":
                        identityData.deviceType = v
                        break
                    case "ldv":
                        identityData.localDataVersion = v
                        break
                    case "lsi":
                        identityData.installId = v
                        break
                    case "sn":
                        identityData.screenName = v
                        break
                    case "mv":
                        identityData.manifestVersion = v
                        break
                    default:
                        identityData[k] = v
                        break
                }
            }

            // We're going to force a conflict by changing dataVersion
            if(forceConflict.toBoolean()) {
                identityData += ["dataVersion": "PUBLISHING_APP"]
            }

            // Require active users to re-login to accept server-authoritative changes
            accountService.setForcedLogout(clientSession, id, true)
            def account = accountService.updateAccountData(clientSession, id, identityData)
            clientSession.commitTransaction()
            responseData.success = true
        } catch(all) {
            logger.error(all.getMessage(), all)
            clientSession.abortTransaction()
            responseData.success = false
            responseData.errorText = all.getMessage()
        } finally {
            clientSession?.close()
        }

        render responseData as JSON
    }


    // Update player component data
    def updateComponent(){
        authService.checkServerAuth(request)

        paramsService.require(params, 'aid', 'data')

        def id = params.aid.trim()
        def data = params.data.trim()
        def forceConflict = params.forceConfict?.trim() ?: true
        def responseData = [:]
        def clientSession
        try {
            clientSession = mongoService.client().startSession()
            clientSession.startTransaction()

            def jsonSlurper = new JsonSlurper()
            data = jsonSlurper.parseText(data)
            data.each { key, component ->
                def d = jsonSlurper.parseText(component)
                def componentData = d.data
                accountService.saveComponentData(clientSession, id, key, JsonOutput.toJson(componentData))
            }

            // We're going to force a conflict by changing dataVersion
            if(forceConflict.toBoolean()) {
                def account = accountService.updateAccountData(clientSession, id, ["dataVersion": "PUBLISHING_APP"])
            }

            // Require active users to re-login to accept server-authoritative changes
            accountService.setForcedLogout(clientSession, id, true)
            clientSession.commitTransaction()
            responseData.success = true
        } catch(all) {
            logger.error(all.getMessage(), all)
            clientSession.abortTransaction()
            responseData.success = false
            responseData.errorText = all.getMessage()
        } finally {
            clientSession?.close()
        }

        render responseData as JSON
    }

}