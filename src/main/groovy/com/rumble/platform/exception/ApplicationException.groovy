package com.rumble.platform.exception

/*
Thrown by application code when a logical error is detected.

Rendered by exception controller as an HTTP 200.
 */
class ApplicationException extends PlatformException {
    public ApplicationException(def errorCode, def debugText = null, Throwable cause = null, Map responseData = null) {
        super(errorCode, debugText, cause, responseData)
    }
}
