# Logging in with player-service

As of Sprint 42, login has been reworked.  The original goal was only to add Rumble Accounts, but this turned into a complete refactor of the way account management is done.  Previously, player-service would attach a "profile" to a player account.  When logging in, player-service would:

1. Look up a profile from an `installId`.
2. Look up the accountId from above profile.
3. Look up any profiles that matched provided SSO options (e.g. Google, Apple accounts).
4. Look up the accountIds for those returned profiles.
   1. If no accountId was found, a profile would be created.
5. Check to see that all the accountIds matched, and would throw an account conflict if not.
   1. To resolve an account conflict, profiles would change their accountIds.
   2. As separate database entries, there was no limit on the number of profiles an account could have.

The new approach simplifies this process by eliminating Profiles.  Now, each Player document has device information and one of each SSO account type, eliminating the need to bounce around different collections to figure out what information is relevant for a player.  As iOS login has not yet been implemented, all Apple SSO is only stubbed out for now.

In addition to this major change for Profiles, there are new types of login flows to support username / password / 2FA / recovery, etc.  This document will guide you through the various flows introduced with these changes and serves as the authoritative information source on login.


#### **IMPORTANT:** Never send player-service any passwords.

Only send _hashes_.  Under no circumstance are we to store plaintext passwords.  As long as all of our frontend projects use the same hash algorithms / salts, we don't have to worry about leaking passwords in the unfortunate event of a data breach.

# Glossary


| Term                       | Definition                                                                                                                                                                                                 |
|:---------------------------|:-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| 2FA                        | Two-factor authentication.  Requires confirmation links and / or code entry to complete.                                                                                                                   |
| Account / Player / Install | Interchangeable terms for a specific installation                                                                                                                                                          |
| Adopt / Adoption           | The process of making one account the parent of several others.                                                                                                                                            |
| Child                      | A player account that has previously been linked to another account through SSO.                                                                                                                           |
| Conflict                   | A situation in which a player has tried to log in, but Platform found more than one eligible account that could be used.                                                                                   |
| Device / Install           | The hardware, or information about the hardware, a player is using for the game client.                                                                                                                    |
| Hash                       | A hash of a password.  Passwords should _never_ be stored in plaintext, and they should never even be sent to a backend.                                                                                   |
| Link                       | A reference to a different player account ID.  Any account with a `link` is a child account to another.  Parents do not have links.                                                                        |
| Parent                     | The primary account for a player.  When a player logs in with a child account, the token generated is actually for the parent.                                                                             |
| Recovery                   | A process in which a player has forgotten their password and starts to reset it.                                                                                                                           |
| Reset                      | A process priming the password reset by confirming a code from recovery.  This enables a player to set a new password hash without knowing their previous one.                                             |
| Rumble Account             | A traditional login, consisting of an email address, username, and a hash of a password.                                                                                                                   |
| Salt                       | A randomized value generated by Platform.  This value is used by applications to generate password hashes.                                                                                                 |
| SSO                        | Single sign-on.  Allows a player to share an account across multiple devices.  May be Apple, Google, or Rumble accounts.                                                                                   |
| Token                      | A JWT representing authorized access to a particular resource.  Tokens provided by third parties must be verified and translated into Rumble tokens; they cannot be used to access our resources directly. |

## A Primer on Salt & Cryptography

Before we can create or login to a Rumble account - that is, an account with a username and password - it's important to have a basic understanding of how we're protecting our users.  The most important thing to know is that **no client application, either web or device, should be sending passwords to the service.**  Instead, the service only cares about seeing a _hash_ of the password.  The hash must be calculated on the application's side.

We're using BCrypt to tackle this.  It's a widely-supported password-hashing utility, and is available for most major languages.  BCrypt works by taking a "salt" - a randomly-generated value - to protect against rainbow tables (cached / precomputed hashes) and add a work factor in that we can control to deliberately slow down login processes.  If we increase the work factor, the hash takes longer to compute; this means that as hardware improves in processing speed, we can make our system more resilient to more-capable brute-force attacks.

Player-service is responsible for generating the salts used with login.  Each username gets its own salt.  If you generate your own salt at runtime, you won't be able to log in; the salt must remain the same for each account to generate the same hash.  Whenever you need your salt, you must make a request to player-service similar to the following:

