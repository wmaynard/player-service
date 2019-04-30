package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.DBObject

class ProfileService {
    def appleService
    def facebookService
    def googleService
    def mongoService

    static def PROFILE_COLLECTION_NAME = "profiles"

    def validateProfile(identityData) {
        def valid
        def validProfiles = [:]
        if(identityData.facebook) {
            valid = facebookService.validateAccount(identityData.facebook)
            if(valid) {
                validProfiles.facebook = valid
            }
        }

        if(identityData.gameCenter) {
            valid = appleService.validateAccount(identityData.gameCenter)
            if(valid) {
                validProfiles.gameCenter = valid
            }
        }

        if(identityData.googlePlay) {
            valid = googleService.validateAccount(identityData.googlePlay)
            if(valid) {
                validProfiles.googlePlay = valid
            }
        }

        return validProfiles
    }

    def saveProfile(type, accountId, profileId, updateData = null){
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        def now = System.currentTimeMillis()
        def query = new BasicDBObject("type", type)
                .append("aid",accountId)
                .append("pid", profileId)

        DBObject upsertDoc = new BasicDBObject("type", type)
                .append("aid",accountId)
                .append("pid", profileId)
                .append("cd", now)

        DBObject updateDoc = new BasicDBObject("lu", now)

        if(updateData) {
            updateData.each { k, v ->
                updateDoc.append(k, v)
            }
        }

        def profile = coll.findAndModify(
                query, // query
                new BasicDBObject(), // fields
                new BasicDBObject(), // sort
                false, // remove
                new BasicDBObject('$setOnInsert', upsertDoc)
                        .append('$set', updateDoc), // update
                true, // returnNew
                true // upsert
        )

        return profile
    }

    def saveInstallIdProfile(accountId, installId, identityData) {
        // Extract data to save with the Install ID
        def data = AccountService.extractInstallData(identityData)

        return saveProfile("installId", accountId, installId, data)
    }

    def getProfile(type, accountId, profileId) {}

    def getProfiles(accountId) {
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("aid", accountId)
        def result = coll.find(query)
        return result.toArray()
    }
}
