package com.rumble.platform.services

import com.mongodb.client.MongoClient
import com.mongodb.client.MongoCollection
import com.mongodb.client.MongoDatabase

class MongoService {
    private static mongoClient
    private static databaseName = System.getProperty("MONGODB_NAME") ?: System.getenv("MONGODB_NAME")
    private static logger = new com.rumble.platform.common.Log(this.class)

    static def init(){
        if(mongoClient == null){
            mongoClient = com.mongodb.client.MongoClients.create(System.getProperty("MONGODB_URI"))
        } else {
            logger.warn("MongoClient already initialized")
        }
    }

    static MongoClient client() {
        if(mongoClient == null){
            throw new Exception("Mongo Service not initialized")
        }
        return mongoClient
    }

    static close() {
        mongoClient?.close()
    }

    MongoCollection collection(String collectionName) {
        MongoDatabase db = client().getDatabase(databaseName)
        return db.getCollection(collectionName)
    }
}
