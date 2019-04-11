package com.rumble

//import grails.gorm.transactions.Transactional
import com.mongodb.BasicDBObject
import com.mongodb.DBObject

//@Transactional
class ProfileService {
    def appleService
    def facebookService
    def googleService
    def mongoService

    static def PROFILE_COLLECTION_NAME = "profiles"

    def validateProfile(identityData) {
        def validProfiles = [:]
        if(identityData.facebook) {
            validProfiles.facebook = facebookService.validateAccount(identityData.facebook)
        }

        if(identityData.gameCenter) {
            validProfiles.gameCenter = appleService.validateAccount(identityData.gameCenter)
        }

        if(identityData.googlePlay) {
            validProfiles.googlePlay = googleService.validateAccount(identityData.googlePlay)
        }

        return validProfiles
    }

    def addProfile(type, accountId, profileId, data){
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        def now = System.currentTimeMillis()
        DBObject doc = new BasicDBObject("type", type)
                .append("accountId",accountId)
                .append("profileId", profileId)
                .append("lu", now)

        if(data) {
            data.each { k, v ->
                doc.append(k, v)
            }
        }
        coll.insert(doc)
        return doc
    }

    def getProfile(type, accountId, profileId) {}

    def getProfiles(accountId) {
        def coll = mongoService.collection(PROFILE_COLLECTION_NAME)
        DBObject query = new BasicDBObject("accountId", accountId)
        def result = coll.find(query)
        return result.toArray()
    }
}
