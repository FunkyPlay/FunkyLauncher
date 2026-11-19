# Privacy policy — Funky Launcher

*Last updated: 2026-11-20*

Funky Launcher does **not** collect personal data on its own, has **no telemetry** and sends **no usage statistics**.
Data is sent only when you yourself use the optional functions described below (support chat, Funky Help, Funky Pro tools).
There are no accounts in the launcher itself.

## Internet connections

| When | Where | What for | Can be turned off |
|---|---|---|---|
| On start | `raw.githubusercontent.com` (GitHub) | Download the list of modlists (`catalog.json`) | Yes — Settings → “Update the catalog from the internet” |
| On start | `t.me` (Telegram, public channel @FunkyPlayTV) | Show news on the Home page | Yes — Settings → “Load news” |
| When you install a modlist | `api.github.com`, `github.com` | Download the official Wabbajack and the modlist file | Only happens when you start an install |
| When you install / log in | `nexusmods.com` (through Wabbajack and the built-in browser) | Log in to Nexus Mods and download mods | Only happens when you install or log in |
| First time you open Funky Player | `github.com` (mpv and FFmpeg builds), `www.7-zip.org` (7zr unpacker) | Download the video engine once; files are checked by SHA-256 | Only happens when you open the player and agree to the download |
| When you press “Funky Help” | `github.com` (official RustDesk release); then our support server `2.31.81.143` | Download RustDesk once; connect to Funky support. The supporter sees and controls your screen **only while the session you started is open**. Sessions are not recorded. The connection log (your PC's RustDesk ID and IP address) is kept no longer than 31 days; the server also remembers your RustDesk ID and last IP so the connection works — we delete that record on request (it appears again if you use Funky Help again) | Only on click; you close the session at any time |
| When you press “Install” on the Software page | Microsoft winget (Windows) → the program author’s download server | Download and install that program; you accept its license once in the launcher | Only on click, after your consent |
| When you write in “Contact support” and press Send | our support server `api.funkylauncher.com` (Germany) | Deliver your message and get our reply | Yes — don't use the chat; Telegram works too |
| When you use the Funky Pro tools “Clip to my Telegram”, “Thanks from Funky”, “Voting” or “Wishlist” | our server `api.funkylauncher.com` | Check the Pro key signature (the key contains only an order number and an expiry date; it is not stored) and do what you asked | Only when you use these tools |
| When you click links | Websites of the programs on the Software page, YouTube, Telegram | Open them in your browser | Only on click |

These requests are normal web requests: the services see your IP address, as with any website.
Their own privacy policies apply:
[GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement),
[Telegram](https://telegram.org/privacy),
[Nexus Mods](https://help.nexusmods.com/article/20-privacy-policy),
[Wabbajack](https://github.com/wabbajack-tools/wabbajack).

**Support chat.** When you write to support in the launcher, we receive your message text, an attached screenshot or report (if you choose to send one), a random installation ID, the modlist name and the launcher version. We use them only to answer you. They are stored on our server in Germany (Hetzner): screenshots for 90 days, everything else for 12 months after your last contact. We do not share them with third parties. You can delete your data with the “Delete my data” button in the chat or by email privacy@funkylauncher.com; we will respond within 10 working days. The data controller is the individual entrepreneur Funky (Georgia).

**Report a flaw (optional).** If you press “Report a flaw”, the launcher sends us the text you type and the screenshot you attach, together with the modlist name, its version, its variant (for example Russian or English), the launcher version and a random installation ID (used to limit spam: at most 10 reports a day per installation). Nothing else about you or your PC is sent. To limit abuse our server also counts requests per IP address (at most 30 reports an hour); the IP address is held only in the server's memory for up to one hour and is not written to any database or log. The report goes to a separate queue on our server; an AI service (Claude, Anthropic, USA) first checks the screenshot automatically, and a screenshot with personal content (faces of real people, chats, documents, addresses, phone numbers) is deleted at once and an unclear one is hidden and kept for up to 90 days (the text stays); a screenshot of the game itself may show adult (18+) content from mods you installed yourself, and it is used only to fix the modlist; the report is then read by us, with the help of the same service, only to fix the modlist. We do not reply to these reports and do not publish them or your screenshot. We keep the text and versions for 12 months and the screenshot for 90 days. You can delete your reports with the “Delete my data” button in the launcher, or ask us at privacy@funkylauncher.com; we delete them within 10 working days. Please do not send personal data on the screenshot, only the game itself. Controller: the individual entrepreneur Funky (Georgia).

**Funky Pro: “Clip to my Telegram” (optional).** If you link your Telegram to our bot @FunkyLauncherBot, our server stores an installation ID, a hash of its key, your Telegram chat ID and your display name from Telegram. When you send a screenshot or a clip, the file passes through our server only to be delivered to you and is not written to the server disk. Before sending, the launcher warns you: “Check the frame: it may contain personal data.” You can unlink at any time with the button in the launcher or the command /unlink; we then delete the stored chat ID and name. To check the key, the server keeps only the installation ID and a hash of its key. Our server keeps no log of IP addresses for these requests. Telegram's own privacy policy applies to the delivery. Controller: the individual entrepreneur Funky (Georgia).

**“Thanks from Funky” (optional).** If you tick the box (it is off by default) and enter a name of up to 32 characters, the launcher sends the name to our server, which stores the installation ID, the name, an approval flag and the time. The name appears in a public list only after Funky approves it. You can remove it at any time by unticking the box: the data is deleted immediately. Do not enter a real name or personal data if you do not want it to be public. The list shows names only, not purchases or amounts.

**Voting (optional).** When you vote in Funky Pro, our server stores an installation ID, a hash of its key, the option you chose and the time. One vote per launcher. Votes are used only to decide what to build next and are shown as totals, never with your ID. You can ask us to delete your vote at privacy@funkylauncher.com.

**Wishlist (optional).** If you mark games, our server stores an installation ID, the IDs and names of the games, the time and the last discount it told you about, so the launcher can tell you about a new discount. Prices come from our discount bot and are for reference only; the launcher buys nothing. You can remove a game from the list at any time; removing it deletes it from the server.

## Data stored on your PC

- `%LOCALAPPDATA%\FunkyLauncher` — settings, cached catalog, the built-in browser profile (your Nexus Mods login
  for free downloads), the Wabbajack copy and downloaded modlist files.
- The modlist folder you chose (for example `C:\Games\NeonNights`).
- Wabbajack keeps its own Nexus login in `%LOCALAPPDATA%\Wabbajack`.
- Save backups — `%LOCALAPPDATA%\FunkyLauncher\Backups` or the folder you chose.
- Funky Pro (if you have it): the cloud backup only writes archives into the cloud folder already on your PC
  (OneDrive, Yandex Disk, Dropbox or Google Drive, `FunkyLauncher Backups`). The launcher does not upload anything itself —
  your cloud app syncs that folder under its own privacy policy. The Pro key is checked offline when you activate it.

Nothing in this list is sent by the launcher, except what is described in “Funky Pro: Clip to my Telegram”, “Thanks from Funky”, “Voting” and “Wishlist” above.

## Error reports

“Make an error report” creates a zip file with log files **on your Desktop**. It is never sent automatically —
you decide whether to send it to support.

---

# Политика конфиденциальности — Funky Launcher

Лаунчер сам **не собирает** личные данные, **не отправляет** статистику и телеметрию, своих аккаунтов у него нет. Данные передаются только когда вы сами пользуетесь необязательными функциями ниже (чат поддержки, Funky Помощь, инструменты Funky Pro).

Он обращается в интернет только:
- **при запуске** — к GitHub (список сборок) и к публичному Telegram-каналу @FunkyPlayTV (новости). Оба можно
  выключить в «Настройках»;
- **при установке** — к GitHub (официальный Wabbajack и файл сборки) и к Nexus Mods (вход и скачивание модов);
- **при первом открытии Funky Плеера** — к GitHub (сборки mpv и FFmpeg) и к 7-zip.org (распаковщик): один раз
  скачивает видеодвижок, только после вашего согласия; файлы проверяются по SHA-256;
- **по кнопке «Funky Помощь»** — к GitHub (официальный RustDesk, скачивается один раз) и к нашему серверу поддержки
  `2.31.81.143`. Поддержка видит ваш экран и может управлять компьютером **только пока открыт сеанс, который вы
  сами начали**; закрыть его можно в любой момент. Сеансы не записываются. Журнал подключений (ID RustDesk вашего
  компьютера и IP-адрес) хранится не дольше 31 дня, затем удаляется. Кроме того, сервер помнит ID RustDesk и последний
  IP-адрес, чтобы соединение работало; по вашей просьбе мы удалим эту запись (если снова воспользуетесь Помощью,
  она появится снова);
- **по кнопке «Установить» в разделе «Софт»** — Windows (winget, менеджер программ Microsoft) скачивает программу с сервера её автора; только после вашего согласия с лицензиями;
- **инструменты Funky Pro «Клип в мой Telegram», «Спасибо от Funky», «Голосование» и «Список желаемых»** — к нашему серверу `api.funkylauncher.com`: проверка подписи ключа Pro (в ключе только номер заказа и срок; ключ не хранится) и то, что вы попросили; только когда вы пользуетесь этими инструментами;
- **по клику** на ссылки — открывает сайты в вашем браузере.

Сервисы видят ваш IP-адрес, как при открытии любого сайта; действуют их политики конфиденциальности (ссылки выше).

Данные лаунчера лежат только у вас на ПК: `%LOCALAPPDATA%\FunkyLauncher` (настройки, бэкапы сохранений) и папка сборки.
Funky Pro: облачный бэкап только кладёт архивы в папку облака, которая уже есть на вашем ПК (OneDrive, Яндекс Диск,
Dropbox, Google Drive); сам лаунчер ничего не выгружает. Ключ Pro при активации проверяется без интернета.
Отчёт об ошибке сохраняется на рабочий стол и никуда сам не отправляется.

**Чат поддержки.** Когда вы пишете в поддержку из лаунчера, мы получаем текст сообщения, скриншот или отчёт (если вы сами его приложите), случайный номер установки, название сборки и версию лаунчера. Мы используем их только чтобы ответить вам. Данные хранятся на нашем сервере в Германии (Hetzner): скриншоты — 90 дней, остальное — 12 месяцев после последнего обращения. Третьим лицам мы их не передаём. Удалить данные можно кнопкой «Удалить мои данные» в чате или письмом на privacy@funkylauncher.com: ответим не позднее чем через 10 рабочих дней. Контролёр данных — индивидуальный предприниматель Funky (Грузия).

**«Сообщить о недочёте» (по желанию).** Если вы нажимаете «Сообщить о недочёте», лаунчер отправляет нам текст, который вы написали, и снимок экрана, который вы приложили, вместе с названием сборки, её версией, вариантом (например, русская или английская), версией лаунчера и случайным номером установки (он нужен, чтобы ограничить спам: не больше 10 сообщений в сутки с установки). Больше ничего о вас и вашем компьютере не отправляется. Чтобы ограничить злоупотребления, сервер также считает запросы по IP-адресу (не больше 30 сообщений в час); IP-адрес держится только в памяти сервера не дольше часа и не записывается ни в базу, ни в журналы. Сообщение попадает в отдельную очередь на нашем сервере; сначала снимок автоматически проверяет ИИ-сервис (Claude, Anthropic, США): снимок с личным содержимым (лица реальных людей, переписки, документы, адреса, телефоны) сразу удаляется, непонятный скрывается и хранится до 90 дней (текст остаётся); на снимке игры может быть контент 18+ из модов, которые вы сами установили, он используется только чтобы исправить сборку; затем сообщение читаем мы, с помощью того же сервиса, только чтобы исправить сборку. Мы не отвечаем на такие сообщения и не публикуем ни их, ни ваш снимок. Текст и версии храним 12 месяцев, снимок — 90 дней. Удалить свои сообщения можно кнопкой «Удалить мои данные» в лаунчере или по просьбе: privacy@funkylauncher.com; удаляем в течение 10 рабочих дней. Не присылайте на снимке личное, только саму игру. Оператор данных — ИП Funky (Грузия).

**Funky Pro: «Клип в мой Telegram» (по желанию).** Если вы привязываете свой Telegram к нашему боту @FunkyLauncherBot, наш сервер хранит номер установки, хэш её ключа, ваш chat_id и отображаемое имя из Telegram. Когда вы отправляете скриншот или клип, файл проходит через наш сервер только для доставки вам и на диск сервера не записывается. Перед отправкой лаунчер предупреждает: «Проверь кадр: на нём могут быть личные данные». Отвязать Telegram можно в любой момент кнопкой в лаунчере или командой /unlink: после этого сохранённые chat_id и имя удаляются. Для проверки ключа сервер хранит только номер установки и хэш её ключа. Журнал IP-адресов для этих запросов наш сервер не ведёт. На доставку действует политика конфиденциальности Telegram. Оператор данных — ИП Funky (Грузия).

**«Спасибо от Funky» (по желанию).** Если вы ставите галочку (по умолчанию выключена) и вводите имя до 32 знаков, лаунчер отправляет имя на наш сервер, который хранит номер установки, имя, отметку об одобрении и время. Имя появляется в публичном списке только после одобрения Funky. Убрать имя можно в любой момент, сняв галочку: данные удаляются сразу. Не вводите настоящее имя и личные данные, если не хотите, чтобы их увидели. В списке только имена, без покупок и сумм.

**Голосование (по желанию).** Когда вы голосуете в Funky Pro, наш сервер хранит номер установки, хэш её ключа, выбранный вариант и время. Один голос с лаунчера. Голоса нужны только чтобы решить, что делать дальше; публично показываются суммы, а не ваш номер. Удалить свой голос можно по просьбе: privacy@funkylauncher.com.

**Список желаемых (по желанию).** Если вы отмечаете игры, наш сервер хранит номер установки, id и названия игр, время и последнюю показанную скидку, чтобы лаунчер сообщил о новой скидке. Цены берутся из нашего бота скидок и справочные; лаунчер ничего не покупает. Убрать игру из списка можно в любой момент; при удалении она стирается и на сервере.
