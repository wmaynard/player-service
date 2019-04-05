package com.rumble

import com.mongodb.BasicDBObject
import com.mongodb.DBCursor
import com.mongodb.DBObject
import org.bson.types.ObjectId
import groovy.json.JsonSlurper

class PlayerService {
    def mongoService

    def exists(String installId) {
        def coll = mongoService.collection("player")
        DBObject query = new BasicDBObject("installId", installId)
        DBCursor cursor = coll.find(query)
        if (cursor.size() > 0) {
            // There should only be one result
            //TODO: Log if there are more than one result because something is wrong
            return cursor.first()
        } else {
            return false
        }
    }

    def create(String installId, data) {
        def coll = mongoService.collection("player")
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject("installId", installId)
                .append("identity", jsonSlurper.parseText(data))
        coll.insert(doc)
        ObjectId id = doc.getObjectId("_id")
        return id
    }

    def findByAccountId(String accountId) {
        def coll = mongoService.collection("player")
        coll.find(eq("_id", accountId)).first()
    }

    def findByInstallId(String installId) {
        def coll = mongoService.collection("player")
        coll.find(eq("installId", installId)).first()
    }

    def getComponentData(accountId, String component) {
        System.out.println("getComponentData")
        def coll = mongoService.collection(component)
        DBObject query = new BasicDBObject("accountId", accountId)
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
        def coll = mongoService.collection(collection)
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject("accountId", accountId)
                .append("data", jsonSlurper.parseText(data))
        System.out.println(doc.toString())
        coll.insert(doc)
    }

    def generateMergeToken(accountId) {
        System.out.println("generateMergeToken")
        def coll = mongoService.collection("player")
        def mergeToken = UUID.randomUUID().toString()
        BasicDBObject doc = new BasicDBObject()
        doc.append("$set", new BasicDBObject().append("mergeToken", mergeToken))
        BasicDBObject query = new BasicDBObject().append("_id", accountId)
        coll.update(query, doc)
        return mergeToken
    }
}
