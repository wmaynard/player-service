package com.rumble.geoip

import com.amazonaws.services.s3.AmazonS3ClientBuilder
import com.amazonaws.services.s3.model.GetObjectRequest
import com.amazonaws.services.s3.model.ObjectMetadata
import com.maxmind.geoip2.DatabaseReader
import com.maxmind.geoip2.exception.GeoIp2Exception
import com.maxmind.geoip2.model.CountryResponse
import com.rumble.platform.exception.ApplicationException

class GeoLookupService {

    def logger = new com.rumble.Log(this.class)

    DatabaseReader dbReader

    def init() {
        update()
    }

    def update() {
        try {
            def clientRegion = System.getProperty("GEO_IP_S3_REGION") ?: System.getenv("GEO_IP_S3_REGION")
            def bucketName = System.getProperty("GEO_IP_S3_BUCKET") ?: System.getenv("GEO_IP_S3_BUCKET")
            def s3Key = System.getProperty("GEO_IP_S3_KEY") ?: System.getenv("GEO_IP_S3_KEY")

            if (!clientRegion || !bucketName || !s3Key) {
                throw new ApplicationException(null, "Missing environment variable(s)", null, [
                        clientRegion: clientRegion ?: null,
                        bucketName  : bucketName ?: null,
                        s3Key       : s3Key ?: null
                ])
            }

            def tmpFolder = File.createTempFile('dummy','ext').parentFile
            def geoIpDbFile = new File(tmpFolder, "geo-ip.mmdb")

            def s3Client = AmazonS3ClientBuilder.standard()
                    .withRegion(clientRegion)
                    .build()

            boolean download = true

            if (geoIpDbFile.exists()) {
                ObjectMetadata s3MetaData = s3Client.getObjectMetadata(bucketName, s3Key)
                if (s3MetaData.lastModified.time < geoIpDbFile.lastModified()) {
                    download = false
                    logger.info("Using existing GeoIP DB")
                } else {
                    logger.info("New GeoIP DB available")
                }
            }

            if (download) {

                def tempFile = File.createTempFile("geo-ip", ".mmdb")

                logger.info("Downloading GeoIP DB", [ path: tempFile.absolutePath ])

                s3Client.getObject(new GetObjectRequest(bucketName, s3Key), tempFile)

                logger.info("Moving GeoIP DB", [ source: tempFile.absolutePath, target: geoIpDbFile.absolutePath ])

                tempFile.renameTo(geoIpDbFile)
            }

            if (download || !dbReader) {
                dbReader = new DatabaseReader.Builder(geoIpDbFile).build()
                logger.info("GeoIP DB initialized")
            }

            return true

        } catch (Exception e) {
            logger.error("GeoIP DB initialization failed", e)
            return false
        }
    }

    /**
     * Checks if an ip address is a reserved ip by converting it to an InetAddress object
     * @param ipAddress a String representing an ip address
     * @return true if ip address is reserved otherwise false
     */
    boolean isReservedIp(String ipAddress) {
        ipAddress ? isReservedIp(InetAddress.getByName(ipAddress)) : false
    }

    /**
     * Checks if an inet address is reserved
     * @param inetAddress an InetAddresss
     * @return true if ip address is reserved
     */
    boolean isReservedIp(InetAddress inetAddress){
        def i = inetAddress.getAddress()
        return (    i[0] == (byte) 10
                || (i[0] == (byte) 192 && i[1] == (byte) 168)
                || (i[0] == (byte) 172 && (i[1] >= (byte) 16 && i[1] <= (byte) 31))
        )
    }

