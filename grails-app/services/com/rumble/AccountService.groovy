package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.DBCursor
import com.mongodb.DBObject
import groovy.json.JsonSlurper

class AccountService {
    def mongoService
    def profileService

    private static def COMPONENT_COLLECTION_NAME_PREFIX = "c_"
    private static def COLLECTION_NAME = "player"

    def exists(String installId, upsertData = null) {
        def coll = mongoService.collection(COLLECTION_NAME)
        DBObject query = new BasicDBObject("lsi", installId)
        if(upsertData) {
            def now = System.currentTimeMillis()
            def setOnInsertObj = new BasicDBObject()
                    .append("cv", upsertData.clientVersion) // client version
                    .append("dv", 0)                        // data version
                    .append("lsi", installId)               // last saved install
                    .append("cd", now)                      // created date

            def player = coll.findAndModify(
                    query, // query
                    new BasicDBObject(), // fields
                    new BasicDBObject(), // sort
                    false, // remove
                    new BasicDBObject('$setOnInsert', setOnInsertObj)
                            .append('$set', new BasicDBObject('lc', now)), // last checked
                    true, // returnNew
                    true // upsert
            )
            return player
        } else {
            DBCursor cursor = coll.find(query)
            if (cursor.size() > 0) {
                // There should only be one result
                //TODO: Log if there are more than one result because something is wrong
                return cursor.first()
            }
        }
        return false
    }

    def create(String installId, data) {
        def coll = mongoService.collection(COLLECTION_NAME)
        def jsonSlurper = new JsonSlurper()
        def now = System.currentTimeMillis()
        def identity = jsonSlurper.parseText(data)
        BasicDBObject doc = new BasicDBObject()
                .append("cv", identity.clientVersion)   // client version
                .append("dv", 0)                        // data version
                .append("lsi", installId)               // install id
                .append("cd", now)                      // created date
        coll.insert(doc)
        return doc
    }

    def getComponentCollectionName(String component) {
        return COMPONENT_COLLECTION_NAME_PREFIX + component
    }

    def getComponentData(accountId, String component) {
        System.out.println("getComponentData")
        def coll = mongoService.collection(getComponentCollectionName(component))
        DBObject query = new BasicDBObject("aid", accountId)
        DBCursor cursor = coll.find(query)
        if (cursor.size() > 0) {
            // There should only be one result
            //TODO: Log if there are more than one result because something is wrong
            return cursor.first()
        }
        return false
    }

    def saveComponentData(accountId, String collection, data) {
        System.out.println("saveComponentData")
        def coll = mongoService.collection(getComponentCollectionName(collection))
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject("aid", accountId)
                .append("data", jsonSlurper.parseText(data))
        System.out.println(doc.toString())
        coll.insert(doc)
    }

    def generateMergeToken(accountId) {
        System.out.println("generateMergeToken")
        def coll = mongoService.collection(COLLECTION_NAME)
        def mergeToken = UUID.randomUUID().toString()
        BasicDBObject doc = new BasicDBObject()
        doc.append("$set", new BasicDBObject().append("mt", mergeToken))
        BasicDBObject query = new BasicDBObject().append("_id", accountId)
        coll.update(query, doc)
        return mergeToken
    }
}
