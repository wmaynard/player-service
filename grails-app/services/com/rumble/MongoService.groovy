package com.rumble

import com.mongodb.*

class MongoService {
    private static mongoClient
    private static databaseName = "test"

    static MongoClient client() {
        if(mongoClient == null){
            MongoClientURI uri = new MongoClientURI(
                    "mongodb://mongoAdmin:vBYpVpdsnNoBmj7S@player-service-nn-shard-00-00-ey7zm.mongodb.net:27017,player-service-nn-shard-00-01-ey7zm.mongodb.net:27017,player-service-nn-shard-00-02-ey7zm.mongodb.net:27017/test?ssl=true&replicaSet=player-service-nn-shard-0&authSource=admin&retryWrites=true")
            return new MongoClient(uri)
        }else {
            return mongoClient
        }
    }

    DBCollection collection(String collectionName) {
        DB db = client().getDB(databaseName)
        return db.getCollection(collectionName)
    }
}
