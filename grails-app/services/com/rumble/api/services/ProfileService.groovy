package com.rumble.api.services

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.model.FindOneAndUpdateOptions
import com.mongodb.client.model.ReturnDocument
import com.mongodb.session.ClientSession
import org.bson.types.ObjectId

class ProfileService {
    def accountService
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

    def saveProfile(ClientSession clientSession, type, accountId, profileId, updateData = null){
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

        def profile = coll.findOneAndUpdate(
                clientSession,
                query, // filter
                new BasicDBObject('$set', updateDoc)
                        .append('$setOnInsert', upsertDoc), // update
                new FindOneAndUpdateOptions().upsert(true).returnDocument(ReturnDocument.AFTER)
        )

        return profile
    }

    def mergeProfile(ClientSession clientSession, type, accountId, profileId, updateData = null) {
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

        def profile = coll.findOneAndUpdate(
                clientSession,
                query, // query
                new BasicDBObject('$set', updateDoc)
                        .append('$setOnInsert', upsertDoc), // update
                new FindOneAndUpdateOptions().upsert(true).returnDocument(ReturnDocument.AFTER)
        )

        return profile
    }

    def saveInstallIdProfile(ClientSession clientSession, accountId, installId, identityData) {
        // Extract data to save with the Install ID
        def data = accountService.extractInstallData(identityData)

        return saveProfile(clientSession, ProfileTypes.INSTALL_ID, accountId, installId, data)
    }

    def getProfilesForAccount(accountId) {
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId): accountId)
        def results = coll.find(query)

        return results.toList()
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
            results += result.toList()
        }

        return results
    }

    def getProfilesFromList(String type, profileIds){
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("type", type)
                .append("pid", new BasicDBObject('$in', profileIds))

        def results = coll.find(query)
        return results.toList()
    }
}

final class ProfileTypes{
    static final String FACEBOOK = "facebook"
    static final String INSTALL_ID = "installId"
}
