/**
 *  Grails Service that would give GeoLocations based on ip address
 *  Delegates the lookup to a DatabaseReader
 *  Two key methods for use:
 *  getCountry(ipAddress) returns a 2 character String of the country code
 *  getContinent(ipAddress) returns a 2 character String of the continent code
 */
package com.rumble.geoip

import com.maxmind.geoip2.GeoIp2Provider
import com.maxmind.geoip2.exception.GeoIp2Exception
import com.maxmind.geoip2.model.CountryResponse
import com.rumble.geoiplib.MonitoredDatabaseReader
//TODO: import com.rumble.platform.gamelaunch.lambda.RumbleLambdaLogger

class GeoLookupService {
    def transactional = false // Required for grails apps without Transactions
    static GeoIp2Provider dbReader // DatabaseReader that reads an IMDB
    //TODO: static RumbleLambdaLogger log = RumbleLambdaLogger.getInstance()

    static Map countryToContinent = [
            A1: null, A2: null, AD: "EU", AE: "AS", AF: "AS", AG: "NA",
            AI: "NA", AL: "EU", AM: "AS", AN: "NA", AO: "AF", AP: "AS",
            AQ: "AN", AR: "SA", AS: "OC", AT: "EU", AU: "OC", AW: "NA",
            AX: "EU", AZ: "AS", BA: "EU", BB: "NA", BD: "AS", BE: "EU",
            BF: "AF", BG: "EU", BH: "AS", BI: "AF", BJ: "AF", BL: "NA",
            BM: "NA", BN: "AS", BO: "SA", BR: "SA", BS: "NA", BT: "AS",
            BV: "AN", BW: "AF", BY: "EU", BZ: "NA", CA: "NA", CC: "AS",
            CD: "AF", CF: "AF", CG: "AF", CH: "EU", CI: "AF", CK: "OC",
            CL: "SA", CM: "AF", CN: "AS", CO: "SA", CR: "NA", CU: "NA",
            CV: "AF", CX: "AS", CY: "AS", CZ: "EU", DE: "EU", DJ: "AF",
            DK: "EU", DM: "NA", DO: "NA", DZ: "AF", EC: "SA", EE: "EU",
            EG: "AF", EH: "AF", ER: "AF", ES: "EU", ET: "AF", EU: "EU",
            FI: "EU", FJ: "OC", FK: "SA", FM: "OC", FO: "EU", FR: "EU",
            FX: "EU", GA: "AF", GB: "EU", GD: "NA", GE: "AS", GF: "SA",
            GG: "EU", GH: "AF", GI: "EU", GL: "NA", GM: "AF", GN: "AF",
            GP: "NA", GQ: "AF", GR: "EU", GS: "AN", GT: "NA", GU: "OC",
            GW: "AF", GY: "SA", HK: "AS", HM: "AN", HN: "NA", HR: "EU",
            HT: "NA", HU: "EU", ID: "AS", IE: "EU", IL: "AS", IM: "EU",
            IN: "AS", IO: "AS", IQ: "AS", IR: "AS", IS: "EU", IT: "EU",
            JE: "EU", JM: "NA", JO: "AS", JP: "AS", KE: "AF", KG: "AS",
            KH: "AS", KI: "OC", KM: "AF", KN: "NA", KP: "AS", KR: "AS",
            KW: "AS", KY: "NA", KZ: "AS", LA: "AS", LB: "AS", LC: "NA",
            LI: "EU", LK: "AS", LR: "AF", LS: "AF", LT: "EU", LU: "EU",
            LV: "EU", LY: "AF", MA: "AF", MC: "EU", MD: "EU", ME: "EU",
            MF: "NA", MG: "AF", MH: "OC", MK: "EU", ML: "AF", MM: "AS",
            MN: "AS", MO: "AS", MP: "OC", MQ: "NA", MR: "AF", MS: "NA",
            MT: "EU", MU: "AF", MV: "AS", MW: "AF", MX: "NA", MY: "AS",
            MZ: "AF", NA: "AF", NC: "OC", NE: "AF", NF: "OC", NG: "AF",
            NI: "NA", NL: "EU", NO: "EU", NP: "AS", NR: "OC", NU: "OC",
            NZ: "OC", O1: null, OM: "AS", PA: "NA", PE: "SA", PF: "OC",
            PG: "OC", PH: "AS", PK: "AS", PL: "EU", PM: "NA", PN: "OC",
            PR: "NA", PS: "AS", PT: "EU", PW: "OC", PY: "SA", QA: "AS",
            RE: "AF", RO: "EU", RS: "EU", RU: "EU", RW: "AF", SA: "AS",
            SB: "OC", SC: "AF", SD: "AF", SE: "EU", SG: "AS", SH: "AF",
            SI: "EU", SJ: "EU", SK: "EU", SL: "AF", SM: "EU", SN: "AF",
            SO: "AF", SR: "SA", ST: "AF", SV: "NA", SY: "AS", SZ: "AF",
            TC: "NA", TD: "AF", TF: "AN", TG: "AF", TH: "AS", TJ: "AS",
            TK: "OC", TL: "AS", TM: "AS", TN: "AF", TO: "OC", TR: "EU",
            TT: "NA", TV: "OC", TW: "AS", TZ: "AF", UA: "EU", UG: "AF",
            UM: "OC", US: "NA", UY: "SA", UZ: "AS", VA: "EU", VC: "NA",
            VE: "SA", VG: "NA", VI: "NA", VN: "AS", VU: "OC", WF: "OC",
            WS: "OC", YE: "AS", YT: "AF", ZA: "AF", ZM: "AF", ZW: "AF"
    ]

