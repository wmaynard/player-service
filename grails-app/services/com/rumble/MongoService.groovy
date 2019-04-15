package com.rumble

import com.mongodb.DB
import com.mongodb.DBCollection
import com.mongodb.MongoClient
import com.mongodb.MongoClientURI

class MongoService {
    private static mongoClient
    private static databaseName = System.getProperty("MONGODB_NAME") ?: System.getenv("MONGODB_NAME")

    static MongoClient client() {
        if(mongoClient == null){
            def u = System.getProperty("MONGODB_URI") ?: System.getenv("MONGODB_URI")
            MongoClientURI uri = new MongoClientURI(u)
            mongoClient = new MongoClient(uri)
        }

        return mongoClient
    }

    static boolean hasClient() {
        return (mongoClient == null)
    }

    DBCollection collection(String collectionName) {
        DB db = client().getDB(databaseName)
        return db.getCollection(collectionName)
    }
}
