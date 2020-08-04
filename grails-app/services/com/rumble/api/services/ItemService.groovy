package com.rumble.api.services

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.model.FindOneAndUpdateOptions
import com.mongodb.client.model.ReturnDocument
import com.mongodb.session.ClientSession
import groovy.json.JsonSlurper
import org.bson.types.ObjectId

/**
 * access to individual items is by logical id, assigned by the game, *not* mongodb id
 */
class ItemService {

    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService

    def collectionName = "items"

    def saveItem(ClientSession clientSession, accountId, itemId, data) {
        if (accountId instanceof String) {
            accountId = new ObjectId(accountId)
        }
        if (data instanceof String) {
            data = new JsonSlurper().parseText(data)
        }
        def coll = mongoService.collection(collectionName)
        DBObject query = new BasicDBObject("aid", accountId).append("iid", itemId)
        BasicDBObject doc = new BasicDBObject('$set', new BasicDBObject("data", data))
            .append('$setOnInsert', new BasicDBObject("aid", accountId))
            .append('$setOnInsert', new BasicDBObject("iid",itemId))
        coll.findOneAndUpdate(
                clientSession,
                query,
                doc,
                new FindOneAndUpdateOptions().upsert(true).returnDocument(ReturnDocument.AFTER)
        )
    }

    def deleteItem(ClientSession clientSession, accountId, String itemId) {
        def coll = mongoService.collection(collectionName)
        if (accountId instanceof String) {
            accountId = new ObjectId(accountId)
        }
        DBObject query = new BasicDBObject("aid", accountId).append("iid", itemId)
        coll.findOneAndDelete(clientSession, query)
    }

    def getItems(accountId) {
        if (accountId instanceof String) {
            accountId = new ObjectId(accountId)
        }
        def coll = mongoService.collection(collectionName)
        DBObject query = new BasicDBObject("aid", accountId)
        def items = coll.find(query).collectEntries { item ->
            return [ (item.iid) : item.data]
        }
        return items
    }
}
