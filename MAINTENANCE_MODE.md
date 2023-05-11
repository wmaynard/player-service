# Maintenance Mode

Player Service serves as the entry point for all game clients.  Consequently, it is the only service that needs to be taken down for planned maintenance.  Player Service uses Dynamic Config to enable / disable / schedule maintenance.

There are two primary use cases for maintenance mode:

1. Something is critically wrong and our entire backend is working incorrectly or otherwise compromised.  There was no notice and we need to immediately cut access to our game services.
2. We have a planned upgrade or other scheduled task where it's helpful to prevent access to our services for the duration.

## What exactly does Maintenance Mode do?

Maintenance Mode uses a custom Filter to prevent endpoint work.  For more information on filters, [read the MSDN docs.](https://learn.microsoft.com/en-us/aspnet/core/mvc/controllers/filters?view=aspnetcore-7.0)

Not all traffic is blocked for maintenance mode.  `/health` endpoints are exempt because without them, our cluster would continuously reboot our servers.  Admin endpoints are also exempt; there's a strong chance that we'll need to repair player accounts using admin features in an emergency maintenance session. 

## Requirements

* You have access to Portal
* You have permissions to modify Dynamic Config

## Turning on Maintenance Mode

1. Navigate to dynamic config in Portal
2. Navigate to the player-service section
3. In the `maintenance` value field, enter **any part of the environment URL**.

The `environment URL` can be found in your URL bar when viewing portal, underlined below:

```
https://portal.dev.nonprod.tower.cdrentertainment.com/config/player-service
               ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^
```

Player Service uses a partial match here to satisfy the requirement of being able to bring down prod A1 independently from A2, or vice versa.  Since the database is shared between both, we can't simply use a boolean value.

Example values:

| `maintenance` Value                             | Effect                                |
|:------------------------------------------------|:--------------------------------------|
| platform-a1                                     | Brings down A1 only                   |
| cdre                                            | Brings down a nonprod environment     |
| rumblegames                                     | Brings down a prod environment        |
| https://platform-a2.prod.tower.rumblegames.com/ | Brings down A2 only                   |
| e                                               | Brings down virtually any environment |

Ultimately, the core logic for checking for maintenance mode is simply:

```
PlatformEnvironment.Url().Contains(maintenanceValue)
```

Just be mindful of the value you enter.  Once dynamic config has been updated and Player Service's config has recognized the change, it will begin blocking all non-health, non-admin traffic.

## Scheduling Maintenance

This step is optional, but there are two other fields that can be set to schedule maintenance:

* `maintenanceBegins`
* `maintenanceEnds`

These are both **Unix timestamps**.  This is a number indicating the number of seconds that have passed since 1/1/1970.  DateTimes, Timespans, and other formats will not be properly recognized.  To convert a UTC date into a Unix timestamp, you can use [unixtimestamp.com](https://www.unixtimestamp.com/).

* These fields do nothing if the partial `maintenance` URL is unset or otherwise not a match for the environment.
* If `maintenanceBegins` is larger than the current timestamp, Maintenance Mode will not be in effect.
* If `maintenanceEnds` is specified and smaller than the current timestamp, Maintenance Mode will not be in effect.

Examples:

For simplicity, we'll use non-Unix timestamps to demonstrate functionality.  Assume the current time is 1000.

| `Begins` Value | `Ends` Value | Result                                                                                                      | 
|:---------------|:-------------|:------------------------------------------------------------------------------------------------------------|
| 1600           | 2200         | Maintenance starts in 10 minutes and lasts for 10 minutes.                                                  |
| (blank)        | 2200         | Maintenance begins imminently and lasts for 20 minutes.                                                     |
| 1600           | (blank)      | Maintenance begins in 10 minutes and lasts an indefinite amount of time, and must be manually ended.        |
| 1600           | 900          | Nothing.  The end time is earlier than the current time.                                                    |
| 1600           | 1600         | Nothing.  By the time maintenance mode would start, the end will be less than or equal to the current time. |

Only one period of maintenance can be scheduled at a time.

## Whitelisting Accounts to Bypass Maintenance

If you need to let players in during maintenance mode, you can do so via Dynamic Config.  This requires players to have signed up with some form of SSO and is not available to anonymous accounts.

Under the player-service section, use the `maintenanceWhitelist` field.  This is a CSV string.  You can whitelist specific accounts (`joe.mcfugal@gmail.com`) or entire domains (`rumbleentertainment.com`).

As long as a player has signed up with an account that matches the pattern, they will be allowed through.

#### Important: the check that's made is just that the email address ends with whatever the whitelist has in it.  If you specify an entry as simply `.com`, or even `gmail.com`, you are opening maintenance mode to huge numbers of players. 

## Restoring Normal Functionality

There are two ways to end Maintenance Mode:

1. Clear the `maintenance` value.
2. Specify `maintenanceEnds` to be the current timestamp or earlier.

While you technically could set the `Begins` value to a far-off date in the future, this is bad practice; you wouldn't want a situation where maintenance is scheduled for 7 years in the future and no one is aware it's going to trigger.