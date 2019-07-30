package com.rumble;

import org.apache.commons.logging.LogFactory;
import org.apache.log4j.MDC;

import java.util.Map;

public class Log {

    private org.apache.commons.logging.Log log;

    public Log(org.apache.commons.logging.Log log) {
        this.log = log;
    }

    public Log(Class clazz) {
        this(LogFactory.getLog(clazz));
    }

    public void trace(Object o) {
        trace(o, null, null);
    }

    public void trace(Object o, Throwable throwable) {
        trace(o, throwable, null);
    }

    public void trace(Object o, Map data) {
        trace(o, null, data);
    }

    public void trace(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.trace(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }

    public void debug(Object o) {
        debug(o, null, null);
    }

    public void debug(Object o, Throwable throwable) {
        debug(o, throwable, null);
    }

    public void debug(Object o, Map data) {
        debug(o, null, data);
    }

    public void debug(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.debug(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }


    public void info(Object o) {
        info(o, null, null);
    }

    public void info(Object o, Throwable throwable) {
        info(o, throwable, null);
    }

    public void info(Object o, Map data) {
        info(o, null, data);
    }

    public void info(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.info(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }

    public void warn(Object o) {
        warn(o, null, null);
    }

    public void warn(Object o, Throwable throwable) {
        warn(o, throwable, null);
    }

    public void warn(Object o, Map data) {
        warn(o, null, data);
    }

    public void warn(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.warn(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }

    public void error(Object o) {
        error(o, null, null);
    }

    public void error(Object o, Throwable throwable) {
        error(o, throwable, null);
    }

    public void error(Object o, Map data) {
        error(o, null, data);
    }

    public void error(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.error(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }

    public void fatal(Object o) {
        fatal(o, null, null);
    }

    public void fatal(Object o, Throwable throwable) {
        fatal(o, throwable, null);
    }

    public void fatal(Object o, Map data) {
        fatal(o, null, data);
    }

    public void fatal(Object o, Throwable throwable, Map data) {
        if (data != null) {
            MDC.put("data", data);
        } else {
            MDC.remove("data");
        }
        try {
            log.fatal(o, throwable);
        } finally {
            MDC.remove("data");
        }
    }
}