    /**
     *  Method to initialize the GeoLookupService. PluginBootStrap calls this upon startup
     *  Also can be called to reinitialize the Database
     */
    static void init(String dbPath) {
        try {
            if(dbReader == null) {
                //File db = new File(Holders.getGrailsApplication().mergedConfig.dbPath)
                File db = new File(dbPath)
                dbReader = new MonitoredDatabaseReader(db);
            }
        } catch (FileNotFoundException e) {
            //TODO: log.error("${this} :: Database file was not found. $e");
        } catch (all) {
            //TODO: log.error("${this} :: ${all.getMessage()}")
        }
    }

    /**
     * Method to initialize GeoLookupService given a particular Database file
     * @param dbFile File database
     */
    static void init(File dbFile) {
        try {
            if(dbReader == null) {
                dbReader = new MonitoredDatabaseReader(dbFile);
            }
        } catch (FileNotFoundException e) {
            //TODO: log.error("${this} :: Database file was not found. $e")
        } catch (all) {
            //TODO: og.error("${this} :: ${all.getMessage()}")
        }
    }

    /**
     * Checks if an ip address is a reserved ip by converting it to an InetAddress object
     * @param ipAddress a String representing an ip address
     * @return true if ip address is reserved otherwise false
     */
    static boolean isReservedIp(String ipAddress) {
        ipAddress ? isReservedIp(InetAddress.getByName(ipAddress)) : false
    }

    /**
     * Checks if an inet address is reserved
     * @param inetAddress an InetAddresss
     * @return true if ip address is reserved
     */
    static boolean isReservedIp(InetAddress inetAddress){
        def i = inetAddress.getAddress()
        return (    i[0] == (byte) 10
                || (i[0] == (byte) 192 && i[1] == (byte) 168)
                || (i[0] == (byte) 172 && (i[1] >= (byte) 16 && i[1] <= (byte) 31))
        )
    }

    /* Gets the ip address based on a request*/
    static String getIpAddress(request) {
        //def headers = request.get('headers')
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
        //if (!ipAddress) ipAddress = request.getHeader('requestContext')?.get('identity')?.get('sourceIp')
        return ipAddress ?: request.remoteAddr
    }

