package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.DBCursor
import com.mongodb.DBObject
import groovy.json.JsonSlurper
import org.bson.types.ObjectId

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
                    .append("dv", upsertData.dataVersion ?: 0)  // data version
                    .append("lsi", installId)                   // last saved install ID
                    .append("dt", upsertData.deviceType ?: "n/a") // last device type
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
                def result = cursor.first()
                cursor.close()
                return result
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

    def updateAccountData(accountId, identityData) {
        def now = System.currentTimeMillis()
        def coll = mongoService.collection(COLLECTION_NAME)
        BasicDBObject updateDoc = new BasicDBObject("lu", now)

        if(identityData.clientVersion) {
            updateDoc.append("cv", identityData.clientVersion)
        }

        if(identityData.dataVersion) {
            updateDoc.append("dv", identityData.dataVersion)
        }

        if(identityData.dataVersion) {
            updateDoc.append("dt", identityData.deviceType)
        }

        if(identityData.installId) {
            updateDoc.append("lsi", identityData.installId)
        }

        System.out.println(updateDoc.toString())
        def account = coll.findAndModify(
                new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId),    // query
                new BasicDBObject(),                    // fields
                new BasicDBObject(),                    // sort
                false,                          // remove
                updateDoc,                              // update
                true,                         // returnNew
                false                            // upsert
        )

        return account
    }

    def getComponentCollectionName(String component) {
        return COMPONENT_COLLECTION_NAME_PREFIX + component
    }

    def getComponentData(accountId, String component) {
        System.out.println("getComponentData")
        def coll = mongoService.collection(getComponentCollectionName(component))
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        DBCursor cursor = coll.find(query)
        if (cursor.size() > 0) {
            // There should only be one result
            //TODO: Log if there are more than one result because something is wrong
            def result = cursor.first()
            cursor.close()
             return result
        }
        return false
    }

    def saveComponentData(accountId, String collection, data) {
        def coll = mongoService.collection(getComponentCollectionName(collection))
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
                .append("data", jsonSlurper.parseText(data))
        System.out.println("saveComponentData" + doc.toString())
        coll.findAndModify(
                query,            // query
                new BasicDBObject(),            // fields
                new BasicDBObject(),            // sort
                false,                          // remove
                doc,                            // update
                true,                           // returnNew
                true                            // upsert
        )
    }

    def validateMergeToken(accountId, mergeToken) {
        def coll = mongoService.collection(COLLECTION_NAME)
        DBCursor cursor = coll.find(new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
                .append("mt", mergeToken)
        )

        def cursorSize = cursor.size()
        cursor.close()
        if(cursorSize > 1) {
            // TODO: Log if there is more than one because this shouldn't happen
        }

        return (cursorSize > 0)
    }

    def generateMergeToken(accountId) {
        System.out.println("generateMergeToken")
        def coll = mongoService.collection(COLLECTION_NAME)
        def mergeToken = UUID.randomUUID().toString()
        BasicDBObject doc = new BasicDBObject()
        doc.append('$set', new BasicDBObject().append("mt", mergeToken))
        BasicDBObject query = new BasicDBObject().append("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        coll.update(query, doc)
        return mergeToken
    }

    static def extractInstallData(identityData) {
        def data = [:]

        // Subtracting data for convenience
        identityData.each { k, v ->
            if(k!= "installId" && k != "facebook" && k != "gameCenter" && k != "googlePlay") {
                data[k] = v
            }
        }

        return data
    }
}
