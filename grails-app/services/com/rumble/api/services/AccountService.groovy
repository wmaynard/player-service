package com.rumble.api.services

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.model.FindOneAndUpdateOptions
import com.mongodb.client.model.ReturnDocument
import com.mongodb.session.ClientSession
import groovy.json.JsonSlurper
import org.bson.types.ObjectId

class AccountService {
    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService
    def profileService

    private static def COMPONENT_COLLECTION_NAME_PREFIX = "c_"
    private static def COLLECTION_NAME = "player"
    private static def FORCE_LOGOUT_FLAG = "forceLogout"

    def componentNamesCache = null
    def componentNamesCacheExpire = 0L

    // TODO: this would benefit from some caching
    def getComponentNames() {
        if (componentNamesCacheExpire < System.currentTimeMillis()) {
            synchronized(this) {
                if (componentNamesCacheExpire < System.currentTimeMillis()) {
                    componentNamesCache =
                            mongoService.collectionNames().findResults
                                    { it.startsWith(COMPONENT_COLLECTION_NAME_PREFIX) ?
                                            it.substring(COMPONENT_COLLECTION_NAME_PREFIX.length()) : null }
                    componentNamesCacheExpire = System.currentTimeMillis() + 60000L
                }
            }
        }
        return componentNamesCache
    }

    def validateAccountId(accountId){
        if(accountId instanceof String) {
            try {
                def test = new ObjectId(accountId)
            } catch(all) {
                logger.warn("Invalid Account ID", all, [accountId:accountId])
                return null
            }
        } else if(accountId instanceof Collection || accountId instanceof List) {
            def aIds = accountId.findAll{
                try {
                    if (it instanceof String) {
                        def test = new ObjectId(it)
                    } else if(it instanceof ObjectId){
                        return true
                    }

                    return true
                } catch(all) {
                    logger.warn("Invalid Account ID", all, [ accountId: it])
                    return false
                }
            }.collect{
                return (it instanceof String) ? new ObjectId(it) : it
            }

            if(aIds.size()) {
                return aIds
            }
        }

        return null
    }

    def find(searchStr) {
        DBObject query
        def coll = mongoService.collection(COLLECTION_NAME)
        def baseQuery = []

        if(searchStr instanceof ObjectId) {
            baseQuery << new BasicDBObject('_id', searchStr)
        } else {
            baseQuery = [
                    new BasicDBObject("lsi", searchStr),
                    new BasicDBObject('sn', new BasicDBObject('$regex', searchStr).append('$options', 'i'))
            ]

            if (ObjectId.isValid(searchStr)) {
                baseQuery << new BasicDBObject('_id', new ObjectId(searchStr))
            }
        }

        query = new BasicDBObject('$or', baseQuery)
        logger.debug("AccountService:find", [query: query])
        def cursor = coll.find(query)
        return cursor.toList()
    }

    def findMulti(searchStr) {
        DBObject query
        def coll = mongoService.collection(COLLECTION_NAME)
        def baseQuery = [
                new BasicDBObject("lsi", new BasicDBObject('$in', searchStr))
        ]

        def ids = []
        searchStr.each {
            if (ObjectId.isValid(it)) {
                ids << new ObjectId(it)
            }
        }

        if(ids) {
            baseQuery << new BasicDBObject('_id', new BasicDBObject('$in', ids))
        }

        query = new BasicDBObject('$or', baseQuery)
        logger.debug("AccountService:findMulti", [query: query])
        def cursor = coll.find(query)
        return cursor.toList()
    }

    def getDetails(String accountId, names) {
        def details = [:]
        (names ?: componentNames).each{ c ->
            def d = getComponentData(accountId, c)
            if (names || d) {
                details[c] = d?d.first():[:]
            }
        }

        return details
    }

