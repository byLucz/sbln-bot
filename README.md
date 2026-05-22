# sbln-bot
[![wakatime](https://wakatime.com/badge/user/018b8006-2512-45db-8277-d8e3339a3084/project/637bbc99-c13f-4c8f-b0c1-8a25356d22c9.svg)](https://wakatime.com/badge/user/018b8006-2512-45db-8277-d8e3339a3084/project/637bbc99-c13f-4c8f-b0c1-8a25356d22c9)
![Static Badge](https://img.shields.io/badge/build-passing-brightgreen)

sbln-bot is a modular Discord bot built on .NET 8 that blends music playback, Twitch stream notifications, games, meme commands, administration, utility tools & deliver a lot of funny stuff 😋

---

# Features 🏃
### Core Bot Infrastructure
* Command & Interaction Handling: Centralized dispatch for prefix and slash commands.
* Logging & Diagnostics: Color‑coded console logging with severity tags.
* Welcome & Reminder Services: Automatic greetings and DM reminders.

### Music
* Queue, play, skip, pause, resume, and control volume with Lavalink.
* Automatic track announcements and error embeds.

### Twitch Integration
* Live stream monitoring and channel notifications (TwitchService/StreamMonoService.cs).
* Admin commands to add/remove monitored streamers (TwitchService/TwitchCommands.cs).

### Fun & Games
* Meme actions (hug, kiss, pat, etc.), cats, jokes, calculator, minesweeper, emoji races.
* GVR Markov‑chain chat generator with adjustable parameters.

### Utilities
* Weather: OpenWeather lookup with translated output.
* Crypto Prices: Bitfinex stats for BTC, ETH, SOL, TON.
* General Commands: Avatars, reminders, status changes, uptime, help pages, and admin tools.

### and more...
  
---

## Dependencies 📦

Stack: `.NET 8, MariaDB 11, Lavalink 4`

| Nuggets  |
| ----------------|
| Discord.Net     | 
| Discord.Addons.Interactive| 
| Victoria        | 
| TwitchLib       | 
| MySqlConnector  |
| Newtonsoft.Json |

---

## Usage 📝
Feel free to use sbln`ok for your moves