```
GET /player/v2/account/salt?username=atakechi

// Sample response
{
    "salt": "$2b$06$IqGk004Uzqx58qTnUCvtfu"
}
```

Once you have this value, you can use BCrypt to calculate your password hash.  Once calculated, an example hash might look like:

```
$2b$06$GXUEm02SBQ.3Ya7lcPlyNeZJ/M5O/A191tsGWYBcQ4iW5JlF3faa
```

This calculated hash is the value that should be sent to player-service when creating or logging into an existing Rumble account.


## Login

First, let's look at what a sample login request looks like.  Every game client request **must** have a `device` field.  The other field, `sso`, is optional, as are all of its contents.  However, if `sso` accounts are specified here, and those accounts can't be found, the login request **will fail**, and no token will be generated.  If, for example, you're not using `appleToken`, do not send the key.

```
POST /player/v2/account/login
{
    "deviceInfo": {
        "installId": "locust-postman",            // Device GUID
        "clientVersion": "0.1.432",
        "language": "English",
        "osVersion": "macOS 11.6",
        "type": "Postman",
        "privateKey": "someRandomValueOrHash"     // See Device Security section for more details
    },
    "sso": {
        "appleToken": "eyJhb....ABSsQ",
        "googleToken": "eyJhb....ABSsQ",
        "rumble": {
            "username": "atakechi",
            "hash": "deadbeefdeadbeefdeadbeefdeadbeef"
        }
    }
}

// Sample Response
{
    "geoData": {
        "continent": "North America",
        "continentCode": "NA",
        "country": "United States",
        "countryCode": "US",
        "registeredCountry": "United States",
        "registeredCountryCode": null,
        "ipAddress": "73.162.30.116"
    },
    "requestId": "d5446978d8cb4f022ae0a65e08118676",
    "player": {
        "appleAccount": null,
        "createdOn": 1672947347,
        "deviceInfo": {
            "clientVersion": "0.1.432",
            "dataVersion": null,
            "installId": "ead2650691e50ba2f4079632c11cf9fe",
            "language": null,
            "osVersion": null,
            "type": null
        },
        "discriminator": 3850,
        "googleAccount": null,
        "lastLogin": 1672952598,
        "rumbleAccount": {
            "associatedAccounts": [
                "6375681659c472bca7dabc40"
            ],
            "email": "darius.germano@rumbleentertainment.com",
            "status": 18,
            "username": "darius.germano@rumbleentertainment.com"
        },
        "screenname": "Player25cc11b",
        "token": "eyJhbG....w63jbzw",
        "id": "6375681659c472bca7dabc40"
    }
}
```

### Login from Web Applications

Unlike a device login, it is impossible for a web application to know the `deviceInfo` for the above request.  Consequently,
web applications are **unable to create new accounts.**  They can only access accounts that have been linked from within the
game client.

For a web login, **do not** include `deviceInfo` in your request.  Otherwise, the request is still the same as above.

```
POST /dmz/player/account/login
{
    "sso": {
        "appleToken": "eyJhb....ABSsQ",
        "googleToken": "eyJhb....ABSsQ",
        "rumble": {
            "username": "atakechi",
            "hash": "deadbeefdeadbeefdeadbeefdeadbeef"
        }
    }
}
```

## Anonymous Accounts

To use an anonymous account, simply omit the `sso` field, or don't include any information inside it.  This means the account will be tied _only_ to the current device.

However, if the device has been previously linked to an account, you will still see account information come back in the response.

#### Important Note for Web Applications

The `/salt` endpoint on the player-service side requires a token for security.  Game clients have a token already, but a web client doesn't.  However, DMZ will authenticate the request with its own admin token.  Use the DMZ version of the endpoint: `GET /dmz/player/account/salt?username=atakechi`.

## Device Security (InstallId)

Prior to 2023.05.09, if you knew an `installId`, you could launch into _any_ account, regardless of SSO / other accounts a player record was tied to.  While the `installId` is a 32-character GUID hex string, it was theoretically possible to brute-force your way into someone else's account - and attack the database with bloat at the same time.

To combat the account access problem, player-service supports a **private key** as a secondary access to an account.  This requires client-side cooperation to lock the account down.

### Mini-Glossary

