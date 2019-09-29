import ch.qos.logback.ext.loggly.LogglyBatchAppender
import com.rumble.platform.common.JsonLayout
import grails.util.Environment
import org.springframework.boot.logging.logback.ColorConverter
import org.springframework.boot.logging.logback.WhitespaceThrowableProxyConverter

import java.nio.charset.Charset

def logglyEnabled = System.getProperty('LOGGLY_ENABLED', !Environment.isDevelopmentMode()) as Boolean

// See http://logback.qos.ch/manual/groovy.html for details on configuration

conversionRule 'clr', ColorConverter
conversionRule 'wex', WhitespaceThrowableProxyConverter

appender('console', ConsoleAppender) {
    encoder(PatternLayoutEncoder) {
        charset = Charset.forName('UTF-8')

        pattern =
                '%clr(%d{yyyy-MM-dd HH:mm:ss.SSS}){faint} ' + // Date
                        '%clr(%5p) ' + // Log level
                        '%clr(---){faint} %clr([%15.15t]){faint} ' + // Thread
                        '%clr(%-40.40logger{39}){cyan} %clr(:){faint} ' + // Logger
                        '%m%n%wex' // Message
    }
}

if (logglyEnabled) {
    def epu = "${System.getProperty('LOGGLY_URL')}tag/${JsonLayout.component}/".replaceAll('/inputs/','/bulk/')
    System.out.println("Setting Loggly endpoint to: ${epu}")
    appender("loggly", LogglyBatchAppender) {
        endpointUrl = epu
        pattern = '%d{"ISO8601", UTC} %p %t %c{0}.%M - %m%n'
        layout(JsonLayout)
        maxNumberOfBuckets = 8
        maxBucketSizeInKilobytes = 1024
        flushIntervalInSeconds = 3
    }
    root(ERROR, ['console', 'loggly'])
} else {
    root(ERROR, ['console'])
}

if (!Environment.isDevelopmentMode()) {
    logger("com.rumble", INFO)
} else {
    logger("com.rumble", System.getProperty('LOG_LEVEL','INFO'))
}

// silence error logging for uncaught exceptions (PlatformErrorController logs them if appropriate)
logger("StackTrace", OFF)
logger("org.grails.web.errors.GrailsExceptionResolver", OFF)
