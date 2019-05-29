package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import org.bson.types.ObjectId

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
                .append("aid",(accountId instanceof String) ? new ObjectId(accountId) : accountId)
                .append("pid", profileId)

        DBObject upsertDoc = new BasicDBObject("type", type)
                .append("aid",(accountId instanceof String) ? new ObjectId(accountId) : accountId)
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

    def mergeProfile(type, accountId, profileId, updateData = null) {
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        def now = System.currentTimeMillis()
        def query = new BasicDBObject("type", type)
                .append("pid", profileId)

        DBObject upsertDoc = new BasicDBObject("type", type)
                .append("pid", profileId)
                .append("cd", now)

        DBObject updateDoc = new BasicDBObject("lu", now)
                .append("aid",(accountId instanceof String) ? new ObjectId(accountId) : accountId)

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

        return saveProfile(ProfileTypes.INSTALL_ID, accountId, installId, data)
    }

    def getProfilesForAccount(accountId) {
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId): accountId)
        def results = coll.find(query)

        return results.toArray()
    }

    /* profiles = {
    *   "facebook" : "1234567890"
    *  } */
    def getAccountsFromProfiles(profiles) {
        def results = []
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        profiles.each { type, profile ->
            DBObject query = new BasicDBObject("type", type).append("pid", profile)
            def result = coll.find(query)
            results += result.toArray()
        }

        return results
    }

    def getProfilesFromList(String type, profileIds){
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("type", type)
                .append("pid", new BasicDBObject('$in', profileIds))

        def results = coll.find(query)
        return results.toArray()
    }
}

final class ProfileTypes{
    static final String FACEBOOK = "facebook"
    static final String INSTALL_ID = "installId"
}