| Term                  | Definition                                                                                                                      |
|:----------------------|:--------------------------------------------------------------------------------------------------------------------------------|
| Confirmed Private Key | A key that is never exposed in any endpoint.  Acts as a secondary account access string.                                        |
| Private Key           | A value provided by the client that's checked against the confirmed private key.  If it does not yet exist, it will be created. |
| Client Device         | The local installed game client or a web application.                                                                           |
| Stored Device         | The device record that exists on the database.                                                                                  |

How it works:

1. A request comes in to `/login` with a client device.
2. player-service looks up the device `installId`.
3. If the stored device (from the database) has a **confirmed private key**:
   1. The client device has to also supply the same key in the field `device.privateKey`, or
   2. The client device information must match perfectly with no private key specified (language, client version, data version, etc)
4. Else if the stored device does not have a **confirmed private key**, and the client device has provided one, the stored device will update its **confirmed private key** to match.

When the stored device lacks a confirmed private key, Platform will suggest (but not assign) one in the login response.  The suggested key is a hash calculated from the device's current information.  The client device may choose to store this value and use it in future requests or it can provide its own.  If providing your own value, it's encouraged to use some randomness, since a hacked client would be able to see the way you generate the private key.

Note that if the device information changes in any way, has been locked down with a private key, and the private key is lost, the device will be inaccessible with the same `installId`.

Private keys are also encrypted before they're stored in our database, so even in the event of a database breach, accounts remain secured.  Consequently, the value sent for the private key won't match if you're looking at the record in MongoDB.

This is ultimately an optional security step.  It is not strictly necessary, and the service will operate as it has in the past without it, but adding a second string to validate against is helpful in preventing brute force attacks.  It may be required at a future date.

When an account fails authentication via the private key, a `deviceMismatch` login diagnosis is returned.

#### Important: Once confirmed, a private key cannot change!

## Logging in as a Player (Admin feature)

Occasionally it's sometimes necessary to access a player's account.  For security reasons, this should be an extremely rare use case, but especially for developers who need to hit endpoints to see the data a player is receiving, it can be a really important tool.

Previously, when devices were only secured by an `installId`, you could hit `/login` with it and get the player's token.  Once devices have a confirmed private key, however, this approach will not work.  Instead, the proper route to go through is to use Portal.  Refer to Portal's documentation for more information on how to do this.

## Create Account

To add SSO capabilities to any account, each SSO type requires a separate call.  If you want to link a Google, Apple, _and_ a Rumble account, you will need to hit three separate endpoints:

```
PATCH /player/v2/account/apple
{
    "deviceInfo": { ... },
    "appleToken": "eyJhb....ABSsQ"
}
PATCH /player/v2/account/google
{
    "deviceInfo": { ... },
    "googleToken": "eyJhb....ABSsQ"
}
PATCH /player/v2/account/rumble
{
     "rumble": {
        "email": "austin.takechi@rumbleentertainment.com",
        "username": "atakechi",
        "hash": "deadbeefdeadbeefdeadbeefdeadbeef"
    }
}
```

For the first two, no further steps will be required.  The accounts are already verified by Apple / Google security, so those tokens can then be immediately used for login.  However, in the case of the Rumble account, a code will be emailed to the player that they then have to enter.  This confirms that the player has access to the email address they provided.  This can be done by clicking a button / URL in their inbox, which translates to the following call:

```
GET /player/v2/account/confirm?id={...}&code={...}
```

As soon as this call is completed successfully, the account can be used to log in.  This link is only valid for a brief time.

### Status Codes

Rumble accounts have the following states:

| Status            | Description                                                                                                                                                                                 |
|:------------------|:--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| None              | This should be an impossible state, or otherwise has not had a use case defined.                                                                                                            |
| NeedsConfirmation | A Rumble account has been added, but in order to be used, the player must confirm they own the account via a link sent to the appropriate email address.                                    |
| Confirmed         | The player has confirmed that they own the account by clicking on a link in their email address.                                                                                            |
| ResetRequested    | The player has forgotten their password and asked for the a password reset.  This means a code has been sent to their email address and the account is waiting for that code to be entered. |
| ResetPrimed       | The player has entered the code from the reset and the account will accept any new (non-empty) hash sent to `PATCH /password`.                                                              |

