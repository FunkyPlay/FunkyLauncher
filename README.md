# Funky Launcher

Launcher for the **Neon Nights by Funky** modlist for Cyberpunk 2077 (2.5 + Phantom Liberty).

Лаунчер сборки **Neon Nights by Funky** для Cyberpunk 2077 (2.5 + Phantom Liberty).

- Checks the PC: game (Steam / GOG / Epic), version, Phantom Liberty, free space, Nexus login.
- Installs the modlist with the **official Wabbajack** (`wabbajack-cli install`). Mods are downloaded from Nexus Mods — nothing is re-uploaded.
- Starts the game through Mod Organizer 2 with one **PLAY** button.
- Shows news from the Telegram channel [@FunkyPlayTV](https://t.me/FunkyPlayTV).
- Russian and English.

## Build

Requires the .NET 10 SDK on Windows.

```
dotnet build src/FunkyLauncher/FunkyLauncher.csproj -c Release
```

## License

GPL-3.0-or-later — see [LICENSE](LICENSE). The names “Funky Launcher”, “FunkyPlay” and “Neon Nights” are not covered by the license.
