> This is the privacy policy of the **Nexus Mods version** of Funky Launcher. It has no self-update, no news feed and no remote help.

# Privacy policy — Funky Launcher

*Last updated: 2026-11-27*

Funky Launcher does **not** collect personal data on its own, has **no telemetry** and sends **no usage statistics**.
Data is sent only when you yourself use the optional functions described below (support chat, “Report a flaw”).
There are no accounts in the launcher itself.

## Internet connections

| When | Where | What for | Can be turned off |
|---|---|---|---|
| On start | `raw.githubusercontent.com` (GitHub) | Download the list of modlists (`catalog.json`) | Yes — Settings → “Update the catalog from the internet” |
| When you install a modlist | `api.github.com`, `github.com` | Download the official Wabbajack and the modlist file | Only happens when you start an install |
| When you install / log in | `nexusmods.com` (through Wabbajack and the built-in browser) | Log in to Nexus Mods and download mods | Only happens when you install or log in |
| First time you open Funky Player | `github.com` (mpv and FFmpeg builds), `www.7-zip.org` (7zr unpacker) | Download the video engine once; files are checked by SHA-256 | Only happens when you open the player and agree to the download |
| When you press “Install” on the Software page | Microsoft winget (Windows) → the program author’s download server | Download and install that program; you accept its license once in the launcher | Only on click, after your consent |
| When you write in “Contact support” and press Send | our support server `api.funkylauncher.com` (Germany) | Deliver your message and get our reply | Yes — don't use the chat |
| When you type words instead of an address in the built-in browser | `google.com` | Search for what you typed | Only when you search |
| When you click links | Websites of the programs on the Software page and mod pages | Open them in your browser | Only on click |

