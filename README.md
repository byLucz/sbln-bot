# sbln-bot v5X.SUP

Off-release support build of the v5X line. It is outside release versioning: no tags, no changelogs. Important fixes only, new features go to v6 (`master`).

Built from [v5.6.1](https://lois.media/sbln/v5.6.1) (base) and [v6.0.0](https://lois.media/sbln/v6.0.0) (ported fixes, DB schema).

## Stack

.NET 8 LTS (SDK `8.0.426`, runtime `8.0.32`), MariaDB, Lavalink 4, Victoria 7.

## Config

Own file `config-v5x.json`, v6 configs are never read. Example: `config/config-v5x.example.json`.
Path: `SBLN_V5X_CONFIG`, else `/opt/sbln/config-v5x.json`, else `config/config-v5x.json`.
Modules are switched in the `Modules` section.

## Docker

Deployed by the main script from `master`, no own scripts. `docker-compose.yml` uses the v6 variables, pass `config-v5x.json` as `SBLN_CONFIG_FILE`.

## Footprint vs v6

| | v6 | v5X.SUP |
|---|---|---|
| App publish | 81 MB, 75 files | 9.6 MB, 43 files (~8x smaller) |
| Image | ~0.7-1 GB (ffmpeg, tesseract, fonts, docker-cli) | ~130 MB, chiseled, no shell (~5-8x smaller) |
| Idle RAM | ~200-400 MB (Server GC, presences, Telegram, Remna, ONNX) | ~60-100 MB, capped at 256 MB (~3-4x less) |
| Host access | `docker.sock` mounted | none |

Publish size is measured. Image and RAM are estimates, check with `docker images` and `docker stats`.
