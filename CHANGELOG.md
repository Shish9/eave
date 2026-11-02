# Changelog

What changed in each version of Eave. Versions match the tags and releases on GitHub.

## 0.3.3 (not released yet)

### Added
- **An installer**, `Eave-Setup-<version>.exe`. It installs for your Windows account only (no administrator
  prompt), lets you choose to start with Windows, connect to Claude Code and add a desktop shortcut, lists Eave once
  in Settings → Apps, and removes the app and the Claude Code hooks when you uninstall (your settings are kept).
  The zip is still available as a portable download.
- **Releases publish themselves** when a version tag is pushed, with the installer and the portable zip attached.
  Until the SignPath application is approved, releases say "(unsigned)". A release is refused if the tag and the
  version in the program files don't match.
- A build check on every pull request, and tests for the settings and the Claude Code hook installer
  (`dotnet test tests/Eave.Tests`).

### Fixed
- If Eave Studio can't open, Eave says so and writes the details to `%APPDATA%\Eave\crash.log`, instead of showing
  Windows' raw error box.
- Two hook installs in the same millisecond made the second backup of Claude Code's `settings.json` collide and fail.
- Only the newest five `settings.json.bak-…` backups are kept; backups you made yourself are never touched.

### Security
- Eave's local port now needs a secret that only your Windows account can read (`%APPDATA%\Eave\token`), and it
  refuses requests that come from a web page. Before, any program or web page could send it fake Claude events or
  fake permission prompts. **`relay.exe` and `Eave.exe` must be the same version**: the installer replaces both. If
  Claude Code stops showing up after a manual update, replace both files.
- The calendar link, which works like a password, is saved encrypted for your Windows account instead of as plain
  text. Existing links are converted the next time settings are saved.

### Changed
- `settings.json` carries a layout version, so future changes can convert old files.

## 0.3.2 (2026-11-02)

### Fixed
- Weather, notifications and Claude sessions disappearing after an update:
  - A `settings.json` that couldn't be read was replaced by defaults on the next save, losing the weather city and
    switching notifications off. Locked files are retried, damaged ones are kept aside and the last good copy is
    used, and saves are crash-safe.
  - One error from Windows' notification listener switched notifications off for good. It now shows the reason in
    Studio and retries.
  - Claude Code hooks that pointed at a deleted download folder are found and repaired on start (unless you removed
    them on purpose), and Studio → Behavior shows their state. Another tool's `relay.exe` hook is left alone.
- One failing widget can no longer stop the others from updating.
- Weather shows why a lookup failed (city not found, no internet) instead of "Looking up…" forever.

## 0.3.1 (2026-11-01)

### Fixed
- Eave Studio crashed on open ("Specified Visual is already a child of another Visual"): the Theme page added its
  "Custom colours" heading twice.

## 0.3.0 (2026-11-01)

First public release: a dynamic island for Windows that follows Claude Code sessions (status, permission prompts
with a diff preview, done and stuck notices) and shows music, downloads, weather, the next meeting, clipboard
history and quick launch. Eave Studio for shape, size, themes, glow and contents. Renamed from "Claude Island";
installs migrate settings and remove the old copy.
