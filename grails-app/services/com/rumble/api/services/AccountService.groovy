package com.rumble.api.services

import com.mongodb.BasicDBObject
import com.mongodb.DBObject
import com.mongodb.client.MongoCollection
import com.mongodb.client.model.FindOneAndUpdateOptions
import com.mongodb.client.model.ReturnDocument
import com.mongodb.client.result.DeleteResult
import com.mongodb.session.ClientSession
import com.rumble.platform.exception.BadRequestException
import com.rumble.platform.exception.PlatformException
import groovy.json.JsonBuilder
import groovy.json.JsonSlurper
import org.bson.Document
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

//        logger.info("AccountService:updateAccountData")//, [updateDoc: updateDoc])
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
        def output = [:]
        if (data instanceof String) {
            data = new JsonSlurper().parseText(data)
        }

        // Check for the account discriminator.  Add it if it doesn't have one, or attempt to change the screenName with the same discriminator.
        // Assign a new discriminator, if possible, for that screenName.
        if (collection == "account") {
            boolean newAccount = false
            def existingData
            try {
                existingData = getComponentData(accountId, collection)[0].data
                data.discriminator = existingData.discriminator
            }
            catch (Exception) {
                newAccount = true
                logglyPost("Couldn't find existing account data", [
                    aid: accountId,
                    collection: collection,
                    data: data
                ])
            }
            // This created a ton of log-spam, so it doesn't catch what I thought it did.
//            if (existingData?.discriminator && existingData.discriminator != data.discriminator) {
//                logger.warn("The discriminator from the request body and the backend discriminator don't match.  The client may not have updated correctly.", [
//                    aid: accountId,
//                    dataDiscriminator: data.discriminator,
//                    existingDiscriminator: existingData.discriminator
//                ])
//            }

            if (!newAccount &&
                    (!existingData?.discriminator                            // We don't have a discriminator for this aid yet
                    || (data.accountName != null && data.accountName != existingData?.accountName))) {     // The user is changing their screenname
                try {
                    def newDiscriminator = generateDiscriminator(accountId, data.accountName, (int)(existingData?.discriminator ?: -1))

                    logglyPost("possible discriminator generation", [
                        aid: accountId,
                        collection: collection,
                        dataName: data?.accountName,
                        dataDiscriminator: data?.discriminator,
                        existingName: existingData?.accountName,
                        existingDiscriminator: existingData?.discriminator,
                        newDiscriminator: newDiscriminator,
                        existingData: existingData
                    ])

                    // This should only happen if, for example, there are a *ton* of people with the same screenname, and all the retries failed.
                    // We need to throw an exception, though, because we need to guarantee screenName + discriminator combinations are unique.
                    if (newDiscriminator == null)
                        logger.error("Could not create a new discriminator for $accountId.")
                    data.discriminator = newDiscriminator
                    output = [
                        identityChanged: true,
                        screenName: data.accountName,
                        discriminator: newDiscriminator
                    ]
                }
                catch (Exception e) {
                    logger.info("Discriminator generation failed (aid: "
                        + (accountId ?: "null") + " | screenName: "
                        + (data?.accountName ?: "null") + " | existingDiscriminator: "
                        + (existingData?.discriminator ?: "null")
                        + ") ["
                        + e.message
                        + ", "
                        + e.stackTrace
                        + "]"
                    )
                }
            }
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
        return output
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

    private def random(int max) {
        return (int) (Math.random() * max)
    }

    def getDiscriminator(String aid, String screenname) {
        if (aid == null || screenname == null)
            return null
        try {
            MongoCollection coll = mongoService.collection("discriminators")
            BasicDBObject query = new BasicDBObject("members.aid", aid)
            Object result = coll.find(query).first()
            return result?.number ?: generateDiscriminator(aid, screenname)
        }
        catch (Exception) {}
        return null
    }
    private def generateDiscriminator(String aid, String screenName, int desiredNumber = -1) {
        final int RETRY_COUNT = 50
        final int DEDUP_RANGE = 10_000

        try {
            MongoCollection coll = mongoService.collection("discriminators")

            int retries = RETRY_COUNT;
            int rando = desiredNumber >= 0 ? desiredNumber : random(DEDUP_RANGE)
            while (retries-- > 0) {
                String dedup = screenName + "#" + rando
//                System.out.println("Checking '$dedup' ($retries attempts remaining)")

                BasicDBObject numExistsQuery = new BasicDBObject("number", rando)
                BasicDBObject numTakenQuery = new BasicDBObject("\$and", [
                    new BasicDBObject("number", rando),
                    new BasicDBObject("members.sn", screenName)
                ])

                Object result = coll.find(numExistsQuery).first()
                boolean exists = result != null;
                boolean taken = coll.find(numTakenQuery).first() != null

                if (!exists) { // We haven't yet encountered this discriminator
                    Document doc = new Document("_id", new ObjectId())
                    doc.append("number", rando)
                    doc.append("members", [[sn: screenName, aid: aid]])
                    erasePreviousDiscriminator(aid, coll)
                    coll.insertOne(doc)
                    return rando
                } else if (!taken) { // The discriminator exists.  Check to see if the username is taken.
//                    System.out.println("Yay!  $dedup is new!")
                    DBObject item = new BasicDBObject("members", [sn: screenName, aid: aid])
                    DBObject update = new BasicDBObject("\$push", item)
                    erasePreviousDiscriminator(aid, coll)
                    coll.updateOne(numExistsQuery, update)
                    return rando
                }
                else {
//                    System.out.println("$dedup taken.")
                    rando = random(DEDUP_RANGE)
                }
            }
        } catch (Exception e) {
//            System.out.println(e);
        }
        logger.info("Couldn't assign a new discriminator for $aid#$desiredNumber")
        return null
    }
    private def erasePreviousDiscriminator(String aid, MongoCollection coll) {
        def query = new BasicDBObject("members.aid", aid)
        DeleteResult result = coll.deleteMany(query)
        System.out.println("Deleted $result.deletedCount records.")
    }
    // The logger.info call hasn't been yielding expected results and is a confusing mess to understand,
    // so we'll use a standard Java post call to Loggly instead, just to get by until we get to a .NET rewrite.
    String logglyPost(String message, def map) {

        def payload = [:];
        payload.env = System.getProperty("RUMBLE_DEPLOYMENT");
        payload.message = message;
        payload.severity = "INFO";
        payload.component = "player-service";
        payload.data = map;

        String json = new JsonBuilder(payload).toString();

        String targetURL = System.getProperty("LOGGLY_URL");
        HttpURLConnection connection = null;

        try {
            //Create connection
            URL url = new URL(targetURL);
            connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod("POST");
            connection.setRequestProperty("Content-Type", "application/json");

            connection.setRequestProperty("Content-Length", Integer.toString(json.getBytes().length));
            connection.setRequestProperty("Content-Language", "en-US");

            connection.setUseCaches(false);
            connection.setDoOutput(true);

            //Send request
            DataOutputStream wr = new DataOutputStream (connection.getOutputStream());
            wr.writeBytes(json);
            wr.close();

            //Get Response
            InputStream is = connection.getInputStream();
            BufferedReader rd = new BufferedReader(new InputStreamReader(is));
            StringBuilder response = new StringBuilder(); // or StringBuffer if Java version 5+
            String line;
            while ((line = rd.readLine()) != null) {
                response.append(line);
                response.append('\r');
            }
            rd.close();
            return response.toString();
        } catch (Exception e) {
            e.printStackTrace();
            return null;
        } finally {
            if (connection != null) {
                connection.disconnect();
            }
        }
    }
}
