package com.rumble.platform.exception

/*
Thrown by platform core when a authentication or authorization error is detected.

Rendered by exception controller as an HTTP 403.
 */
class AuthException extends PlatformException {

    public AuthException(def debugText = null, Throwable cause = null) {
        super('auth', debugText, cause)
    }
}
