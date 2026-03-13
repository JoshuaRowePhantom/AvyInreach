# AvyInReach configuration

## Requirements

- Windows
- `.NET 10`
- GitHub Copilot CLI installed and already authenticated
- SMTP access for normal email recipients

## SMTP setup

Configure SMTP once with:

```powershell
.\AvyInReach.exe smtp server undead.home.phantom.to:25 from avyinreach@phantom.to
```

This writes `%LOCALAPPDATA%\AvyInReach\smtp.json`.

Default JSON values are:

- `enableSsl: false`
- `useDefaultCredentials: true`

The `smtp` command updates `server` and `fromAddress`. If you need explicit SMTP auth, edit `smtp.json` manually and add `username` and `password`.

The configured `fromAddress` is also reused as the sender identity when posting Garmin replies through the public inReach reply page.

## Garmin InReach setup

If the recipient is an `@inreach.garmin.com` address, configure the Garmin reply link from a real incoming inReach email:

```powershell
.\AvyInReach.exe garmin link somebody@inreach.garmin.com https://inreachlink.com/example
```

Optional max-part override:

```powershell
.\AvyInReach.exe garmin link somebody@inreach.garmin.com https://inreachlink.com/example messages 4
```

This writes `%LOCALAPPDATA%\AvyInReach\garmin.json`.

When sending to Garmin, AvyInReach fetches the configured reply page, extracts Garmin's hidden reply identifiers, and posts the message back through Garmin's web reply flow.

Garmin sends replies in chunks of up to 160 characters each.

By default, AvyInReach allows up to 3 Garmin message parts per send. That value is stored in `garmin.json` per recipient and can be increased with the optional `messages <count>` argument.

## Scheduled task credentials

When you create a schedule, AvyInReach prompts for the Task Scheduler run-as username and password.

Those credentials are passed directly to Task Scheduler for that task registration and are not stored in AvyInReach's local config files.

Example `smtp.json`:

```json
{
  "server": {
    "host": "undead.home.phantom.to",
    "port": 25
  },
  "fromAddress": "avyinreach@phantom.to",
  "enableSsl": false,
  "useDefaultCredentials": true,
  "username": null,
  "password": null
}
```

## Data storage

AvyInReach stores its local files in `%LOCALAPPDATA%\AvyInReach\`.

Files:

- `smtp.json` stores SMTP server and sender configuration
- `garmin.json` stores Garmin reply links by InReach recipient address
- `delivery-state.json` stores the last sent summary plus retry/error notification state
- `schedules.json` stores installed schedule metadata and task names

## Copilot CLI integration

The app shells out to:

```powershell
copilot -p "<prompt>" --allow-all --silent --output-format text --no-color
```

That means the local machine must already have a working `copilot` executable and login session.

## Examples

List regions:

```powershell
.\AvyInReach.exe regions avalanche-canada
```

Send now:

```powershell
.\AvyInReach.exe send somebody@inreach.garmin.com avalanche-canada Glacier
```

Preview the generated summary without sending:

```powershell
.\AvyInReach.exe summary avalanche-canada Glacier
```

Only send when the final summary changes:

```powershell
.\AvyInReach.exe update somebody@inreach.garmin.com avalanche-canada Glacier
```

Install a 15-minute schedule:

```powershell
.\AvyInReach.exe schedule 3/14 3/22 somebody@inreach.garmin.com avalanche-canada Glacier
```

## Retry notifications

If no forecast is available for a scheduled region, the app records that state on disk.

After one hour of repeated checks with no published forecast, it sends a one-time `still retrying` notice.

If the app is extended to route provider fetch errors through the stored error path, the same store format also supports a one-time persistent-error notification after one hour.
