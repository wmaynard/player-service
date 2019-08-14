package com.rumble.platform.common

import com.rumble.platform.TypeConvertingMap
import org.json.simple.parser.JSONParser

class DynamicConfigMap extends TypeConvertingMap {

    JSONParser parser = new JSONParser()

    DynamicConfigMap() {
        super()
    }

    DynamicConfigMap(Map map) {
        super(map)
    }

    Object clone() {
        new DynamicConfigMap(new LinkedHashMap(this.@wrappedMap))
    }

    // TODO: find a safe and efficient way to cache parsed values
    Map map(String name, Map defaultValue=[:]) {
        String val = get(name)
        if (val) {
            try {
                return parser.parse(val) as Map
            } catch (Exception e) {
                System.err.println("Error getting dynamic config value for '${name}; unable to parse value '${val} as JSON")
            }
        }
        return defaultValue
    }

    List<String> list(String name, List defaultValue=[]) {
        String val = get(name)
        if (val) {
            try {
                def result = parser.parse(val)
                return ((List<String>) result)
            } catch (Exception e) {
                System.err.println("Error getting dynamic config value for '${name}; unable to parse value '${val} as list")
            }
        }
        return defaultValue
    }
}
