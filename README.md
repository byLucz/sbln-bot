# sbln-bot
[![wakatime](https://wakatime.com/badge/user/018b8006-2512-45db-8277-d8e3339a3084/project/637bbc99-c13f-4c8f-b0c1-8a25356d22c9.svg)](https://wakatime.com/badge/user/018b8006-2512-45db-8277-d8e3339a3084/project/637bbc99-c13f-4c8f-b0c1-8a25356d22c9)
![Static Badge](https://img.shields.io/badge/build-passing-brightgreen)

sbln-bot is a modular Discord bot built on .NET 10 that blends music playback, Twitch stream notifications, a Telegram bridge, games, meme commands, administration, utility tools & deliver a lot of funny stuff 😋

---

# Features 🏃
### Core Bot Infrastructure
* Command & Interaction Handling: Centralized dispatch for prefix commands, slash commands, buttons and modals with unified error reporting.
* Logging & Diagnostics: Color‑coded console and daily file logs with severity tags, configurable log level and retention.
* Server Settings Panel: Superuser role, welcome channel/role/text and stream channel configured via buttons.
* Welcome & Reminder Services: Automatic greetings with auto-role and DM reminders.
* Internal Mail: Anonymous or regular DM letters with reply routing.

### Music (Audio8)
* Queue, play, skip, pause, resume, seek, loop, shuffle and volume with Lavalink4NET.
* Bass boost, filters (nightcore, slowed, 8D, karaoke, vibrato), TTS and voice votes.
* Smart search across YouTube/SoundCloud/Spotify with alternatives, recent playlists, queue picking.
* Now-playing panel with controls and player state restore after restart.

### Twitch Integration
* Live stream monitoring and channel notifications (TwitchService/StreamMonoService.cs).
* Admin commands to add/remove monitored streamers (TwitchService/TwitchCommands.cs).

### Telegram Bridge
* Selected commands work in Telegram chats via [DTF](https://github.com/byLucz/DTF), with text or image rendering of embeds.

### Fun & Games
* Meme actions (hug, kiss, pat, etc.), cats, jokes, recipes, minesweeper, emoji races, Apex sets.
* GVR Markov‑chain chat generator with adjustable parameters.
* Book Club: weekly book picks, ratings, seasons and member stats.

### Utilities
* Weather: OpenWeather current, 24h and 5-day forecast.
* Crypto Prices: Bitfinex stats for BTC, ETH, SOL, TON.
* Currency: CBR exchange rates.
* PPM: temporary and permanent mailboxes with inbox viewer.
* pgAPI: control panel for the pg container (health-check, logs, restarts).
* General Commands: Avatars, calculator, reminders, status changes, uptime, ping, versions, help pages, and admin tools.

### and more...
  
---

## Dependencies 📦

Stack: `.NET 10, MariaDB 11, Lavalink 4`

| Nuggets  |
| ----------------|
| Discord.Net     | 
| Lavalink4NET    | 
| TwitchLib.Api   | 
| MySqlConnector  |
| MailKit         |
| NAudio / NLayer |
| Telegram.Bot (via DTF) |

---

## Usage 📝
Feel free to use sbln`ok for your moves
