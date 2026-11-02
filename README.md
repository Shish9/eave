<p align="center"><img src="docs/logo.png" width="180" alt="Eave logo"></p>

# Eave

A dynamic island for Windows. A small notch at the top of your screen that shows what
[Claude Code](https://docs.claude.com/en/docs/claude-code) is doing, lets you answer its permission
prompts with one click, and keeps music, downloads, weather and more a glance away.

> Eave is an independent open-source project. It is not affiliated with, endorsed by, or sponsored by
> Anthropic. Claude and Claude Code are trademarks of Anthropic.

## Features

**Claude Code**
- Live status of every running session: project, current tool, elapsed time, tool count, context size
- Allow, Always allow or Deny permission prompts from the island, with a preview of the lines an edit would change
- A sound and notification when Claude finishes or needs you; a warning when a session looks stuck
- Click a session to jump to its terminal

**Widgets**
- Now playing: album art, controls, progress, and a waveform that follows the music
- Downloads in progress (from your Downloads folder) with speed, and a notice when they finish
- Weather, next meeting (from a calendar link), clipboard history, quick-launch apps, timers
- Volume, battery and "app closed" notices; scroll on the island to change volume

**Make it yours (Eave Studio)**
- Notch or floating pill, size, position, opacity
- Themes: Dark, Midnight, Graphite, Clay, Light, or your own colours
- Glow that follows Claude's state, your album art, or a fixed colour, with strength and spread
- Turn every part on or off

## Install

1. Download `Eave-Setup-….exe` from [Releases](../../releases) and run it. It installs for your Windows account only,
   so it doesn't ask for administrator rights.
2. Choose what you want in the setup window:
   - **Start Eave when Windows starts**
   - **Connect to Claude Code**: adds hooks to `~/.claude/settings.json` so Claude Code reports to Eave
     (a timestamped backup is saved first)
   - **Desktop shortcut**
3. Finish, and start a new Claude Code session.

Windows may say "Windows protected your PC" while releases are not code-signed (see
[Code signing policy](#code-signing-policy)): click **More info**, then **Run anyway**.

Eave is installed to `%LOCALAPPDATA%\Eave`, with Start menu entries ("Eave" and "Eave Studio") and one entry in
Settings → Apps.

- **Update:** run the newer setup. Your settings are kept.
- **Uninstall:** Settings → Apps → Eave. This removes the app, the startup entry, the shortcuts and the Claude Code
  hooks. Your Eave settings in `%APPDATA%\Eave` are kept.

**No installer?** `Eave-…-portable.zip` holds `Eave.exe` and `relay.exe`. Unzip and run `Eave.exe --install`; it lists
what it is about to change and asks you to confirm (copy itself to `%LOCALAPPDATA%\Eave`, start with Windows, add the
Start menu entries and the Claude Code hooks). Remove it with `Eave.exe --uninstall`.

## Privacy

Eave runs locally. It listens only on `127.0.0.1:47821` for events from Claude Code on the same PC.
It does not collect analytics or send your data anywhere. It only contacts the internet for features
you turn on yourself:

| Feature | Contacts | What is sent |
|---|---|---|
| Weather (off until you set a city) | open-meteo.com | the city name, then its coordinates |
| Next meeting (off until you add a link) | the calendar link you provide | a normal download of that link |

Clipboard history stays in memory and is never saved; copies marked private by password managers are skipped.
The downloads widget only reads file names and sizes in your Downloads folder.

## Build from source

Requires the .NET 8 SDK.

```
powershell -ExecutionPolicy Bypass -File build.ps1
```

The output is in `dist\`: `Eave.exe`, `relay.exe` and, if [Inno Setup 6](https://jrsoftware.org/isdl.php) is installed,
the installer `Eave-Setup-<version>.exe` (built from `installer\Eave.iss`). On PCs with Smart App Control turned on, unsigned builds you make yourself
are blocked from running; use a signed release instead.

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

Release builds are produced by GitHub Actions from this repository (`.github/workflows/release.yml`)
and, once the SignPath Foundation application is approved, signed through SignPath. Until then releases are
not signed, and their title says "(unsigned)". Only builds from tagged versions in this repository are signed.

- Committers and reviewers: [Shish9](https://github.com/Shish9)
- Approvers: [Shish9](https://github.com/Shish9)

Every release is built from a tagged version of this repository, and each signing request is approved manually.

### Privacy policy

This program will not transfer any information to other networked systems unless specifically
requested by the user or the person installing or operating it (see [Privacy](#privacy)).

## License

[MIT](LICENSE)