These statuses are held in a `[Flags]` enum.  The "reset" states also function as a confirmed state, so if the player decides to keep trying their password, they are not prevented from using their account.

## Change Password

Changing passwords is straightforward when we know the players old hash; it's a simple lookup and modification.  It's worth noting, though, that the old hash is actually optional; when a player has forgotten their password, they have a flow available to them that primes their account for a password change.

After confirming and entering a 2FA code (covered later), the player can then reset their password without their old hash.

```
PATCH /player/v2/account/password
{
    "username": "atakechi",
    "oldHash": "deadbeefdeadbeefdeadbeefdeadbeef2",       // Optional
    "newHash": "deadbeefdeadbeefdeadbeefdeadbeef3"
}

// Sample response
{
    "success": true,
    "player": {
        "lastLogin": 1667324181,
        "screenname": "Player14b1cf8",
        "deviceInfo": {
            "clientVersion": "0.1.432",
            "dataVersion": null,
            "installId": "locust-postman5",
            "language": "English",
            "osVersion": "macOS 11.6",
            "type": "Postman"
        },
        "googleAccount": {
            "id": "100816465484097383603",
            "email": "william.maynard@rumbleentertainment.com",
            "emailVerified": true,
            "hostedDomain": "rumbleentertainment.com",
            "name": "Will Maynard",
            "picture": "https://lh3.googleusercontent.com/a/ALm5wu3Ry2eu_DYsjGEdvK4StRS6rz3VueNq2E6_KuPt=s96-c"
        },
        "appleAccount": null,
        "rumbleAccount": {
            "email": "austin.takechi@rumbleentertainment.com",
            "username": "atakechi",
            "hash": "deadbeefdeadbeefdeadbeefdeadbeef2",
            "expiration": 0,
            "code": null,
            "status": 2
        },
        "token": "eyJhb...fsh8Vw",
        "id": "6360534d7dc2a4be0ee6ade9"
    }
}
```

## Reset Password

When a player has forgotten their password, we have the following flow defined:

1. Player enters their email address they used to sign up
```
PATCH /player/v2/account/recover
{
    "email": "austin.takechi@rumbleentertainment.com"
}
```
3. A 6-digit code is sent to their email, valid for 15 minutes
4. The player enters their 6-digit code in the client
```
PATCH player/v2/account/reset
{
    "username": "atakechi",
    "code": "622155"
}
```
5. The player enters a new password in the client
```
PATCH /player/v2/account/password
{
    "username": "atakechi",
    "newHash": "deadbeefdeadbeefdeadbeefdeadbeef"
}
```
6. The player is logged in

The player's token will be returned in that final endpoint, so there's no need to call `/login` with the new password. 

## Account Conflicts

When a player has multiple accounts and tries to log in, platform will respond with a 400-level HTTP code.  Take the following scenario:

* A player has a GPG account on mobile with 60 hours of play time. 
* The player has a separate Rumble account on desktop with 35 hours of play time.
* The player signs into their Rumble account on my phone and gets an account conflict.
* The player decides to keep their GPG account.
* The player logs in again on desktop with their Rumble account; they now have their GPG progress.

It's up to the design team to build a flow that makes sense for what information should be displayed to make an informed decision.  Platform is agnostic to player data, but provides all the data necessary to access multiple accounts from the same device, as necessary.

First, assume the following records exist on MongoDB:

