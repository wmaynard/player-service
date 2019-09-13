package com.rumble.api.services

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.model.FindOneAndUpdateOptions
import com.mongodb.session.ClientSession
import groovy.json.JsonSlurper
import org.bson.types.ObjectId

class AccountService {
    def logger = new com.rumble.platform.common.Log(this.class)
    def mongoService
    def profileService

    private static def COMPONENT_COLLECTION_NAME_PREFIX = "c_"
    private static def COLLECTION_NAME = "player"

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

    def find(String searchStr) {
        DBObject query
        def coll = mongoService.collection(COLLECTION_NAME)
        def baseQuery = [
                new BasicDBObject("lsi", searchStr),
                new BasicDBObject('sn', new BasicDBObject('$regex', searchStr).append('$options', 'i'))
        ]

        try {
            def id = new ObjectId(searchStr)
            baseQuery << new BasicDBObject('_id', id)
        } catch(all) {
            logger.warn("Not an Object ID", all, [searchStr:searchStr])
        }

        query = new BasicDBObject('$or', baseQuery)
        logger.debug("AccountService:find", [query: query])
        def cursor = coll.find(query)
        return cursor.toList()
    }

    def getDetails(String accountId) {
        def details = [:]

        // TODO: Do not hardcode components
        def components = [
                "account",
                "chests",
                "challenges",
                "heroes",
                "inbox",
                "store",
                "summary",
                "tracking",
                "tutorials",
                "wallet"
        ]

        components.each{ c ->
            def d = getComponentData(accountId, c)
            details[c] = (d.size() == 1) ? d.first() : d
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
                    new FindOneAndUpdateOptions().upsert(true) //.returnDocument()
            )
            return player
        } else {
            def cursor = coll.find(query)
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
                new FindOneAndUpdateOptions().upsert(true) //.returnDocument()
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
                def result = cursor.toList()
                logger.info("AccountService:getComponentData",[accountId: accountId, component: component])//, docs: docs.collect { it.toString() }])
                return result
            }

            return []
        }

        return false
    }

    def saveComponentData(ClientSession clientSession, accountId, String collection, data) {
        def coll = mongoService.collection(getComponentCollectionName(collection))
        DBObject query = new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        def jsonSlurper = new JsonSlurper()
        BasicDBObject doc = new BasicDBObject('$set', new BasicDBObject("data", jsonSlurper.parseText(data)))
                .append('$setOnInsert', new BasicDBObject("aid", (accountId instanceof String) ? new ObjectId(accountId) : accountId))
        logger.info("AccountService:saveComponentData")//, [doc: doc])
        //System.out.println("saveComponentData" + doc.toString())
        coll.findOneAndUpdate(
                clientSession,
                query,            // query
                doc,                            // update
                new FindOneAndUpdateOptions().upsert(true) //.returnDocument()
        )
    }

    def validateMergeToken(accountId, mergeToken) {
        def coll = mongoService.collection(COLLECTION_NAME)
        def cursor = coll.find(new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
                .append("mt", mergeToken)
        )

        def cursorSize = cursor.size()
        cursor.close()
        if(cursorSize > 1) {
            // Log if there is more than one because this shouldn't happen
            logger.warn("Found more than one merge token", [ accountId: accountId, mergeToken: mergeToken ])
        }

        return (cursorSize > 0)
    }

    def generateMergeToken(ClientSession clientSession, accountId) {
        logger.trace("AccountService:generateMergeToken()")
        def coll = mongoService.collection(COLLECTION_NAME)
        def mergeToken = UUID.randomUUID().toString()
        BasicDBObject updateDoc = new BasicDBObject('$set', new BasicDBObject("mt", mergeToken))
        BasicDBObject query = new BasicDBObject("_id", (accountId instanceof String) ? new ObjectId(accountId) : accountId)
        coll.findOneAndUpdate(clientSession, query, updateDoc)
        return mergeToken
    }

    def hasInstallConflict(player, manifest){
        return (player.lsi != manifest.identity.installId)
    }

    def hasVersionConflict(player, manifest){
        return (player.dv > manifest.identity.dataVersion)
    }

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
}
