# AvyInReach overview

`AvyInReach` is a Windows-first CLI that reads Avalanche Canada forecasts, asks GitHub Copilot CLI to compress the forecast into an InReach-friendly one-line summary, and sends that summary either by SMTP email or, for Garmin InReach recipients, through Garmin's public reply flow.

Maintainer: Josh Rowe

Phase 1 supports only `avalanche-canada`.

## Commands

`help`

`garmin link <inreach> <reply-url>`

`garmin link <inreach> <reply-url> [messages <count>]`

`smtp server <host:port> from <address>`

`regions [provider]`

`summary <provider> <region>`

`send <inreach> <provider> <region>`

`update <inreach> <provider> <region>`

`schedule <start> <end> <inreach> <provider> <region>`

`schedules`

`unschedule <id>`

## How `update` decides to send

`update` does not compare source timestamps or issue dates.

It generates the final outbound summary text first, then compares that summary to the last summary stored on disk for the `(inreach, provider, region)` tuple.

If the summary text changed, it sends. If the summary text is identical, it does not.

`summary` uses the same fetch and Copilot summarization path, but prints the generated line to stdout instead of sending mail.

## Summary shape

The Copilot prompt asks for a compact ASCII one-line forecast with:

- danger as `below/treeline/alpine`
- up to two avalanche problems with presence, size, and aspects
- terse weather
- a very brief notice when highlights/messages matter
- the exact suffix `valid to M/d HH:mmTZ`

## State on disk

State is stored in `%LOCALAPPDATA%\AvyInReach\`.

Files:

- `smtp.json` stores SMTP server and sender configuration
- `garmin.json` stores Garmin reply links by recipient address
- `delivery-state.json` stores the last sent summary plus retry/error notification state
- `schedules.json` stores installed schedule metadata and task names

## Scheduling

`schedule` creates a Windows Task Scheduler task that runs every 15 minutes between the requested start and end dates.

The app installs the task with `schtasks.exe` and configures the task XML so the task expires and self-deletes after the date range closes.
