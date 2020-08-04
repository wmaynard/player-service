package com.rumble.api.controllers

import grails.converters.JSON

class ItemController {

    def authService
    def itemService

    def list() {

        def accountId = authService.requireClientAuth(request)

        def components = itemService.getItems(accountId)

        def responseData = [
                success: true,
                data: components
        ]

        render responseData as JSON
    }
}
