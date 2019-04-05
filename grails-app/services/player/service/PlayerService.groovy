package player.service

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
}
