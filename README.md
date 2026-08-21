# ED Switcher

![App Screenshot](Assets/screenshot.png)

A modern, fast, and native account switcher for **Elite Dangerous**, built with C#, WinUI 3, and .NET 8.

It acts as a lightweight, Fluent Design GUI front-end for [min-ed-launcher](https://github.com/Rfvgyhn/min-ed-launcher) (using a [custom fork](https://github.com/Gitveu/min-ed-launcher_consfix) to prevent console redirection crashes): managing multiple Frontier account credential files (including first-time sign-in with email 2FA) and spawning the real `MinEdLauncher.exe` to perform the actual game update and launch.

## Features

- **Multi-Account Management**: Easily switch between multiple Elite Dangerous accounts without re-entering passwords or 2FA codes.
- **Modern Windows Native UI**: Built with WinUI 3 for a beautiful, responsive, and native Windows 11/10 experience, complete with Mica/Acrylic transparency.
- **Native 2FA Support**: Handles Frontier's 2FA email verification natively within the UI.
- **MinEdLauncher Integration**: Generates fully compatible `.cred` files with DPAPI encryption so `MinEdLauncher` can read them directly and launch the game silently.
- **Auto Exit**: Optionally closes ED Switcher a couple of seconds after the game has started, so nothing is left in your taskbar.
- **Remembers Your Last Version**: The selected game version (Odyssey / Horizons / Legacy) is saved and pre-selected on the next start — no more switching from Horizons to Odyssey every single time.
- **Accurate Machine Spoofing**: Perfectly replicates the exact F#-based `MachineId` generation algorithm used by `MinEdLauncher`.

## Prerequisites

1. **Elite Dangerous** installed on your system.
2. **[min-ed-launcher (Console Fix Fork)](https://github.com/Gitveu/min-ed-launcher_consfix/releases)**.
   - *Note: You must use this specific fork! The original `min-ed-launcher` crashes when run outside of a standard console (which happens when this GUI app redirects its output).*
   - Place `MinEdLauncher.exe` in your Elite Dangerous install directory, next to `EDLaunch.exe`.

## Launching from Steam (optional)

You can make Steam start ED Switcher instead of Frontier's launcher, so the game still starts from your Steam library, desktop shortcut or Big Picture.

1. Right-click **Elite Dangerous** in your Steam library → **Properties** → **General**
2. Set **Launch Options** to the full path of `ED Switcher.exe` followed by `%command%`:

   ```
   "<Elite Dangerous install dir>\EDSwitch\ED Switcher.exe" %command%
   ```

   Example:

   ```
   "D:\Steam\steamapps\common\Elite Dangerous\EDSwitch\ED Switcher.exe" %command%
   ```

Notes:

- `%command%` is required. Without it Steam treats the whole string as *arguments* for `EDLaunch.exe`, and the official launcher starts instead.
- The path has to be absolute. Steam resolves the executable itself and does not use the game folder as the base for relative paths (`"EDSwitch\ED Switcher.exe" %command%` fails with *The system cannot find the path specified*).
- Don't wrap it in `cmd /c` — since ED Switcher is a GUI app, `cmd` waits for it and the console window stays open for the whole session. If you really want one install-independent line, use `cmd /c start "" "EDSwitch\ED Switcher.exe" %command%`; the trade-off is that Steam marks the game as closed immediately (no in-game status, no overlay, no playtime).
- Whatever `%command%` expands to is ignored by ED Switcher. Account, game version, VR mode and Auto Exit are configured inside the app.

## Settings

Settings are stored in `settings.json` next to the executable:

| Key | Description |
| --- | --- |
| `EdInstallPath` | Elite Dangerous install directory (must contain `EDLaunch.exe`). |
| `LauncherPathBox` | Full path to `MinEdLauncher.exe`. |
| `AppTheme` | `Light`, `Dark` or `Default`. |
| `UiSounds` | Interface click sounds. |
| `HideEmails` | Masks e-mail addresses in the account list (streamer mode). |
| `AutoExit` | Close ED Switcher after the game has started. |
| `AutoExitDelaySeconds` | Delay before quitting, `0`–`60`, default `2`. |
| `PreferredVersion` | Last used product filter (`edo`, `edh4`, `ed`, …), restored on startup. |

## Development / Building from Source

This project is built using C# and the Windows App SDK (WinUI 3).

**Prerequisites:**
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community or higher)
- **.NET 8.0 SDK**
- **Windows App SDK** workload installed in Visual Studio.

**Build Instructions:**
1. Open `EDAccountSwitcher.sln` in Visual Studio 2022.
2. Ensure the startup project is set to `EDAccountSwitcher`.
3. Set the build configuration to `Release` and architecture to `x64`.
4. Build the solution.

## How it works (Technical Details)

This app ports the relevant `min-ed-launcher` internals into C#:

- **Credential files**: Fully compatible with `min-ed-launcher`. Stored in `%LOCALAPPDATA%\min-ed-launcher\.frontier-<profile-lowercased>.cred`. It contains three lines: plaintext email, encrypted password, encrypted machine token.
- **DPAPI Encryption**: Encryption is UTF-16LE → DPAPI with `CryptProtectData` and the salt reflected from `ClientSupport.dll` (found in the ED install dir) → Base64. Because the same salt and Windows Data Protection API (DPAPI) are used, cred files written by this app are perfectly readable by `min-ed-launcher`.
- **Machine ID Algorithm**: Matches `min-ed-launcher` exactly. It computes a SHA1 hash over the concatenation of `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` and `HKCU\SOFTWARE\Frontier Developments\Cryptography\MachineGuid` (which is created if missing), converts to hex, and truncates to 16 lowercase characters.
- **Frontier API**: Talks to `https://api.zaonce.net` exactly like `min-ed-launcher`. It fetches time from `GET /1.1/server/time`, authenticates via `POST /3.0/user/frontier/auth`, and completes 2FA via `POST /3.0/user/frontier/token` to retrieve the final machine token.
- **Launching**: Spawns `MinEdLauncher.exe /frontier <profile> /autorun /<product-filter> /autoquit` with the ED install dir as the working directory.
- **Auto Exit**: `MinEdLauncher` quits as soon as the game is up, so its exit code is used as the "game started" signal — on code `0` the app shuts itself down after `AutoExitDelaySeconds`. A non-zero exit code keeps the window (and its log) open.

## Acknowledgments

This project would have been impossible without the work of other people. Special thanks to:

- [rfvgyhn](https://github.com/rfvgyhn) for creating [min-ed-launcher](https://github.com/rfvgyhn/min-ed-launcher) — the backbone of fast, headless ED launching and authorization.
- [thomas9120](https://github.com/thomas9120) for the inspiration and foundation from the original [ED-Account-Switcher](https://github.com/thomas9120/ED-Account-Switcher).

## License

MIT License
