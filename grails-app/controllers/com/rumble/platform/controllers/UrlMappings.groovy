package com.rumble.platform.controllers

class UrlMappings {

    static mappings = {
        "/admin/player/$action"(controller: 'adminPlayer')


        "/$controller/$action?/$id?(.$format)?"{
            constraints {
                // apply constraints here
            }
        }

        "/"(view:"/index")
        "404"(controller: 'platformError', action: 'notFound')
        "500"(controller: 'platformError', action: 'uncaughtException')
    }
}