    /**
     * Returns data of an ip address in a MaxMind object
     * @param ipAddress String
     * @throws com.maxmind.geoip2.exception.GeoIp2Exception if ip address is not in the Database
     * @return CountryResponse MaxMind object that stores data
     * @return null if ipAddress is Loopback Address or is reserved. Or if an exception is thrown.
     */
    static CountryResponse getLocation(String ipAddress) {
        def inetAddress = ipAddress ? InetAddress.getByName(ipAddress) : null

        if (!inetAddress) {
            //TODO: log.warn("${this}.getLocation() :: Inet Address is $inetAddress")
            return null
        }

        if (inetAddress.isLoopbackAddress()) {
            //TODO: log.warn("${this}.getLocation() :: $inetAddress is Loopback Address")
            return null
        }

        if (isReservedIp(inetAddress)) {
            //TODO: log.warn("${this}.getLocation() :: $inetAddress is Reserved IP")
            return null
        }

        try {
            def attempts = 0
            while (dbReader == null && attempts < 2) {
                // Database was not inialized
                //TODO: log.warn("${this}.getLocation() :: Database was not initialized")
                //TODO: log.warn("${this}.getLocation() :: Attempting to initialize")
                init()
                attempts++
            }
            if (dbReader == null) {
                //TODO: log.error("${this}.getLocation() :: Unable to initialize Database")
                return null
            }
            try {
                CountryResponse response = dbReader.country(inetAddress)
                return response
            } catch (IOException e) {
                //TODO: log.error("${this}.getLocation() :: Caught $e. Check is Database file exists, or the path ${this} is reading.")
            } catch (GeoIp2Exception e) {
                //TODO: log.error("${this}.getLocation() :: Caught $e. Check if Database is corrupted, or IP address is invalid.")
            } catch (all) {
                //TODO: log.error(all.getMessage())
            }
        } catch (GeoIp2Exception e) {
            //TODO: log.error("${this}.getLocation() :: $e")
        } catch (all) {
            //TODO: log.error("${this}.getLocation() :: ${all.getMessage()}")
        }

        return null
    }

    /**
     *  Gets the country code given an ip address
     *  @param ipAddress a String representing an ip address
     *  @return a 2 character country code that corresponds to the ip address or empty String if an error occured
     */
    static String getCountry(String ipAddress) {
        def location = getLocation(ipAddress)
        if (location == null) {
            return ""
        }
        try {
            def country = location.getCountry()
            if (country == null) {
                /*TODO: log.warn("""${this}.getCountry($ipAddress) :: Unable to get country.
                    Database: $dbReader. Result: $country.""")*/
                return ""
            } else {
                return country.getIsoCode()
            }
        } catch (IOException e) {
            //TODO: log.error("${this}.getCountry() :: Caught $e. Check is Database file exists, or the path ${this} is reading.")
        } catch (GeoIp2Exception e) {
            //TODO: log.error("${this}.getCountry() :: Caught $e. Check if Database is corrupted, or IP address is invalid.")
        }
    }

    /**
     * Gets the continent code given an ip address
     * @param  ipAddress a String representing an ip address
     * @return a 2 character continent code that corresponds to the ip address or empty String if an error occured
     */
    static String getContinent(String ipAddress) {
        def location = getLocation(ipAddress)
        if (location == null) {
            return ""
        }
        try {
            def continent = location.getContinent()
            if (continent == null) {
                /*TODO: log.warn("""${this}.getContinent($ipAddress) :: Unable to get country.
                    Database: $dbReader. Result: $continent.""")*/
                return ""
            } else {
                return continent.getCode()
            }
        } catch (IOException e) {
            //TODO: log.error("${this}.getContinent() :: Caught $e. Check is Database file exists, or the path ${this} is reading.")
        } catch (GeoIp2Exception e) {
            //TODO: log.error("${this}.getContinent() :: Caught $e. Check if Database is corrupted, or IP address is invalid.")
        }
    }
}
