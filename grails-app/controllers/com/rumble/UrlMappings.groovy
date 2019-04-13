package com.rumble

class UrlMappings {

    static mappings = {
        "/health"(controller:'health')


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
