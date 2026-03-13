# AvyInReach configuration

## Requirements

- Windows
- `.NET 10`
- GitHub Copilot CLI installed and already authenticated
- SMTP access that can send mail to the target InReach address

## SMTP environment variables

Required:

- `AVYINREACH_SMTP_HOST`
- `AVYINREACH_SMTP_PORT`
- `AVYINREACH_SMTP_FROM`
- `AVYINREACH_SMTP_ENABLE_SSL`

Optional:

- `AVYINREACH_SMTP_USERNAME`
- `AVYINREACH_SMTP_PASSWORD`

Example PowerShell setup:

```powershell
$env:AVYINREACH_SMTP_HOST = "smtp.example.com"
$env:AVYINREACH_SMTP_PORT = "587"
$env:AVYINREACH_SMTP_FROM = "alerts@example.com"
$env:AVYINREACH_SMTP_ENABLE_SSL = "true"
$env:AVYINREACH_SMTP_USERNAME = "alerts@example.com"
$env:AVYINREACH_SMTP_PASSWORD = "your-password"
```

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