    def exists(ClientSession clientSession, String installId, upsertData = null) {
        def coll = mongoService.collection(COLLECTION_NAME)
        DBObject query = new BasicDBObject("lsi", installId)
        if(upsertData) {
            def now = System.currentTimeMillis()
            def setOnInsertObj = new BasicDBObject()
                    .append("cv", upsertData.clientVersion) // client version
                    .append("dv", upsertData.dataVersion ?: 0)  // data version
                    .append("ldv", upsertData.localDataVersion ?: 0)  // local data version
                    .append("mv", upsertData.manifestVersion ?: 0)  // manifest version
                    .append("lsi", installId)                   // last saved install ID
                    .append("dt", upsertData.deviceType ?: "n/a") // last device type
                    .append("cd", now)                      // created date

            if(upsertData.screenName){
                setOnInsertObj.append('sn', upsertData.screenName)
            }

            def player = coll.findOneAndUpdate(
                    clientSession,
                    query, // query
                    new BasicDBObject('$set', new BasicDBObject("lc", now))
                            .append('$setOnInsert', setOnInsertObj),
                    new FindOneAndUpdateOptions().upsert(true).sort(new BasicDBObject("lu", -1)).returnDocument(ReturnDocument.AFTER)
            )

            return player
        } else {
            def cursor = coll.find(query).sort(new BasicDBObject("lu", -1))
            if (cursor.size() > 0) {
                // There should only be one result
                if(cursor.size() > 1) {
                    logger.warn("There are more than one account with installId", [installId: installId])
                }
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
                .append("ldv", 0)                        // data version
                .append("lsi", installId)               // install id
                .append("cd", now)                      // created date
        coll.insert(doc)
        return doc
    }

    def updateAccountData(ClientSession clientSession, accountId, identityData, manifestVersion = null, isMerge = false) {
        def now = System.currentTimeMillis()
        def coll = mongoService.collection(COLLECTION_NAME)
        BasicDBObject updateDoc = new BasicDBObject("lu", now)

        if(identityData.clientVersion) {
            updateDoc.append("cv", identityData.clientVersion)
        }

        if(identityData.dataVersion) {
            updateDoc.append("dv", identityData.dataVersion)
        }

        if(identityData.localDataVersion) {
            updateDoc.append("ldv", identityData.localDataVersion)
        }

        if(identityData.deviceType) {
            updateDoc.append("dt", identityData.deviceType)
        }

        if(identityData.installId) {
            updateDoc.append("lsi", identityData.installId)
        }

        if(identityData.screenName) {
            updateDoc.append("sn", identityData.screenName)
        }

        if(manifestVersion) {
            updateDoc.append("mv", manifestVersion)
        }

        if(isMerge) {
            // Remove merge token
            updateDoc = new BasicDBObject('$set', updateDoc).append('$unset', new BasicDBObject("mt", ""))
        } else {
            updateDoc = new BasicDBObject('$set', updateDoc)
        }

        logger.info("AccountService:updateAccountData")//, [updateDoc: updateDoc])
        def account = coll.findOneAndUpdate(
                clientSession,
                new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId),    // query
                updateDoc,                              // update
                new FindOneAndUpdateOptions().upsert(true).returnDocument(ReturnDocument.AFTER)
        )

        return account
    }

    def getComponentCollectionName(String component) {
        return COMPONENT_COLLECTION_NAME_PREFIX + component
    }

    def getComponentData(accountId, String component) {
        def coll = mongoService.collection(getComponentCollectionName(component))
        DBObject query = new BasicDBObject()

        if(accountId instanceof ObjectId) {
            query.append('aid', accountId)
        } else if(accountId instanceof String) {
            query.append('aid', new ObjectId(accountId))
        } else if(accountId instanceof Collection || accountId instanceof List) {
            if(accountId.size()) {
                query.append('aid', new BasicDBObject('$in', accountId))
            }
        }

        if(query.size()) {
            def cursor = coll.find(query)
            if (cursor.size() > 0) {
                return cursor.toList()
            }
        }
        
        return []
    }

    def saveComponentData(ClientSession clientSession, accountId, String collection, data) {
        if (data instanceof String) {
            data = new JsonSlurper().parseText(data)
        }
        def coll = mongoService.collection(getComponentCollectionName(collection))
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        BasicDBObject doc = new BasicDBObject('$set', new BasicDBObject("data", data))
                .append('$setOnInsert', new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId))
        //System.out.println("saveComponentData" + doc.toString())
        coll.findOneAndUpdate(
                clientSession,
                query,            // query
                doc,                            // update
                new FindOneAndUpdateOptions().upsert(true).returnDocument(ReturnDocument.AFTER)
        )
    }

    def deleteComponentData(ClientSession clientSession, accountId, String collection) {
        def coll = mongoService.collection(getComponentCollectionName(collection))
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        coll.findOneAndDelete(clientSession, query)
    }

    def validateMergeToken(String accountId, String mergeToken, String mergeAccountId) {
        def coll = mongoService.collection(COLLECTION_NAME)
        def query = new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
                .append("mt", mergeToken)
        if (mergeAccountId && (mergeAccountId != accountId)) {
            query = query.append("ma", mergeAccountId)
        }
        def cursor = coll.find(query)

        def cursorSize = cursor.size()
        cursor.iterator().close()
        if(cursorSize > 1) {
            // Log if there is more than one because this shouldn't happen
            logger.warn("Found more than one merge token", [ accountId: accountId, mergeToken: mergeToken ])
        }

        return (cursorSize > 0)
    }

    def generateMergeToken(ClientSession clientSession, accountId, mergeAccountId) {
        logger.trace("AccountService:generateMergeToken()")
        def coll = mongoService.collection(COLLECTION_NAME)
        def mergeToken = UUID.randomUUID().toString()
        BasicDBObject updateDoc = new BasicDBObject('$set', new BasicDBObject("mt", mergeToken).append("ma", mergeAccountId))
        BasicDBObject query = new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        coll.findOneAndUpdate(clientSession, query, updateDoc)
        return mergeToken
    }

    // Fix for PLATF-5247/5248 | Publishing App data is overwritten by active clients
    // Sets a flag under Player > Components > Data.  If the flag is set, forces /player/update to fail and force the
    // client to relaunch.  Flag is cleared in /player/launch.
    private def getForcedLogout(session, accountId) {
        def output = false
        try {
            def collection = mongoService.collection(getComponentCollectionName("account"))
            DBObject query = new BasicDBObject("aid", toObjectId(accountId))
            def result = collection.find(query)
            output = result[0].get("data").get(FORCE_LOGOUT_FLAG)
        }
        catch (e) {
//            System.println("Couldn't retrieve forced logout flag")
        }
        return output
    }
    private def setForcedLogout(session, accountId, value) {
        def collection = mongoService.collection(getComponentCollectionName("account"))
        DBObject query = new BasicDBObject("aid", toObjectId(accountId))
        BasicDBObject doc = new BasicDBObject('$set', new BasicDBObject("data.forceLogout", value))
        collection.findOneAndUpdate(session, query, doc)
    }

    def hasInstallConflict(player, identity){
        return (player.lsi != identity.installId)
    }

    def hasVersionConflict(player, identity){
        return (player.dv > identity.dataVersion)
    }

    // TODO: change this to an allow list
    def extractInstallData(identityData) {
        def data = [:]

        // Subtracting data for convenience
        identityData.each { k, v ->
            if(k!= "installId" && k != "facebook" && k != "gameCenter" && k != "googlePlay") {
                data[k] = v
            }
        }

        return data
    }
    private def toObjectId(value) {
        if (value instanceof String)
            return new ObjectId(value)
        return value
    }
}
