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
| SSO                        | Single sign-on.  Allows a player to share an account across multiple devices.  May be Apple, Google, or Rumble accounts.                                                                                   |
| Token                      | A JWT representing authorized access to a particular resource.  Tokens provided by third parties must be verified and translated into Rumble tokens; they cannot be used to access our resources directly. |

## Login

First, let's look at what a sample login request looks like.  Every request **must** have a `device` field.  The other field, `sso`, is optional, as are all of its contents.  However, if `sso` accounts are specified here, and those accounts can't be found, the login request **will fail**, and no token will be generated.

```
POST /player/account/login
{
    "device": {
        "installId": "locust-postman",            // Device GUID
        "clientVersion": "0.1.432",
        "language": "English",
        "osVersion": "macOS 11.6",
        "type": "Postman"
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
```

## Anonymous Accounts

To use an anonymous account, simply omit the `sso` field, or don't include any information inside it.  This means the account will be tied _only_ to the current device.

However, if the device has been previously linked to an account, you may still see account information come back in the response.

## Create Account

To add SSO capabilities to any account, each SSO type requires a separate call.  If you want to link a Google, Apple, _and_ a Rumble account, you will need to hit three separate endpoints:

```
PATCH /account/apple
{
    "device": { ... },
    "appleToken": "eyJhb....ABSsQ"
}
PATCH /account/google
{
    "device": { ... },
    "googleToken": "eyJhb....ABSsQ"
}
PATCH /account/rumble
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
GET /player/account/confirm?id={...}&code={...}
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
PATCH /player/account/password
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
        "device": {
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
            "pendingHash": null,
            "codeExpiration": 0,
            "confirmationCode": null,
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
PATCH player/account/recover
{
    "email": "austin.takechi@rumbleentertainment.com"
}
```
3. A 6-digit code is sent to their email, valid for 15 minutes
4. The player enters their 6-digit code in the client
```
PATCH player/account/reset
{
    "username": "atakechi",
    "code": "622155"
}
```
5. The player enters a new password in the client
```
PATCH /player/account/password
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
    "device": {
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
    "device": {
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
        "pendingHash": null,
        "codeExpiration": 0,
        "confirmationCode": null,
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
     "device": {
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
        "device": {
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
            "device": {
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
                "pendingHash": null,
                "codeExpiration": 0,
                "confirmationCode": null,
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