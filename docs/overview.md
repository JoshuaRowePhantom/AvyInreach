# AvyInReach overview

`AvyInReach` is a Windows-first CLI that reads avalanche forecasts, asks GitHub Copilot CLI to compress the forecast into an InReach-friendly one-line summary, and sends that summary either by SMTP email or, for Garmin InReach recipients, through Garmin's public reply flow.

Maintainer: Josh Rowe

Supported providers are `avalanche-canada` and `nwac`.

## Setup order

1. Configure SMTP for normal outbound mail.
2. Configure the `update` delivery cap if you want something other than the default of `1`.
3. If you send to `@inreach.garmin.com`, configure a Garmin reply link for that recipient.
4. If you want automation, install a schedule.

## Commands

`help`

`copilot model [model-id]`

`smtp server <host:port> from <address>`

`delivery reports <count>`

`recipient configure <address> transport <email|sms|inreach> [summary <count>]`

`garmin link <inreach> <reply-url>`

`garmin link <inreach> <reply-url> [messages <count>]`

`regions [provider]`

`preview <recipient> <provider> <region>`

`send <recipient> <provider> <region>`

`update <recipient> <provider> <region>`

`schedule <start> <end> <recipient> <provider> <region>`

`schedules`

`unschedule <id>`

## Scheduling

`schedule` creates a Windows Task Scheduler task that runs every 15 minutes between the requested start and end dates.

It fails fast if the recipient is missing required recipient configuration, and Garmin recipients must already have a configured Garmin reply link.

The app installs the task with `schtasks.exe` and configures the task XML so the task expires and self-deletes after the date range closes.

Each schedule also writes its latest stdout/stderr to a per-schedule log file, and `schedule log <id>` prints that last-run log.

## How `update` decides to send

`update` does not compare source timestamps or issue dates.

It first computes a fingerprint from the forecast inputs stored on disk for the `(inreach, provider, region)` tuple.

If the forecast-input fingerprint is unchanged, it does not generate or send a new report.

If the inputs changed, it generates the outbound summary text and compares a separate output fingerprint for that generated summary.

If the generated-summary fingerprint changed, it sends. If the generated-summary fingerprint is identical, it does not.

`preview` uses the same fetch and Copilot summarization path as sending, but prints the generated line to stdout instead of sending mail. It uses the configured recipient settings for sizing.

`update` also enforces a per-recipient rolling 6-hour report cap. The default is `1`, and one Garmin multipart delivery still counts as one report. Manual `send` bypasses that cap.

## Garmin delivery

Garmin recipients use a configured reply link from an incoming inReach message rather than normal SMTP delivery.

Garmin replies may be split into multiple 160-character parts, but one logical report still counts as one `update` send for the daily cap.

## Summary shape

The Copilot prompt asks for a compact ASCII one-line forecast with:

- danger as `below/treeline/alpine`
- up to two avalanche problems with presence, size, and aspects
- weather that always includes sun/cloud, wind, and low/high temperature
- a very brief decision-driving notice when recent avalanche activity, serious hazards, or weak-layer concerns matter
- the exact suffix `valid to M/d HH:mmTZ`

## Copilot CLI integration

The app shells out to:

```powershell
copilot -p "<prompt>" --model "<configured-model>" --allow-all --silent --output-format text --no-color
```

The default model is `gpt-5-mini`, and `copilot model <model-id>` stores an override in local app config. The local machine must already have a working `copilot` executable and login session.

## State on disk

State is stored in `%LOCALAPPDATA%\AvyInReach\`.

Files:

- `smtp.json` stores SMTP server and sender configuration
- `delivery.json` stores the rolling 6-hour report cap
- `copilot.json` stores the configured Copilot model id
- `recipients.json` stores recipient transport and summary budget configuration
- `garmin.json` stores Garmin reply links by recipient address
- `delivery-state.json` stores the last sent summary plus retry/error notification state
- `schedules.json` stores installed schedule metadata and task names
