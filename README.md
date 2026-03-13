# AvyInReach

AvyInReach is a Windows-first CLI for turning avalanche forecasts into compact Garmin inReach-friendly messages and delivering them automatically.

## Purpose

The project exists to:

- fetch avalanche forecasts for a chosen region
- compress them into a terse one-line field report
- send them to normal email recipients or Garmin inReach recipients
- avoid noisy duplicate updates when forecast inputs have not changed
- automate delivery on a schedule during a trip window

## Features

- Avalanche Canada and NWAC forecast support
- GitHub Copilot CLI summarization into a compact ASCII line
- SMTP delivery for normal recipients
- Garmin inReach web-reply delivery for `@inreach.garmin.com` recipients
- Garmin multipart message splitting with configurable per-recipient part limits
- recipient-configured summary budgets for `preview`, `send`, and `update`
- `update` deduplication using separate input and output fingerprints
- configurable rolling 24-hour report cap for `update`
- Windows Task Scheduler integration for recurring updates
- local JSON configuration in `%LOCALAPPDATA%\AvyInReach\`

## Main commands

```powershell
.\AvyInReach.exe smtp server <host:port> from <address>
.\AvyInReach.exe copilot model [model-id]
.\AvyInReach.exe delivery reports <count>
.\AvyInReach.exe recipient configure <address> transport <email|sms|inreach> [summary <count>]
.\AvyInReach.exe garmin link <inreach> <reply-url> [messages <count>]
.\AvyInReach.exe regions avalanche-canada
.\AvyInReach.exe regions nwac
.\AvyInReach.exe preview <recipient> avalanche-canada <region>
.\AvyInReach.exe preview <recipient> nwac <region>
.\AvyInReach.exe send <recipient> avalanche-canada <region>
.\AvyInReach.exe update <recipient> avalanche-canada <region>
.\AvyInReach.exe schedule <start> <end> <recipient> avalanche-canada <region>
```

## Notes

- supported providers are `avalanche-canada` and `nwac`
- Copilot prompt execution defaults to `gpt-5-mini` and can be changed with `copilot model <model-id>`
- `send` always sends immediately
- `preview`, `send`, and `update` size summaries from recipient configuration
- summaries prioritize decision-driving notices and always include sun/cloud, wind, and low/high temperature in `WX`
- `update` sends only when forecast inputs and generated output require it
- scheduled tasks prompt for Task Scheduler credentials at install time

## About this repository

This project is completely written by GitHub Copilot CLI.

For more detail, see `docs\configuration.md` and `docs\overview.md`.