```
// Mobile account hooked up to GPG, has 60 hours of play time.
{
    "lastLogin": 1667250037,
    "linkCode": "09c0da60-9a22-491a-bdc7-1dd729a5ce6f",
    "screenname": "Player71855ab",
    "deviceInfo": {
        "clientVersion": "0.1.432",
        "dataVersion": null,
        "installId": "locust-postman",
        "language": "English",
        "osVersion": "Android 13.1",
        "type": "Pixel 7"
    },
    "googleAccount": {
        "id": "100816465484097383603",
        "email": "austin.takechi@rumbleentertainment.com",
        "emailVerified": true,
        "hostedDomain": "rumbleentertainment.com",
        "name": "Austin Takechi",
        "picture": "https://lh3.googleusercontent.com/a/ALm5wu3Ry2eu_DYsjGEdvK4StRS6rz3VueNq2E6_KuPt=s96-c"
    }
    "appleAccount": null,
    "rumbleAccount": null,
    "token": null,
    "id": "635c64af7dc2a4be0ee65a1c"
}

// Desktop account hooked up through Rumble account has 35 hours of play time.
{
    "lastLogin": 1667250037,
    "linkCode": "09c0da60-9a22-491a-bdc7-1dd729a5ce6f",
    "screenname": "Player71855ab",
    "deviceInfo": {
        "clientVersion": "0.1.432",
        "dataVersion": null,
        "installId": "locust-postman2",
        "language": "English",
        "osVersion": "macOS 11.6",
        "type": "Mac Desktop"
    },
    "googleAccount": null,
    "appleAccount": null,
    "rumbleAccount": {
        "email": "austin.takechi@rumblentertainment.com",
        "username": "atakechi",
        "hash": "deadbeefdeadbeefdeadbeefdeadbeef",
        "expiration": 0,
        "code": null,
        "status": 10
    },
    "token": null,
    "id": "635c64af7dc2a4be0ee65a1c"
}
```

Then we have a login request on the mobile phone:

```
POST /player/v2/account/login
{
     "deviceInfo": {
        "clientVersion": "0.1.432",
        "dataVersion": null,
        "installId": "locust-postman",
        "language": "English",
        "osVersion": "Android 13.1",
        "type": "Pixel 7"
    },
    "sso": {
        "rumble": {
            "username": "atakechi",
            "hash": "deadbeefdeadbeefdeadbeefdeadbeef"
        }
    }
}

Sample response:
{
    "errorCode": "accountConflict",
    "player": {
        "lastLogin": 1667327415,
        "linkCode": "b7d309a5-e526-4cf0-b2af-5ec04708230b",
        "screenname": "Player71855ab",
        "deviceInfo": {
            "clientVersion": "0.1.432",
            "dataVersion": null,
            "installId": "locust-postman",
            "language": "English",
            "osVersion": "Android 13.1",
            "type": "Pixel 7"
        },
        "googleAccount": {
            "id": "100816465484097383603",
            "email": "austin.takechi@rumbleentertainment.com",
            "emailVerified": true,
            "hostedDomain": "rumbleentertainment.com",
            "name": "Austin Takechi",
            "picture": "https://lh3.googleusercontent.com/a/ALm5wu3Ry2eu_DYsjGEdvK4StRS6rz3VueNq2E6_KuPt=s96-c"
        },
        "token": "eyJhb...Bzn7Q",
        "discriminator": 9488,
        "id": "6361591a7dc2a4be0ee6c699"
    },
    "conflicts": [
        {
            "lastLogin": 1667324181,
            "linkCode": "b7d309a5-e526-4cf0-b2af-5ec04708230b",
            "screenname": "Player71855ab",
            "deviceInfo": {
                "clientVersion": "0.1.432",
                "dataVersion": null,
                "installId": "locust-postman2",
                "language": "English",
                "osVersion": "macOS 11.6",
                "type": "Mac Desktop"
            },
            "rumbleAccount": {
                "email": "austin.takechi@rumblentertainment.com",
                "username": "atakechi",
                "hash": "deadbeefdeadbeefdeadbeefdeadbeef",
                "expiration": 0,
                "code": null,
                "status": 2
            },
            "token": "eyJhb...m66Nw",
            "discriminator": 1775,
            "id": "6360534d7dc2a4be0ee6ade9"
        }
    ],
    "transferToken": "972ec69f-caa3-4466-a4f9-2889f9286e14"
}
```

Here we see the second account - the desktop client - come through as a conflict.  This is because the request coming from the Pixel 7 contained the Rumble account login for the desktop, but the device information (installId) doesn't match.

When this happens, Platform returns a valid token for each account.  This gives the client options:

1. It can choose to log in with one of the tokens and ignore the conflict.
2. It can pass the tokens to the game server perform some sort of merge behavior.
3. It can hit the endpoint `/player/v2/account/adopt` with one of the tokens.  Doing this will resolve the account conflict.  The token used is made the parent account of all related accounts (other devices used).  No data is deleted, but child accounts do not generate tokens, and thus the data is only accessible internally at Rumble and not to the player.

## Adoption (Conflict Resolution)

