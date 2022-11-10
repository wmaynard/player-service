using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using PlayerService.Models.Login;
using Rumble.Platform.Common.Attributes;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Data;

namespace PlayerService.Services;

public class SaltService : PlatformMongoService<Salt>
{
    public SaltService() : base("salt") { }

    public Salt Fetch(string username)
    {
        Salt upsert = new Salt
        {
            Username = username,
            Value = Guid.NewGuid().ToString()
        };
        
        return _collection
            .Find(filter: Builders<Salt>.Filter.Eq(salt => salt.Username, upsert.Username))
            .FirstOrDefault() 
            ?? _collection
                .FindOneAndUpdate(
                    filter: Builders<Salt>.Filter.Eq(salt => salt.Username, upsert.Username),
                    update: Builders<Salt>.Update
                        .Set(salt => salt.Username, upsert.Username)
                        .Set(salt => salt.Value, upsert.Value),
                    options: new FindOneAndUpdateOptions<Salt>
                    {
                        IsUpsert = true,
                        ReturnDocument = ReturnDocument.After
                    }
                );
    }
}