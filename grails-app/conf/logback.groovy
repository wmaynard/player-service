import ch.qos.logback.ext.loggly.LogglyBatchAppender
import com.rumble.platform.common.JsonLayout
import grails.util.BuildSettings
import grails.util.Environment
import org.springframework.boot.logging.logback.ColorConverter
import org.springframework.boot.logging.logback.WhitespaceThrowableProxyConverter

import java.nio.charset.Charset

def TESTING_LOGGLY = false
conversionRule 'clr', ColorConverter
conversionRule 'wex', WhitespaceThrowableProxyConverter

// See http://logback.qos.ch/manual/groovy.html for details on configuration
appender('STDOUT', ConsoleAppender) {
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

def rootErrorLogOutput = ['STDOUT']

if (!Environment.isDevelopmentMode() || TESTING_LOGGLY) {
    JsonLayout.component = 'player-service'
    def epu = "${System.getProperty('LOGGLY_URL')}tag/player-service/".replaceAll('/inputs/','/bulk/')
    System.out.println("Setting Loggly endpoint to: ${epu}")
    appender("loggly", LogglyBatchAppender) {
        endpointUrl = epu
        pattern = '%d{"ISO8601", UTC} %p %t %c{0}.%M - %m%n'
        layout(JsonLayout)
        maxNumberOfBuckets = 8
        maxBucketSizeInKilobytes = 1024
        flushIntervalInSeconds = 3
    }
    rootErrorLogOutput.add('loggly')
    logger("com.rumble", INFO, ['loggly'])
} else if(Environment.isDevelopmentMode()) {
    logger("com.rumble", INFO)
}

def targetDir = BuildSettings.TARGET_DIR
if (Environment.isDevelopmentMode() && targetDir != null) {
    appender("FULL_STACKTRACE", FileAppender) {
        file = "${targetDir}/stacktrace.log"
        append = true
        encoder(PatternLayoutEncoder) {
            pattern = "%level %logger - %msg%n"
        }
    }
    logger("StackTrace", ERROR, ['FULL_STACKTRACE'], false)
} else {
    // silence error logging for uncaught exceptions (PlatformErrorController logs them if appropriate)
    logger("StackTrace", OFF)
}
root(ERROR, rootErrorLogOutput)

// Silence warn logging for no mapping found for HTTP request with URI
logger("org.springframework.web.servlet.DispatcherServlet", ERROR)