package com.rumble.platform.controllers

class UrlMappings {

    static mappings = {
        "/admin/player/$action"(controller: 'adminPlayer')

        "/$controller/$action?/$id?(.$format)?"{
            constraints {
                // apply constraints here
            }
        }

        "/"(controller: 'error', action: 'notFound')
        "404"(controller: 'error', action: 'notFound')
        "500"(controller: 'error', action: 'uncaughtException')
    }
}