    /* Gets the ip address based on a request*/
    String getIpAddress(request) {
        String ipAddress = request.getHeader('X-Real-IP')
        if (!ipAddress) ipAddress = request.getHeader('Client-IP')
        if (!ipAddress){
            // This can be a list of IP Addresses. First one should be the Client.
            def addr = request.getHeader('X-Forwarded-For')?.split(',')
            if(addr) {
                ipAddress = addr[0].trim()
            }
        }
        // Source: https://stackoverflow.com/questions/16558869/getting-ip-address-of-client
        if (!ipAddress) ipAddress = request.getHeader('Proxy-Client-IP')
        if (!ipAddress) ipAddress = request.getHeader('WL-Proxy-Client-IP')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_X_FORWARDED_FOR')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_X_FORWARDED')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_X_CLUSTER_CLIENT_IP')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_CLIENT_IP')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_FORWARDED_FOR')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_FORWARDED')
        if (!ipAddress) ipAddress = request.getHeader('HTTP_VIA')
        if (!ipAddress) ipAddress = request.getHeader('REMOTE_ADDR')
        if (!ipAddress) ipAddress = request.remoteAddr
        return ipAddress
    }

    /**
     * Returns data of an ip address in a MaxMind object
     * @param ipAddress String
     * @throws com.maxmind.geoip2.exception.GeoIp2Exception if ip address is not in the Database
     * @return CountryResponse MaxMind object that stores data
     * @return null if ipAddress is Loopback Address or is reserved. Or if an exception is thrown.
     */
    CountryResponse getLocation(String ipAddress) {
        def inetAddress = ipAddress ? InetAddress.getByName(ipAddress) : null

        if (!inetAddress) {
            log.warn("GeoIP Inet Address is $inetAddress")
            return null
        }

        if (inetAddress.isLoopbackAddress()) {
            log.warn("GeoIP $inetAddress is Loopback Address")
            return null
        }

        if (isReservedIp(inetAddress)) {
            log.warn("GeoIP $inetAddress is Reserved IP")
            return null
        }

        try {
            if (dbReader == null) {
                log.warn("GeoIP Database was not initialized")
                return null
            }
            try {
                CountryResponse response = dbReader.country(inetAddress)
                return response
            } catch (IOException e) {
                log.error("GeoIP Caught $e. Check is Database file exists, or the path ${this} is reading.")
            } catch (GeoIp2Exception e) {
                log.error("GeoIP Caught $e. Check if Database is corrupted, or IP address is invalid.")
            } catch (all) {
                log.error(all.getMessage())
            }
        } catch (GeoIp2Exception e) {
            log.error("GeoIP $e")
        } catch (all) {
            log.error("GeoIP ${all.getMessage()}")
        }

        return null
    }

    /**
     *  Gets the country code given an ip address
     *  @param ipAddress a String representing an ip address
     *  @return a 2 character country code that corresponds to the ip address or empty String if an error occured
     */
    String getCountry(String ipAddress) {
        def location = getLocation(ipAddress)
        if (location == null) {
            return ""
        }
        try {
            def country = location.getCountry()
            if (country == null) {
                log.warn("""${this}.getCountry($ipAddress) :: Unable to get country.
                    Database: $dbReader. Result: $country.""")
                return ""
            } else {
                return country.getIsoCode()
            }
        } catch (IOException e) {
            log.error("${this}.getCountry() :: Caught $e. Check is Database file exists, or the path ${this} is reading.")
        } catch (GeoIp2Exception e) {
            log.error("${this}.getCountry() :: Caught $e. Check if Database is corrupted, or IP address is invalid.")
        }
    }

    /**
     * Gets the continent code given an ip address
     * @param  ipAddress a String representing an ip address
     * @return a 2 character continent code that corresponds to the ip address or empty String if an error occured
     */
    String getContinent(String ipAddress) {
        def location = getLocation(ipAddress)
        if (location == null) {
            return ""
        }
        try {
            def continent = location.getContinent()
            if (continent == null) {
                log.warn("""${this}.getContinent($ipAddress) :: Unable to get country.
                    Database: $dbReader. Result: $continent.""")
                return ""
            } else {
                return continent.getCode()
            }
        } catch (IOException e) {
            log.error("${this}.getContinent() :: Caught $e. Check is Database file exists, or the path ${this} is reading.")
        } catch (GeoIp2Exception e) {
            log.error("${this}.getContinent() :: Caught $e. Check if Database is corrupted, or IP address is invalid.")
        }
    }
}
