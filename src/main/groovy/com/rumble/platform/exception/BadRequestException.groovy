package com.rumble.platform.exception

/*
Thrown by application controllers in response to inputs that indicate programmer error.

Rendered by exception controller as an HTTP 400.
 */
class BadRequestException extends PlatformException {
    public BadRequestException(def debugText = null, Throwable cause = null) {
        super('badRequest', debugText, cause)
    }
}
