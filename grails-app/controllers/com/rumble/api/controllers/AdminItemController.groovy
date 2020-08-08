package com.rumble.api.controllers

import com.rumble.platform.exception.BadRequestException
import grails.converters.JSON

class AdminItemController {

    def itemService
    def authService
    def logger = new com.rumble.platform.common.Log(this.class)

    def list() {
        authService.checkServerAuth(request)
        paramsService.require(params, 'account')
        def accountId = params.account
        def types = params.types ? params.types.split(',') : null
        def items = itemService.getItems(accountId, types)
        def responseData = [
                success: true,
                data: items
        ]
        render responseData as JSON
    }

    def save() {
        authService.checkServerAuth(request)
        if (!request.getHeader("content-type")?.startsWith("application/json")) {
            throw new BadRequestException("expected content type application/json")
        }
        def requestData = request.JSON
        paramsService.require(requestData, ['account'])
        def accountId = requestData.account
        requestData.items?.each { item ->
            itemService.saveItem(clientSession, accountId, item.key, item.value)
        }
        render ([success: true] as JSON)
    }

    def delete() {
        authService.checkServerAuth(request)
        paramsService.require(params, 'account')
        paramsService.require(params, 'item')
        itemService.deleteItem(clientSession, accountId, item)
        render ([success: true] as JSON)
    }
}
