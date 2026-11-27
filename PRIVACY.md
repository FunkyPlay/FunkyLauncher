> This is the privacy policy of the **Nexus Mods version** of Funky Launcher. It only installs and starts the modlists: no self-update, no online catalog, no news, no support chat, no remote help, no other tools that go online.

# Privacy policy — Funky Launcher

*Last updated: 2026-11-27*

Funky Launcher does **not** collect personal data on its own, has **no telemetry** and sends **no usage statistics**.
It sends nothing about you anywhere.
There are no accounts in the launcher itself.

## Internet connections

| When | Where | What for | Can be turned off |
|---|---|---|---|
| Only if you choose to install it | Microsoft (the WebView2 Runtime installer) | Install the built-in browser engine the launcher needs for Nexus Mods | Only happens when you agree |
| When you install a modlist | `api.github.com`, `github.com` | Download the official Wabbajack and the modlist file | Only happens when you start an install |
| When you install / log in | `nexusmods.com` (through Wabbajack and the built-in browser) | Log in to Nexus Mods and download mods | Only happens when you install or log in |
| When you click links | Mod pages on Nexus Mods | Open them in your browser | Only on click |

These requests are normal web requests: the services see your IP address, as with any website.
Their own privacy policies apply:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Nexus Mods](https://help.nexusmods.com/article/20-privacy-policy),
[Wabbajack](https://github.com/wabbajack-tools/wabbajack).



## Data stored on your PC

- `%LOCALAPPDATA%\FunkyLauncher` — settings, the built-in browser profile (your Nexus Mods login
  for free downloads), the Wabbajack copy and downloaded modlist files.
- The modlist folder you chose (for example `C:\Games\NeonNights`).
- Wabbajack keeps its own Nexus login in `%LOCALAPPDATA%\Wabbajack`.
- Save backups — `%LOCALAPPDATA%\FunkyLauncher\Backups` or the folder you chose.
- “Moving to a new PC” writes one file with your saves and settings to the place you choose; nothing is sent.
- If you download a mod in your normal browser, the launcher may look in your Downloads folder only for the file you
  downloaded yourself to install that mod (by its name and size) and copies it to itself; nothing is sent.

Nothing in this list is sent by the launcher.

## Error reports

“Make an error report” creates a zip file with log files **on your Desktop**. It is never sent anywhere — keep it
in case we ask about it when you write in the comments or bug reports on the launcher’s Nexus Mods page.
The report may also include whether the launcher runs as administrator, the WebView2 version and the end of launcher.log
(it can contain folder paths and the Windows user name). It stays on your Desktop; keep it in case we ask about it.
Since 0.1.20 the report may also include REDscope crash files of the game (the 3 newest crashes and REDscope's own log). They can contain folder paths, the Windows user name and small pieces of the game's memory at the moment of the crash. Large memory dumps (.zip, .dmp) are not included. The report is sent only if you send it yourself.

---

# Политика конфиденциальности — Funky Launcher

*Обновлено: 27.11.2026*

Лаунчер сам **не собирает** личные данные, **не отправляет** статистику и телеметрию, своих аккаунтов у него нет. Данные передаются только когда вы сами пользуетесь необязательными функциями ниже — лаунчер ничего о вас никуда не отправляет.

Он обращается в интернет только:
- **только если вы согласились его установить** — к Microsoft (установщик WebView2 — движок встроенного браузера для Nexus Mods);
- **при установке** — к GitHub (официальный Wabbajack и файл сборки) и к Nexus Mods (вход и скачивание модов);
- **по клику** на ссылки — открывает сайты в вашем браузере.

Сервисы видят ваш IP-адрес, как при открытии любого сайта; действуют их политики конфиденциальности (ссылки выше).

Данные лаунчера лежат только у вас на ПК: `%LOCALAPPDATA%\FunkyLauncher` (настройки, бэкапы сохранений) и папка сборки.
«Переезд на новый ПК» записывает один файл с вашими сохранениями и настройками туда, куда вы укажете; ничего не отправляется.
Отчёт об ошибке сохраняется на рабочий стол и никуда сам не отправляется.
В отчёт также попадают: запущен ли лаунчер от администратора, версия WebView2 и конец launcher.log (в нём могут быть пути к папкам и имя пользователя Windows). Он лежит на рабочем столе — сохраните его на случай, если мы о нём спросим.
С версии 0.1.20 в отчёт также попадают файлы вылетов REDscope (3 последних вылета и журнал REDscope). В них могут быть пути к папкам, имя пользователя Windows и небольшие фрагменты памяти игры в момент вылета. Большие снимки памяти (.zip, .dmp) не берутся. Сам отчёт никуда не уходит.
Лаунчер может искать в папке «Загрузки» только файл, который вы сами скачали для установки мода (по имени и размеру), и копирует его к себе; ничего не отправляется.


