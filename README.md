# Funky Launcher

**Funky Launcher** installs and runs FunkyPlay mod packs (modlists) for PC games in one click:
compatibility check, install, launch and repair. Mods live in their own folder; only 6 small loader files
go into the game folder, and "Restore clean game" removes them.

**Funky Launcher** ставит и запускает сборки модов FunkyPlay в один клик: проверка совместимости,
установка, запуск и починка. Моды живут в отдельной папке, в папку игры ставятся только 6 служебных
файлов-загрузчиков — кнопка «Вернуть чистую игру» их убирает.

Available now / сейчас доступна: **Neon Nights** for Cyberpunk 2077 2.5 + Phantom Liberty.

## Download / Скачать

**[Latest release / последний выпуск](https://github.com/FunkyPlay/FunkyLauncher/releases/latest)** — `FunkyLauncher.exe`, no install needed (.NET is built in).

Support / поддержка: Telegram [@FunkyLauncherBot](https://t.me/FunkyLauncherBot) · News / новости: [@FunkyPlayTV](https://t.me/FunkyPlayTV)

## What it does

- Checks the PC: game (Steam / GOG / Epic), game version, expansion, free space, Nexus Mods login.
- Installs the modlist with the **official [Wabbajack](https://github.com/wabbajack-tools/wabbajack)** (downloaded from its GitHub releases). Mods are downloaded from their authors' Nexus Mods pages — nothing is re-uploaded.
- Free Nexus accounts: a built-in browser opens each mod page; the launcher saves the file you download.
- Starts the game through Mod Organizer 2 with one **PLAY** button, watches for crashes and offers repair or going back to the previous modlist version.
- Library of FunkyPlay modlists (the catalog is `catalog.json` in this repository), useful software links, news from Telegram.
- Russian and English.

## Privacy / Конфиденциальность

The launcher collects no personal data and has no telemetry. It connects to the internet only to:
GitHub (modlist catalog, Wabbajack and modlist downloads), Telegram (public news of @FunkyPlayTV),
and — when you install or log in — Nexus Mods (through Wabbajack and the built-in browser).
News and catalog updates can be turned off in **Settings**. Details: **[PRIVACY.md](PRIVACY.md)**.

Лаунчер не собирает личные данные и не отправляет статистику. Подробности — в [PRIVACY.md](PRIVACY.md).

## Uninstall / Удаление

The launcher has no installer and does not change Windows settings.

1. In the launcher: Library → **Restore clean game** (takes the mod loaders out of the game folder).
2. Delete `FunkyLauncher.exe`.
3. Delete the modlist folder (by default `C:\Games\NeonNights`).
4. Delete the launcher data: `%LOCALAPPDATA%\FunkyLauncher` (settings, built-in browser profile, Wabbajack copy).
5. Optional: Wabbajack's own data `%LOCALAPPDATA%\Wabbajack` (Nexus login).

Удаление: в лаунчере «Библиотека» → «Вернуть чистую игру» (убирает загрузчики из папки игры), затем удалите
`FunkyLauncher.exe`, папку сборки (по умолчанию `C:\Games\NeonNights`) и папку `%LOCALAPPDATA%\FunkyLauncher`.

## Code signing policy

We have applied for free code signing by SignPath Foundation. Once approved, Windows releases are signed:
*Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).*

- Only `FunkyLauncher.exe` built from this repository by GitHub Actions ([workflow](.github/workflows/release.yml)) is signed. Wabbajack and mods are not signed by us — they come from their own authors.
- **Team roles** (members of the [FunkyPlay](https://github.com/FunkyPlay) organization):
  - Committers and reviewers: [Funky (@funkyzayka-maker)](https://github.com/funkyzayka-maker)
  - Approvers (approve every signing request by hand): [Funky (@funkyzayka-maker)](https://github.com/funkyzayka-maker)
- Privacy: see [PRIVACY.md](PRIVACY.md).

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build src/FunkyLauncher/FunkyLauncher.csproj -c Release
```

Releases are built by [GitHub Actions](.github/workflows/release.yml) from a version tag.

## Contributing / Участие

Outside code contributions (pull requests) are not accepted for now. Found a bug or have an idea? Open an
[issue](https://github.com/FunkyPlay/FunkyLauncher/issues) or write to [@FunkyLauncherBot](https://t.me/FunkyLauncherBot).

Правки кода со стороны (pull requests) пока не принимаются. Нашли ошибку или есть идея — откройте
[issue](https://github.com/FunkyPlay/FunkyLauncher/issues) или напишите в [@FunkyLauncherBot](https://t.me/FunkyLauncherBot).

## Disclaimer / Отказ от связи

Funky Launcher is an independent, unofficial app. It is not affiliated with or endorsed by any game publisher or
developer, Nexus Mods or Wabbajack. All game names and trademarks belong to their respective owners. Mods belong to
their authors and are downloaded from Nexus Mods.

Funky Launcher — независимая неофициальная программа. Она не связана с издателями и разработчиками игр, с Nexus Mods
и Wabbajack и не одобрена ими. Названия игр и товарные знаки принадлежат их владельцам. Моды принадлежат их авторам
и скачиваются с Nexus Mods.

## License

GPL-3.0-or-later — see [LICENSE](LICENSE). The names “Funky Launcher”, “FunkyPlay” and “Neon Nights” are not covered by the license.
