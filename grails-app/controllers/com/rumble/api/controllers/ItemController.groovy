package com.rumble.api.controllers

import grails.converters.JSON

class ItemController {

    def authService
    def itemService

    def list() {

        def accountId = authService.requireClientAuth(request)
        def types = params.types ? params.types.split(',') : null
        def items = itemService.getItems(accountId, types)

        def responseData = [
                success: true,
                items: items
        ]

        render responseData as JSON
    }
}
