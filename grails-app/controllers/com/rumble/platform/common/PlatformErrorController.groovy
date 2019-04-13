package com.rumble.platform.common

import com.rumble.platform.exception.ApplicationException
import com.rumble.platform.exception.AuthException
import com.rumble.platform.exception.BadRequestException
import com.rumble.platform.exception.PlatformException
import grails.converters.JSON
import org.springframework.web.context.request.RequestContextHolder

class PlatformErrorController {

    def uncaughtException() {

        try {

            def e = request.exception?.cause ?: request.exception

            if (e instanceof AuthException) {
                response.status = 403
            } else if (e instanceof BadRequestException) {
                response.status = 400
            } else if (e instanceof ApplicationException) {
                response.status = 200
            } else {
                response.status = 500
            }

            def message = PlatformException.toResponseMessage(e)

            if (response.status == 500) {
                log.error(message.errorCode, e)
            }

            render(message as JSON)

        } catch (Exception e) {
            log.error('Exception thrown handling error', e)
            render ([success: false, errorCode: 'error', debugText: 'Exception thrown handling error'] as JSON)
        }
    }

    def notFound() {

        render ([ success: false, errorCode: 'notFound' ] as JSON)
    }
}