These requests are normal web requests: the services see your IP address, as with any website.
Their own privacy policies apply:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Nexus Mods](https://help.nexusmods.com/article/20-privacy-policy),
[Wabbajack](https://github.com/wabbajack-tools/wabbajack).

**Support chat.** When you write to support in the launcher, we receive your message text, an attached screenshot or report (if you choose to send one), a random installation ID, the modlist name and the launcher version. We use them only to answer you. They are stored on our server in Germany (Hetzner) and in a working copy on the developer's computer: screenshots and error reports for 90 days, everything else for 12 months after your last contact. We do not share them with third parties. You can delete your data with the “Delete my data” button in the chat or by email privacy@funkylauncher.com; we will respond within 10 working days. The data controller is the individual entrepreneur Funky (Georgia).

**Report a flaw (optional).** If you press “Report a flaw”, the launcher sends us the text you type and the screenshot you attach, together with the modlist name, its version, its variant (for example Russian or English), the launcher version and a random installation ID (used to limit spam: at most 10 reports a day per installation). You can also use the same window to suggest a mod: then we receive the mod name, the Nexus link you give and the text you write, with the same technical data and the same retention periods; a screenshot is sent only if you attach one, and it is treated as described above. Nothing else about you or your PC is sent. To limit abuse our server also counts requests per IP address (at most 30 reports an hour); the IP address is held only in the server's memory for up to one hour and is not written to any database or log. The report goes to a separate queue on our server; an AI service (Claude, Anthropic, USA) first checks the screenshot automatically, and a screenshot with personal content (faces of real people, chats, documents, addresses, phone numbers) is deleted at once and an unclear one is hidden and kept for up to 90 days (the text stays); a screenshot of the game itself may show adult (18+) content from mods you installed yourself, and it is used only to fix the modlist; the report is then read by us, with the help of the same service, only to fix the modlist. We do not reply to these reports and do not publish them or your screenshot. We keep the text and versions for 12 months and the screenshot for 90 days. A working copy of the report and screenshot is kept on the developer's computer, in a folder synced with Microsoft OneDrive cloud storage; the same retention periods apply there (a deleted file may remain in the OneDrive recycle bin for up to 30 more days). You can delete your reports with the “Delete my data” button in the launcher, or ask us at privacy@funkylauncher.com; we delete them within 10 working days. Please do not send personal data on the screenshot, only the game itself. Controller: the individual entrepreneur Funky (Georgia).

## Data stored on your PC

- `%LOCALAPPDATA%\FunkyLauncher` — settings, cached catalog, the built-in browser profile (your Nexus Mods login
  for free downloads), the Wabbajack copy and downloaded modlist files.
- The modlist folder you chose (for example `C:\Games\NeonNights`).
- Wabbajack keeps its own Nexus login in `%LOCALAPPDATA%\Wabbajack`.
- Save backups — `%LOCALAPPDATA%\FunkyLauncher\Backups` or the folder you chose.
- “Moving to a new PC” writes one file with your saves and settings to the place you choose; nothing is sent.
- If you download a mod in your normal browser, the launcher may look in your Downloads folder only for the file you
  downloaded yourself to install that mod (by its name and size) and copies it to itself; nothing is sent.

Nothing in this list is sent by the launcher.

## Error reports

“Make an error report” creates a zip file with log files **on your Desktop**. It is never sent automatically —
you decide whether to send it to support.
The report may also include whether the launcher runs as administrator, the WebView2 version and the end of launcher.log
(it can contain folder paths and the Windows user name). It stays on your Desktop; you decide whether to send it.
Since 0.1.20 the report may also include REDscope crash files of the game (the 3 newest crashes and REDscope's own log). They can contain folder paths, the Windows user name and small pieces of the game's memory at the moment of the crash. Large memory dumps (.zip, .dmp) are not included. The report is sent only if you send it yourself.

---

# Политика конфиденциальности — Funky Launcher

*Обновлено: 27.11.2026*

Лаунчер сам **не собирает** личные данные, **не отправляет** статистику и телеметрию, своих аккаунтов у него нет. Данные передаются только когда вы сами пользуетесь необязательными функциями ниже (чат поддержки, «Сообщить о недочёте»).

Он обращается в интернет только:
- **при запуске** — к GitHub (список сборок). Это можно выключить в «Настройках»;
- **при установке** — к GitHub (официальный Wabbajack и файл сборки) и к Nexus Mods (вход и скачивание модов);
- **при первом открытии Funky Плеера** — к GitHub (сборки mpv и FFmpeg) и к 7-zip.org (распаковщик): один раз
  скачивает видеодвижок, только после вашего согласия; файлы проверяются по SHA-256;
- **по кнопке «Установить» в разделе «Софт»** — Windows (winget, менеджер программ Microsoft) скачивает программу с сервера её автора; только после вашего согласия с лицензиями;
- **при поиске во встроенном браузере** (если ввести слова вместо адреса) — к google.com;
- **по клику** на ссылки — открывает сайты в вашем браузере.

Сервисы видят ваш IP-адрес, как при открытии любого сайта; действуют их политики конфиденциальности (ссылки выше).

Данные лаунчера лежат только у вас на ПК: `%LOCALAPPDATA%\FunkyLauncher` (настройки, бэкапы сохранений) и папка сборки.
«Переезд на новый ПК» записывает один файл с вашими сохранениями и настройками туда, куда вы укажете; ничего не отправляется.
Отчёт об ошибке сохраняется на рабочий стол и никуда сам не отправляется.
В отчёт также попадают: запущен ли лаунчер от администратора, версия WebView2 и конец launcher.log (в нём могут быть пути к папкам и имя пользователя Windows). Он лежит на рабочем столе, отправлять ли его — решаешь ты.
С версии 0.1.20 в отчёт также попадают файлы вылетов REDscope (3 последних вылета и журнал REDscope). В них могут быть пути к папкам, имя пользователя Windows и небольшие фрагменты памяти игры в момент вылета. Большие снимки памяти (.zip, .dmp) не берутся. Отчёт уходит, только если ты сам его отправил.
Лаунчер может искать в папке «Загрузки» только файл, который вы сами скачали для установки мода (по имени и размеру), и копирует его к себе; ничего не отправляется.

**Чат поддержки.** Когда вы пишете в поддержку из лаунчера, мы получаем текст сообщения, скриншот или отчёт (если вы сами его приложите), случайный номер установки, название сборки и версию лаунчера. Мы используем их только чтобы ответить вам. Данные хранятся на нашем сервере в Германии (Hetzner) и в рабочей копии на компьютере разработчика: скриншоты и отчёты об ошибке — 90 дней, остальное — 12 месяцев после последнего обращения. Третьим лицам мы их не передаём. Удалить данные можно кнопкой «Удалить мои данные» в чате или письмом на privacy@funkylauncher.com: ответим не позднее чем через 10 рабочих дней. Контролёр данных — индивидуальный предприниматель Funky (Грузия).

**«Сообщить о недочёте» (по желанию).** Если вы нажимаете «Сообщить о недочёте», лаунчер отправляет нам текст, который вы написали, и снимок экрана, который вы приложили, вместе с названием сборки, её версией, вариантом (например, русская или английская), версией лаунчера и случайным номером установки (он нужен, чтобы ограничить спам: не больше 10 сообщений в сутки с установки). В том же окне можно предложить мод: тогда мы получаем название мода, ссылку на Nexus и ваш текст, с теми же техническими данными и сроками хранения; снимок отправляется только если вы его приложили и обрабатывается так же, как описано выше. Больше ничего о вас и вашем компьютере не отправляется. Чтобы ограничить злоупотребления, сервер также считает запросы по IP-адресу (не больше 30 сообщений в час); IP-адрес держится только в памяти сервера не дольше часа и не записывается ни в базу, ни в журналы. Сообщение попадает в отдельную очередь на нашем сервере; сначала снимок автоматически проверяет ИИ-сервис (Claude, Anthropic, США): снимок с личным содержимым (лица реальных людей, переписки, документы, адреса, телефоны) сразу удаляется, непонятный скрывается и хранится до 90 дней (текст остаётся); на снимке игры может быть контент 18+ из модов, которые вы сами установили, он используется только чтобы исправить сборку; затем сообщение читаем мы, с помощью того же сервиса, только чтобы исправить сборку. Мы не отвечаем на такие сообщения и не публикуем ни их, ни ваш снимок. Текст и версии храним 12 месяцев, снимок — 90 дней. Рабочая копия сообщения и снимка есть на компьютере разработчика, в папке, которая синхронизируется с облаком Microsoft OneDrive; сроки там те же (удалённый файл может ещё до 30 дней оставаться в корзине OneDrive). Удалить свои сообщения можно кнопкой «Удалить мои данные» в лаунчере или по просьбе: privacy@funkylauncher.com; удаляем в течение 10 рабочих дней. Не присылайте на снимке личное, только саму игру. Оператор данных — ИП Funky (Грузия).
