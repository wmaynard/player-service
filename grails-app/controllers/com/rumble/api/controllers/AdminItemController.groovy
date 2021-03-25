package com.rumble.api.controllers

import com.mongodb.MongoCommandException
import com.mongodb.MongoException
import com.rumble.platform.exception.BadRequestException
import com.rumble.platform.exception.HttpMethodNotAllowedException
import com.rumble.platform.exception.PlatformException
import grails.converters.JSON

class AdminItemController {

    def itemService
    def accountService
    def authService
    def paramsService
    def mongoService
    def logger = new com.rumble.platform.common.Log(this.class)

    def list() {
        authService.checkServerAuth(request)
        paramsService.require(params, 'account')

        def accountId = accountService.validateAccountId(params.account)
        if(!accountId) {
            throw new BadRequestException("invalid account")
        }

        def types = params.types ? params.types.split(',') : null
        def items = (types) ? itemService.getItems(accountId, types) : []
        def responseData = [
                success: true,
                data   : items
        ]
        render responseData as JSON
    }

    def save(){
        mongoService.runTransactionWithRetry({ saveTransaction() }, 1)
    }

    def saveTransaction() {
        if (request.method != 'POST') {
            throw new HttpMethodNotAllowedException()
        }

        authService.checkServerAuth(request)
        if (!request.getHeader("content-type")?.startsWith("application/json")) {
            throw new BadRequestException("expected content type application/json")
        }

        def requestData = request.JSON
        paramsService.require(requestData, ['account'])
        def accountId = accountService.validateAccountId(requestData.account)
        if(!accountId) {
            throw new BadRequestException("invalid account")
        }

        def clientSession
        try {
            try {
                clientSession = mongoService.client().startSession()
                clientSession.startTransaction()

                requestData.items?.each { item ->
                    itemService.saveItem(clientSession, accountId, item.key, item.value)
                }
            } catch (MongoCommandException e) {
                clientSession.abortTransaction()
                throw e
            } catch (all) {
                clientSession?.abortTransaction()
                throw all
            }

            mongoService.commitWithRetry(clientSession, 1)
        } catch (MongoException err) {
            logger.error(err)
            throw new PlatformException('dbError', err.response?.errmsg)
        } finally {
            clientSession?.close()
        }
        render ([success: true] as JSON)
    }

    def delete(){
        mongoService.runTransactionWithRetry({ deleteTransaction() }, 1)
    }

    def deleteTransaction() {
        if (request.method != 'POST') {
            throw new HttpMethodNotAllowedException()
        }

        authService.checkServerAuth(request)
        paramsService.require(params, 'account')
        paramsService.require(params, 'item')
        def accountId = accountService.validateAccountId(params.account)
        if(!accountId) {
            throw new BadRequestException("invalid account")
        }

        def clientSession
        try {
            try {
                clientSession = mongoService.client().startSession()
                clientSession.startTransaction()

                // TODO: This should support multiple deletion
                itemService.deleteItem(clientSession, accountId, item)
            } catch (MongoCommandException e) {
                clientSession.abortTransaction()
                throw e
            } catch (all) {
                clientSession?.abortTransaction()
                throw all
            }

            mongoService.commitWithRetry(clientSession, 1)
        } catch (MongoException err) {
            logger.error(err)
            throw new PlatformException('dbError', err.response?.errmsg)
        } finally {
            clientSession?.close()
        }

        render ([success: true] as JSON)
    }
}
