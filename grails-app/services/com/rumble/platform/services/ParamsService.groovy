package com.rumble.platform.services

import com.rumble.platform.exception.BadRequestException

class ParamsService {

    def require(params, String[] names) {
        names?.each {
            if (!params[it]) throw new BadRequestException("Required parameter ${it} was not provided.".toString())
        }
    }
}
