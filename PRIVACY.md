# Privacy policy — Funky Launcher

*Last updated: 2026-11-10*

Funky Launcher does **not** collect personal data, has **no telemetry** and sends **no usage statistics**.
There are no accounts in the launcher itself.

## Internet connections

| When | Where | What for | Can be turned off |
|---|---|---|---|
| On start | `raw.githubusercontent.com` (GitHub) | Download the list of modlists (`catalog.json`) | Yes — Settings → “Update the catalog from the internet” |
| On start | `t.me` (Telegram, public channel @FunkyPlayTV) | Show news on the Home page | Yes — Settings → “Load news” |
| When you install a modlist | `api.github.com`, `github.com` | Download the official Wabbajack and the modlist file | Only happens when you start an install |
| When you install / log in | `nexusmods.com` (through Wabbajack and the built-in browser) | Log in to Nexus Mods and download mods | Only happens when you install or log in |
| When you click links | Websites of the programs on the Software page, YouTube, Telegram | Open them in your browser | Only on click |

These requests are normal web requests: the services see your IP address, as with any website.
Their own privacy policies apply:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Telegram](https://telegram.org/privacy),
[Nexus Mods](https://help.nexusmods.com/article/20-privacy-policy),
[Wabbajack](https://github.com/wabbajack-tools/wabbajack).

## Data stored on your PC

- `%LOCALAPPDATA%\FunkyLauncher` — settings, cached catalog, the built-in browser profile (your Nexus Mods login
  for free downloads), the Wabbajack copy and downloaded modlist files.
- The modlist folder you chose (for example `C:\Games\NeonNights`).
- Wabbajack keeps its own Nexus login in `%LOCALAPPDATA%\Wabbajack`.

Nothing of this is sent anywhere by the launcher.

## Error reports

“Make an error report” creates a zip file with log files **on your Desktop**. It is never sent automatically —
you decide whether to send it to support.

---

# Политика конфиденциальности — Funky Launcher

Лаунчер **не собирает** личные данные, **не отправляет** статистику и телеметрию, своих аккаунтов у него нет.

Он обращается в интернет только:
- **при запуске** — к GitHub (список сборок) и к публичному Telegram-каналу @FunkyPlayTV (новости). Оба можно
  выключить в «Настройках»;
- **при установке** — к GitHub (официальный Wabbajack и файл сборки) и к Nexus Mods (вход и скачивание модов);
- **по клику** на ссылки — открывает сайты в вашем браузере.

Сервисы видят ваш IP-адрес, как при открытии любого сайта; действуют их политики конфиденциальности (ссылки выше).

Данные лаунчера лежат только у вас на ПК: `%LOCALAPPDATA%\FunkyLauncher` и папка сборки.
Отчёт об ошибке сохраняется на рабочий стол и никуда сам не отправляется.
