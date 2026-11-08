# Chest Counter

A small always-on-top overlay that counts Fortnite chests. Built for streams: use hotkeys to add or remove a chest, and get a gold celebration every 100.

By **@hypr.7**

## Features
- Hotkeys that work while the game is focused (default **F8** +1, **F7** -1, **F9** reset), changeable in Settings
- Type in a number (`1000`, `2,500`, `2.5k`)
- Resizable, draggable, can be pinned to the taskbar
- Pop animation and a 100-chest celebration (both can be turned off)
- Saves your count and settings in `%AppData%\ChestCounter`

## Download
Grab `ChestCounter-windows.zip` from the [Releases](../../releases) page, extract it, and run `ChestCounter.exe`.
Set Fortnite to **Windowed Fullscreen** so the overlay can show on top.

Windows may show "Windows protected your PC" because the app isn't signed with a paid certificate. Click **More info > Run anyway**.

## What it does (and doesn't)
- Checks whether your chosen keys are pressed (`GetAsyncKeyState`) and reads/writes two small text files.
- No network code. It doesn't read, inject into, or modify the game.
- All the code is in [`ChestCounter.cs`](ChestCounter.cs), about 900 lines, so you can read it yourself.

## Verify your download
Releases are built by GitHub Actions from this source. To check that an exe really came from this repo:

```
gh attestation verify ChestCounter.exe --repo YOUR-USERNAME/chest-counter
```

## Build it yourself
Needs nothing but Windows. Download the repo and double-click `build.cmd` (it uses the C# compiler that ships with Windows).

## Disclaimer
Not affiliated with or endorsed by Epic Games. Fortnite and the chest artwork (`chest.png` / `chest.ico`) belong to Epic Games and are **not** covered by this project's MIT license. The code is MIT licensed (see [LICENSE](LICENSE)).
