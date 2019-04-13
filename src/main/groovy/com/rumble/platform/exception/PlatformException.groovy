package com.rumble.platform.exception

import org.springframework.web.context.request.RequestContextHolder

class PlatformException extends RuntimeException {

    def errorCode
    def responseData

    public PlatformException(def errorCode, def debugText, Throwable cause = null, Map responseData = null) {
        super((debugText?:errorCode) as String, cause)
        this.errorCode = errorCode
        this.responseData = responseData
    }

    static toResponseMessage(e) {

        def message

        if (PlatformException.class.isAssignableFrom(e.class)) {
            message = (e.responseData ?: [:]) + [
                    success  : false,
                    errorCode: e.errorCode as String,
                    debugText: e.message as String
            ]
        } else {
            message = [
                    success  : false,
                    errorCode: 'uncaughtException',
                    debugText: (e?.cause ?: e) as String
            ]
        }
        return message
    }

}
