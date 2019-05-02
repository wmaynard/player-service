package com.rumble

import com.rumble.platform.DynamicConfigMap
import com.rumble.platform.RumblePath
import groovyx.net.http.HttpResponseException
import groovyx.net.http.RESTClient
import org.json.JSONException
import org.json.simple.JSONArray
import org.json.simple.JSONObject
import org.json.simple.parser.JSONParser
import redis.clients.jedis.Jedis
import redis.clients.jedis.JedisPubSub

import java.util.concurrent.atomic.AtomicBoolean

class DynamicConfigService {

    def maxAge = 30000

    AtomicBoolean updating = new AtomicBoolean();

    Map cachedConfigs = [:]

    Map lastUpdateInfo = [:]

    def listenerInitialized;
    def configServiceUrl
    def rumbleKey
    def _isLocal = null

    //RumbleLambdaLogger log = RumbleLambdaLogger.getInstance()

    DynamicConfigService() {
        configServiceUrl = this.getConfigServiceUrl()
        rumbleKey = this.getRumbleKey()

        this.init()
    }

    def init() {
        while (!listenerInitialized) {
            if(isLocal()) {
                listenerInitialized = true
            } else {
                try {
                    def restClient = new RESTClient(configServiceUrl, "application/json")
                    restClient.client.params.setParameter("http.connection.timeout", 2000)
                    restClient.client.params.setParameter("http.socket.timeout", 2000)
                    def config = (Map) restClient?.get(path: "clientConfig", headers: ["RumbleKey": rumbleKey])?.data
                    final host = (String) config?.get("pubsub-host");
                    final port = new Integer((String) config?.get("pubsub-port")).intValue();
                    final auth = (String) config?.get("pubsub-auth");
                    final dynamicConfigService = this
                    new Thread() {
                        @Override
                        void run() {
                            int sleep = 0;
                            while (true) {
                                def jedis = new Jedis(host, port);
                                try {
                                    if (auth != null && auth.length() > 0) {
                                        jedis?.auth(auth);
                                    }
                                    //log.info("Connected to config-service message stream at ${host}:${port}");
                                    sleep = 0;
                                    jedis.subscribe(new JedisPubSub() {
                                        @Override
                                        void onMessage(String s, String message) {
                                            def parts = message.split("\\|")
                                            if (parts.size() > 2) {
                                                def scope = parts[0];
                                                def etag = parts[1];
                                                def event = parts[2];
                                                def cached = dynamicConfigService.cachedConfigs[scope]
                                                if (cached) {
                                                    if (event == 'delete') {
                                                        //log.info("Received config-service notification: '${message}', discarding scope '${scope}'")
                                                        discardConfig(scope)
                                                    } else if (etag && (etag == cached['etag'])) {
                                                        //log.info("Received config-service notification: '${message}', scope '${scope}' is up to date, ignoring")
                                                    } else {
                                                        //log.info("Received config-service notification: '${message}', refreshing scope '${scope}'")
                                                        fetchConfig(scope, (String) cached['etag'])
                                                    }
                                                } else {
                                                    //log.info("Received config-service notification: '${message}', scope '${scope}' is not in cache, ignoring")
                                                }
                                            }
                                        }

                                        @Override
                                        void onPMessage(String s, String s1, String s2) {}

                                        @Override
                                        void onSubscribe(String s, int i) {}

                                        @Override
                                        void onUnsubscribe(String s, int i) {}

                                        @Override
                                        void onPUnsubscribe(String s, int i) {}

                                        @Override
                                        void onPSubscribe(String s, int i) {}
                                    }, 'config-notifications')
                                } catch (Exception e) {
                                    try {
                                        jedis.close();
                                    } catch (Exception ignore) {
                                        //log.error("Exception closing redis connection in config-service message handler: ${e}")
                                    }
                                    if (sleep == 0) {
                                        //log.error("Not connected to config-service message stream: ${e}, connecting.");
                                        sleep = 2;
                                    } else {
                                        //log.error("Not connected to config-service message stream: ${e}, will try to connect in " + sleep + " seconds.");
                                        try {
                                            Thread.sleep(sleep * 1000);
                                        } catch (Exception ignore) {
                                            //
                                        }
                                        sleep = Math.min(sleep * 2, 32);
                                    }
                                }
                            }
                        }
                    }.start()

                    listenerInitialized = true

                } catch (Exception e) {
                    //log.error("Failed to initialize config-service listener from " + configServiceUrl + ": ${e}, will retry in 10 seconds.");
                    try {
                        Thread.sleep(10000);
                    } catch (Exception ignore) {
                        //
                    }
                }
            }
        }
    }

