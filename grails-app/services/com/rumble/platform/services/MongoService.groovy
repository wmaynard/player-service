package com.rumble.platform.services

import com.mongodb.MongoException
import com.mongodb.client.ClientSession
import com.mongodb.client.MongoClient
import com.mongodb.client.MongoCollection
import com.mongodb.client.MongoDatabase

class MongoService {
    private static mongoClient
    private static databaseName = System.getProperty("MONGODB_NAME") ?: System.getenv("MONGODB_NAME")
    def logger = new com.rumble.platform.common.Log(this.class)

    def init(){
        if(mongoClient == null){
            mongoClient = com.mongodb.client.MongoClients.create(System.getProperty("MONGODB_URI"))
        } else {
            logger.warn("MongoClient already initialized")
        }
    }

    MongoClient client() {
        if(mongoClient == null){
            throw new Exception("Mongo Service not initialized")
        }
        return mongoClient
    }

    def close() {
        mongoClient?.close()
    }

    MongoCollection collection(String collectionName) {
        MongoDatabase db = client().getDatabase(databaseName)
        return db.getCollection(collectionName)
    }

    def commitWithRetry(ClientSession clientSession, maxNumberOfRetries = 3) {
        def count = 0
        while (count <= maxNumberOfRetries) {
            try {
                clientSession.commitTransaction()
                logger.info("Transaction committed");
                break
            } catch (MongoException e) {
                // can retry commit
                if (e.hasErrorLabel(MongoException.UNKNOWN_TRANSACTION_COMMIT_RESULT_LABEL)) {
                    logger.info("UnknownTransactionCommitResult, retrying commit operation ...");
                    count++
                    continue
                } else {
                    logger.warn("Exception during commit ...");
                    clientSession.abortTransaction()
                    throw e
                }
            }
        }
    }

    def runTransactionWithRetry(Runnable transactional, maxNumberOfRetries = 3) {
        def count = 0
        while (count <= maxNumberOfRetries) {
            try {
                transactional.run()
                break;
            } catch (MongoException e) {
                log.warn("Transaction aborted. Caught exception during transaction.")

                if (e.hasErrorLabel(MongoException.TRANSIENT_TRANSACTION_ERROR_LABEL)) {
                    log.warn("TransientTransactionError, aborting transaction and retrying ...")
                    count++
                    continue
                } else {
                    throw e
                }
            }
        }
    }
}
