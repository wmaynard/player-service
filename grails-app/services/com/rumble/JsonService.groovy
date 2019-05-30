package com.rumble

import groovy.json.JsonGenerator
import groovy.json.JsonGenerator.Converter
import org.bson.types.ObjectId

class JsonService {

    private static jsonGenerator

    static JsonGenerator generator(){
        if(jsonGenerator == null) {
            jsonGenerator = new JsonGenerator.Options()
                    .addConverter(new Converter() { // Custom generator implemented via Converter interface.
                /**
                 * Indicate which type this generator can handle.
                 */
                @Override
                boolean handles(Class<?> type) {
                    return (type && type == ObjectId)
                }

                /**
                 * Logic to convert object.
                 */
                @Override
                Object convert(Object obj, String key) {
                    return obj.toString()
                }
            }).build() // Create the generator instance.
        }

        return jsonGenerator
    }

    static def toJson(data){
        def g = generator()
        return g.toJson(data)
    }
}
