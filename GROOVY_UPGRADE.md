# Important Differences From Groovy (v1)

In the first version of player-service, `/player/launch` was monolithic; one launch was required to get client config variables, one launch was required to get an access token, and if there was a conflict of any kind, a third launch was required to select a desired account to use.  This has been redesigned, and this guide explains the changes in detail.

### 1. `GET /player/v2/config` returns dynamic config values.

* Unsecured endpoint; no token required.
* Optionally looks for `clientVersion` in the request's **parameters**.

### 2. `POST /player/v2/launch` has changed significantly.

* Unsecured endpoint; no token required.
* No longer returns dynamic config values.
* Always returns an `accessToken` with account information embedded in it.
* New accounts are assigned a screenname automatically using the `NameGeneratorService`.
* When an `accountConflict` is detected:
	* The request will always fail until `/player/v2/transfer` is called.
	* `mergeToken` is renamed to `transferToken`.

### 3. The term "merge" was previously used when discussing account recovery; now is known as "transfer".

The Groovy player-service never actually "merged" anything.  A merge would mean that two accounts are combined in some way.  For a simple example, if account A has 1000 gems and account B has 500, a truly "merged" account would have 1500 gems.  Instead, "merge" just meant that an SSO profile (e.g. Google Play or Apple ID) was transferred over to a new account, "orphaning" the previous account.

The term "transfer" is far more intuitive here, since player data is never altered.

### 4. The way accounts are linked to data via SSO has changed.

In Groovy, the way accounts are linked to their data is convoluted:

* `/player/launch` looks for an `installId` in the Mongo collection `player`.  If one isn't found, a new record is created.  The `_id` field of this collection is the player's `aid`.
* The service then uses the `installId` - not the `aid` - to look for a **Profile**.  The profile's `aid` field is the one that's used to identify account data.
* This is further complicated when using SSO, which compares multiple profiles (one for the SSO account and one for the `installId`).
* When an account is transferred, its profiles move, including the profile for the `installId`.

This has been simplified:

* The Mongo collection `player` has a new field: `accountIdOverride` (on the data-side: `oaid`).
* `/player/v2/launch` uses `oaid` in place of `aid` if not null.
* When logging in with SSO:
	* Service looks for a profile matching the SSO.  If one is not found, it is attached to the current `aid`.
	* If one is found, an `accountConflict` if the `aid` is different.
	* Rather than moving profiles around during a transfer, the `oaid` is set.

This reduces the amount of lookups that have to happen to retrieve or modify data.  This may have an unintentional side effect, though, of allowing two devices using one SSO account to overwrite each other.  This needs to be tested.  This can be solved with a call to token-service's `/invalidate` if it proves to be an issue.

### 5. `PATCH /player/v2/transfer` is the new way to resolve `accountConflict` errors.

Groovy's conflict resolution only had simple outcomes: either you use the account linked to your SSO or you effectively cancel your request to log in with SSO and only use the local account.  If you wanted to unlink your SSO and transfer it to the local account, you were SOL.  This is problematic for anyone who does not have access to their old device.  And to reiterate, this was handled in `/player/launch`, as part of a branching and complex execution path.

The `transfer` endpoint enables the client to:

* Link the local account to the one their SSO account uses.
* Move their SSO profile to the local account.
* Cancel the transfer.

### 6. `/player/v2/launch` expects SSO data to be in the `sso` field.

The data structure remains the same as Groovy's, but a request body should look like:

```
{
    "installId": "1b1aa757-5a28-4df7-b46a-51b6c0d38113",
    ...
    "sso": {
        "facebook": {
            "accessToken": ""
        },
        "gameCenter": {
            "publicKeyUrl": "https://static.gc.apple.com/public-key/gc-prod-6.cer",
            "signature": "ga4FPQCImbbOFtyyHQ9ZL...",
            "salt": "9xnikA==",
            "timestamp": "1637624701147",
            "bundleId": "com.plarium.towerheroes",
            "playerId": "T:_859d2cd136b97c5040ec69c14aa1d3be"
        }
        "googlePlay": {
            "idToken": "deadbeefdeadbeefdeadbeef"
        }
    }
}
```

### 7. `POST /player/update` is now `PATCH /player/v2/update`.

Previous platform API was built on using POST for everything.  v2 uses more appropriate HTTP methods, but the request body for `/update` remains the same.

### 8. Use `GET /player/v2/items` to retrieve a player's items.


### 9. Some type coercion may occur in the JSON.

Between `platform-csharp-common` making C#'s JSON handling less painful and changes to the admin portal, there may be times when a datatype changes.  For example:

```
{ "foo": "31415926" }
```
might get stored as:
```
{ "foo": 31415926 }
```

While any occurrence of this is unintentional, it may be beneficial to improve the front-end handling of the JSON to allow for this.
