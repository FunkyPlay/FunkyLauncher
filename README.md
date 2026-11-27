# Funky Launcher — Nexus Mods version

This is the open source of the **Nexus Mods version** of Funky Launcher: a free Windows program that checks your game,
installs a ready modlist (Neon Nights for Cyberpunk 2077, Nova Nights for Starfield, Atomic Nights for Fallout 4) into
its own folder and starts the game with it.

- Mods are downloaded from the authors' pages on Nexus Mods (Premium: by themselves; free account: you press
  "Slow download" for each file and the launcher guides you).
- Only a few small mod-loader files go into the game folder; "Restore clean game" removes them.
- Everything in this version is free. It has no paid features, no remote help, no self-update (new versions come as
  new files on its Nexus Mods page) and no links to other sites.

## Build

```
dotnet publish src/FunkyLauncher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Windows 10 or 11 (64-bit), .NET 10 SDK.

## License

GPL-3.0-or-later — see [LICENSE](LICENSE). Third-party components: [licenses/THIRD-PARTY-NOTICES.txt](licenses/THIRD-PARTY-NOTICES.txt).
FunkyPatcher (src/FunkyPatcher, uses Mutagen) is a separate GPL-3.0 program run as its own process. The names
"Funky Launcher", "FunkyPlay" and "Neon Nights" are not covered by the license.

Funky Launcher is an independent, unofficial program. It is not affiliated with or endorsed by game publishers,
Nexus Mods or Wabbajack. Mods belong to their authors.

Privacy: [PRIVACY.md](PRIVACY.md).
