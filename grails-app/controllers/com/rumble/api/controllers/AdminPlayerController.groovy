package com.rumble.api.controllers

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.MongoCollection
import com.rumble.api.services.ProfileTypes
import grails.converters.JSON
import groovy.json.JsonBuilder
import groovy.json.JsonOutput
import groovy.json.JsonSlurper
import groovyx.net.http.HTTPBuilder

class AdminPlayerController {
    def accessTokenService
    def accountService
    def authService
    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService
    def paramsService
    def profileService
    def itemService
    def dynamicConfigService

    def game = System.getProperty("GAME_GUKEY")

    private def getReports(String aid) { // TODO: These should really be calling chat-service to get them, but groovy didn't play nice with the API earlier.  Investigate more later.
        MongoCollection coll = mongoService.collection("reports")
        BasicDBObject query = new BasicDBObject("rptd.aid", aid)
        def results = coll.find(query).toList()
        return results
    }
    private def getBans(String aid) {
        MongoCollection coll = mongoService.collection("bans")
        BasicDBObject query = new BasicDBObject("aid", aid)
        def results = coll.find(query).toList()
        return results
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
            // 2021.09.30
            // levelRunInfo && autoplay records added a ton of bloat and aren't useful for CS.
            // The sheer amount of bloat means we can't save items from pubapp.
            // TODO: Eventually would be nice to have separate tabs for these two, especially with a .NET player-service rewrite.
            def items = itemService.getItems(params.id, ["hero", "equipment"])

            def chatData
            def chat = [
                bans: [:],
                reports: [:]
            ]
            try {
                chatData = JSON.parse(chatPlayerDetails(params.id));
                chat = [
                    bans: chatData.bans,
                    reports: chatData.reports
                ]
            }
            catch (e){}
//            def chat = [
//                    reports: getReports(params.id),
//                    bans: getBans(params.id)
//            ]

            responseData = [
                success: true,
                'data': [
                    account   : account,
                    components: components,
                    profiles  : profiles,
                    items     : items,
                    chat      : chat
                ]
            ]
        } else {
            responseData = [
                success: false,
                errorCode: "notFound",
                errorText: "Player '${params.id}' not found"
            ]

            logger.info("Player details", params + responseData)
        }

        render responseData as JSON
    }

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

    def generateToken() {
        authService.checkServerAuth(request, request.JSON)

        def days = Integer.parseInt(request.getParameter("days") ?: "5")
        def aid = request.getParameter("aid")
        def claims = [admin: aid == null]
        aid = aid ?: "RumbleAdmin"
        def gameConfig = dynamicConfigService.getGameConfig(game)
        def lifetime = 60 * 60 * 24 * days; // 60s * 60m * 24h * {number of days}
        def token = accessTokenService.generateAccessToken(
                game, aid, claims, gameConfig.long('auth:maxTokenLifeSeconds', lifetime)) // 4d

        def responseData = [
            success: true,
            token: token
        ]
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

    def updateItems() {
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

            System.println("Saving items!")
            data.each { item ->
                itemService.saveItem(clientSession, id, item.id, item)
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
    // The logger.info call hasn't been yielding expected results and is a confusing mess to understand,
    // so we'll use a standard Java post call to Loggly instead, just to get by until we get to a .NET rewrite.
    String chatPlayerDetails(String aid) {

        String token = dynamicConfigService.getGameConfig(game).chatToken;

        def payload = [:];
        payload.aid = aid;

        String json = new JsonBuilder(payload).toString();

        String targetURL = dynamicConfigService.getGameConfig(game).platformUrl + "chat/admin/playerDetails";
        HttpURLConnection connection = null;

        try {
            //Create connection
            URL url = new URL(targetURL);
            connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod("POST");
            connection.setRequestProperty("Content-Type", "application/json");

            connection.setRequestProperty("Content-Length", Integer.toString(json.getBytes().length));
            connection.setRequestProperty("Content-Language", "en-US");
            connection.setRequestProperty("Authorization", "Bearer " + token);

            connection.setUseCaches(false);
            connection.setDoOutput(true);

            //Send request
            DataOutputStream wr = new DataOutputStream (connection.getOutputStream());
            wr.writeBytes(json);
            wr.close();

            //Get Response
            InputStream is = connection.getInputStream();
            BufferedReader rd = new BufferedReader(new InputStreamReader(is));
            StringBuilder response = new StringBuilder(); // or StringBuffer if Java version 5+
            String line;
            while ((line = rd.readLine()) != null) {
                response.append(line);
                response.append('\r');
            }
            rd.close();
            return response.toString();
        } catch (Exception e) {
            e.printStackTrace();
            return null;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }
}