When a conflict occurs, Platform updates all related player accounts with a "link code" (a GUID) and an expiration for said code.  This primes accounts for a takeover.  If any one of these accounts uses its token to hit the right endpoint, all accounts flagged with the link code will become **child accounts**.

```
// requires token as auth
PATCH /player/v2/account/adopt
{
}
```

Simple and no-fuss.  Once this endpoint is hit, the player will not have access to any other account than the one the token represents, so be mindful that this is, barring CS intervention, final.  There's no need to login again; the token can then just be used.

There are use cases when it may be preferred to automatically resolve account conflicts.  For example:

1. I play on a Pixel 4 with my GPG account.
2. I upgrade to a Pixel 7 and log in with GPG.
   * There is no progress on device.  This is a fresh install.
3. While player-service will return an account conflict, automatically resolving it by using the conflict token with `/adopt` will save me from several screens and nuisance.

## Account Links

Every single client installation will create its own record in the `players` collection.  When you pair these installations with any kind of SSO, the account that has the SSO account attached to it becomes a **parent account**.  Future devices that are linked through SSO then become **child accounts**.

player-service determines the parent account in the field `player.link`.  The parent account will not have this value, but the children will.

When you hit `/adopt`, the account used becomes the parent (if it wasn't already).  All related accounts become children.

## Handling Banned Emails

On nonprod environments, email domains must be whitelisted to be valid.  In prod, all domains are valid, but DMZ will ban addresses that cause bounces; this is a necessary protection to maintain good standing with email providers.

Of particular mention is during our account creation.  The game client has a loop that executes, checking on the `/account/status`, waiting for the Rumble Account status to change from an unconfirmed state.  If this state corresponds to `RumbleAccount.AccountStatus.EmailInvalid`, the client will know that _no email will ever arrive_, and should let the player know that something went wrong, and future attempts will also fail.

Example response:

```
GET /account/status
{
    "player": {
        ...
        "rumbleAccount": {
            "associatedAccounts": [],
            "expiration": 1682981510,
            "email": "william.maynard@blooper.com",
            "emailBanned": true,                       <---
            "status": 32,                              <---
            "username": "william.maynard@blooper.com"
        },
        "screenname": "Player8b69ee6",
        "token": null,
        "id": "64503ee51d4a77b3227e8182"
    }
}
```

Banned emails are a much larger topic; for more information, refer to [DMZ's documentation](https://gitlab.cdrentertainment.com/platform-services/dmz-service/-/blob/main/EMAIL_BOUNCE_PREVENTION.md).

## The Login Diagnosis

In order to improve client handling and communication, all login-adjacent endpoints will return a `LoginDiagnosis` when a request fails.  These necessarily will return a 400 error code, but they contain a helpful model with boolean values to indicate exactly what's wrong.

The example below should be mostly self-evident of what was wrong with the request that generated it:

```
HTTP 400
{
    "loginDiagnosis": {
        "accountLocked": true,
        "emailNotLinked": false,
        "emailNotConfirmed": false,
        "emailCodeExpired": false,
        "emailInUse": false,
        "emailInvalid": false,
        "passwordInvalid": false,
        "codeInvalid": false,
        "duplicateAccount": false,
        "deviceMismatch": false,
        "maintenance": false,
        "other": false,
        "message": "This account is locked.  Try again later.",
        "code": 1112,
        "stackTrace": "   at PlayerService.Services.LockoutService.EnsureNotLockedOut(String email, String ip) in /Users/Will/Dev/NET/Platform/player-service/Services/LockoutService.cs:line 42\n   at PlayerService.Services.PlayerAccountService.FromSso(SsoData sso, String ipAddress) in /Users/Will/Dev/NET/Platform/player-service/Services/PlayerAccountService.cs:line 169\n   at PlayerService.Controllers.AccountController.Login() in /Users/Will/Dev/NET/Platform/player-service/Controllers/AccountController.cs:line 557",
        "data": {
            "secondsRemaining": 294
        }
    }
}
```

**Important Notes**
* `stackTrace` will only show up in nonprod environments
* `data` is a generic JSON object and can be used to attach any relevant information.  It's up to the client whether or not to use this.  At the time of this writing, it's only used for account lockouts.
* `other` indicates an unknown or otherwise unhandled exception occurred; these need to be addressed.