    DynamicConfigMap getGameConfig(String gukey, boolean allowCached = true) {

        def config

        if (gukey && gukey.matches('[0-9a-f]{32}')) {

            config = getConfig("game:${gukey}", allowCached)

            /*if (!config) {

                String deploymentName = grailsApplication.config?.deploymentName

                if (deploymentName && deploymentName.startsWith("itdep") && !deploymentName.startsWith("itdep3")) {

                    gukey = "${deploymentName.substring(0,8)}:${gukey}".encodeAsMD5()
                    config = getConfig("game:${gukey}", allowCached)
                }
            }*/

        }

        return config?:new DynamicConfigMap()
    }

    DynamicConfigMap getConfig(String scope, boolean allowCached = true) {

        def cached = allowCached ? cachedConfigs[scope] : null

        cached ? (DynamicConfigMap)cached['config'] : fetchConfig(scope)
    }

    def getRumbleKey() {
        return System.getenv('RUMBLE_KEY') ?: System.getProperty('RUMBLE_KEY')
    }

    def getConfigServiceUrl() {
        return System.getenv('RUMBLE_CONFIG_SERVICE_URL') ?: System.getProperty('RUMBLE_CONFIG_SERVICE_URL')
    }

    def discardConfig(scope) {
        synchronized(cachedConfigs) {
            cachedConfigs.remove(scope);
        }
    }

    DynamicConfigMap fetchConfig(String scope, String etag = null) {
        // Determine if local file or remote URL
        if(isLocal()) {
            try {
                //log.info("Attempting to fetch config at: ${configServiceUrl}${scope}")
                def contents = RumblePath.readFile(configServiceUrl + scope)
                JSONParser parser = new JSONParser()
                if(contents) {
                    JSONObject obj = parser.parse(contents)
                    def config = new DynamicConfigMap(this.toMap(obj))
                    return config
                }
            } catch(JSONException e) {
                //log.error("Error initializing config service from files: ${e}")
                return new DynamicConfigMap()
            }
        } else {

            try {
                def restClient = new RESTClient(configServiceUrl, "application/json")
                restClient.client.params.setParameter("http.connection.timeout", 2000)
                restClient.client.params.setParameter("http.socket.timeout", 2000)
                def response = restClient.get(path: "config/$scope", headers: ["RumbleKey": rumbleKey, "If-None-Match": etag])
                switch (response.status) {
                    case 200:
                        def config = new DynamicConfigMap(response['data'] + [scope: scope])
                        synchronized (cachedConfigs) {
                            cachedConfigs[scope] = [
                                    config     : config,
                                    etag       : response.getFirstHeader('etag')?.value,
                                    fetchedTime: System.currentTimeMillis()
                            ]
                        }
                        return config
                    case 304:
                        synchronized (cachedConfigs) {
                            ((Map) cachedConfigs[scope])?.put('fetchedTime', System.currentTimeMillis())
                        }
                        return new DynamicConfigMap()
                    default:
                        return new DynamicConfigMap()
                }
            } catch (HttpResponseException e) {
                switch (e['response']?.status) {
                    case 401:
                    case 403:
                        //log.error("Auth error ${e['response'].status} talking to config service.")
                        return new DynamicConfigMap()
                    case 404:
                        synchronized (cachedConfigs) {
                            cachedConfigs[scope] = [
                                    config     : new DynamicConfigMap(),
                                    fetchedTime: System.currentTimeMillis()
                            ]
                        }
                        return new DynamicConfigMap()
                    default:
                        return new DynamicConfigMap()
                }
            } catch (Exception e) {
                //log.error("Error calling config service: ${e}")
                return new DynamicConfigMap()
            }
        }
    }

    boolean isLocal() {
        if(_isLocal == null) {
            _isLocal = RumblePath.isLocalPath(configServiceUrl)
        }

        return _isLocal
    }

    // Source: https://stackoverflow.com/questions/21720759/convert-a-json-string-to-a-hashmap

    public static Map<String, Object> toMap(JSONObject object) throws JSONException {
        Map<String, Object> map = new HashMap<String, Object>();

        Set<String> set = object.keySet();
        Iterator<String> keysItr = set.iterator()
        while(keysItr.hasNext()) {
            String key = keysItr.next();
            Object value = object.get(key);

            if(value instanceof JSONArray) {
                value = toList((JSONArray) value);
            }

            else if(value instanceof JSONObject) {
                value = toMap((JSONObject) value);
            }
            map.put(key, value);
        }
        return map;
    }

    public static List<Object> toList(JSONArray array) throws JSONException {
        List<Object> list = new ArrayList<Object>();
        for(int i = 0; i < array.length(); i++) {
            Object value = array.get(i);
            if(value instanceof JSONArray) {
                value = toList((JSONArray) value);
            }

            else if(value instanceof JSONObject) {
                value = toMap((JSONObject) value);
            }
            list.add(value);
        }
        return list;
    }
}
