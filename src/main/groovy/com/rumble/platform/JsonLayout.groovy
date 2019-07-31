package com.rumble.platform.common

import ch.qos.logback.classic.spi.ILoggingEvent
import ch.qos.logback.classic.spi.IThrowableProxy
import ch.qos.logback.classic.spi.ThrowableProxyUtil
import ch.qos.logback.core.LayoutBase

import org.apache.log4j.MDC
import org.json.JSONException
import org.json.JSONWriter

import java.text.DateFormat
import java.text.SimpleDateFormat

class JsonLayout extends LayoutBase<ILoggingEvent> {

    private static ThreadLocal<DateFormat> dateFormat = new ThreadLocal<DateFormat>() {
        @Override
        protected DateFormat initialValue() {
            return new SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS Z");
        }
    };

    private static ThreadLocal<CharArrayWriter> caw = new ThreadLocal<CharArrayWriter>() {
        @Override
        protected CharArrayWriter initialValue() {
            return new CharArrayWriter();
        }
    };

    private static String hostName;

    static {
        try {
            hostName = java.net.InetAddress.getLocalHost().getHostName();
        } catch (Exception e) {
            e. printStackTrace();
        }
    }

    String doLayout(ILoggingEvent event) {

        CharArrayWriter sw = caw.get();

        sw.reset();

        try {

            Date now = new Date();

            JSONWriter json = new JSONWriter(sw);
            json.object()

            Map mdc = MDC.getContext()

            if (mdc != null) {
                mdc.each { k, v ->
                    json.key(k)
                    json.value(v)
                }
            }

            MDC.remove("data")

            def deployment = System.getProperty('RUMBLE_DEPLOYMENT') ?: System.getenv('RUMBLE_DEPLOYMENT')
            if(deployment) {
                json.key('env')
                json.value(deployment)
            }

            json.key("time_ms");
            json.value(now.getTime());
            json.key("time");
            json.value(dateFormat.get().format(now));
            json.key("severity");
            json.value(event.getLevel().toString());
            json.key("hostName");
            json.value(hostName);
            json.key("severityIndex");
            json.value(event.getLevel().toInt() / 10000);

            String rumPlayer = (String)MDC.get("rumPlayer");

            if (rumPlayer != null) {
                json.key("rumPlayer");
                json.value(rumPlayer);
            }

            String transactionName = (String)MDC.get("transactionName");

            if (transactionName != null) {
                json.key("transactionName");
                json.value(transactionName);
            }

            String requestId = (String)MDC.get("requestId");

            if (transactionName != null) {
                json.key("requestId");
                json.value(requestId);
            }

            IThrowableProxy proxy = event.getThrowableProxy()
            if(proxy != null) {
                /*String throwableStr = ThrowableProxyUtil.asString(proxy)
                sbuf.append(throwableStr)
                sbuf.append(CoreConstants.LINE_SEPARATOR)*/
                //json.key("exception");
                //json.value(throwable.getClass().getCanonicalName());
                json.key("trace");
                //appendTrace(json, throwable);
                def throwableStr = ThrowableProxyUtil.asString(proxy)
                json.value(throwableStr)
            }
            /*ThrowableInformation ti = event.getThrowableInformation();
            Throwable throwable = (ti != null) ? ti.getThrowable() : null;

            if (throwable != null) {
                json.key("exception");
                json.value(throwable.getClass().getCanonicalName());
                json.key("trace");
                appendTrace(json, throwable);
            }*/

            json.key("message");
            json.value(event.getMessage());
            json.key("thread");
            json.value(Thread.currentThread().getName());

            Object mdcInfo = MDC.get("mdcInfo");

            if (mdcInfo != null) {
                json.key("mdcInfo");
                json.value(mdcInfo);
            }

            json.endObject();

        } catch (JSONException e) {
            e.printStackTrace(new PrintWriter(sw));
        }

        sw.append("\n");

        return sw.toString();
    }

    private void appendTrace(JSONWriter json, Throwable throwable) throws JSONException
    {
        json.array();

        StackTraceElement[] enclosingTrace = null;

        while (throwable != null)
        {
            json.object();

            json.key("exception");
            json.value(throwable.getClass().getCanonicalName());
            json.key("message");
            json.value(throwable.getMessage());

            StackTraceElement[] stackTrace = throwable.getStackTrace();

            json.key("stack");
            appendStack(json, stackTrace, enclosingTrace, 0);

            enclosingTrace = stackTrace;

            throwable = throwable.getCause();

            json.endObject();
        }

        json.endArray();
    }

    public static void appendStack(JSONWriter json, StackTraceElement[] stackTrace, StackTraceElement[] referenceTrace, int skipElements) throws JSONException
    {
        // Count common stack trace entries.
        int commonEntries = 0;
        if (referenceTrace != null)
        {
            if ((stackTrace.length > 0) && (referenceTrace.length > 0))
            {
                int i1 = stackTrace.length - 1;
                int i2 = referenceTrace.length - 1;
                while ((i1 >= 0) && (i2 >= 0))
                {
                    if (stackTrace[i1].equals(referenceTrace[i2]))
                    {
                        ++commonEntries;
                        --i1;
                        --i2;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        final int uniqueEntries = stackTrace.length - commonEntries;

        json.array();
        for (int i = skipElements; i < uniqueEntries; ++i)
        {
            if (frameFilter(stackTrace[i].toString())){
                json.value("at " + stackTrace[i].toString());
            }
        }
        if (commonEntries > 0)
        {
            json.value("... " + Integer.toString(commonEntries) + " more");
        }
        json.endArray();
    }

    public static boolean frameFilter(String frame){
        List<String> filterClasses = Arrays.asList("org.codehaus.groovy",
                "grails.plugin.cache",
                "sun.reflect",
                "org.springsource",
                "org.springframework",
                "ApplicationFilterChain",
                "ApplicationDispatcher");
        for (String filter : filterClasses) {
            if (frame.contains(filter)){
                return false;
            }
        }
        return true;
    }
}