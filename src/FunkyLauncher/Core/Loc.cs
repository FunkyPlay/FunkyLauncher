// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Globalization;

namespace FunkyLauncher.Core;

/// <summary>Russian / English texts. Loc.T("key") returns the text in the current language.</summary>
public static class Loc
{
    public static string Lang { get; private set; } = "ru";

    public static event Action? Changed;

    public static void Set(string lang)
    {
        Lang = lang == "en" ? "en" : "ru";
        Changed?.Invoke();
    }

    public static string DefaultLang() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName is "ru" or "uk" or "be" or "kk" ? "ru" : "en";

    public static string T(string key) =>
        Texts.TryGetValue(key, out var pair) ? (Lang == "en" ? pair.En : pair.Ru) : key;

    public static string F(string key, params object[] args) => string.Format(T(key), args);

    private static readonly Dictionary<string, (string Ru, string En)> Texts = new()
    {
        ["nav.home"] = ("Главная", "Home"),
        ["nav.library"] = ("Библиотека", "Library"),
        ["nav.soft"] = ("Софт", "Software"),

        ["lib.title"] = ("Библиотека", "Library"),
        ["lib.subtitle"] = ("Все сборки FunkyPlay. Новые появляются здесь сами — лаунчер обновлять не нужно.", "All FunkyPlay modlists. New ones appear here automatically — no need to update the launcher."),
        ["lib.setup"] = ("Установка и проверка", "Install and verify"),

        ["start.title"] = ("FUNKY LAUNCHER", "FUNKY LAUNCHER"),
        ["start.text"] = ("Сборки модов FunkyPlay в один клик: проверка компьютера, установка, запуск и починка. Сама игра не меняется — моды живут в отдельной папке, поэтому всегда можно вернуться к чистой игре.", "FunkyPlay modlists in one click: PC check, install, launch and repair. Your game is never changed — mods live in their own folder, so you can always go back to the clean game."),
        ["start.library"] = ("ОТКРЫТЬ БИБЛИОТЕКУ", "OPEN LIBRARY"),
        ["start.whatsnew"] = ("Что нового · версия {0}", "What's new · version {0}"),
        ["start.whatsnew.items"] = ("• Новая главная, библиотека и раздел «Софт»\n• Скачивание модов с бесплатным аккаунтом Nexus\n• Каталог сборок обновляется с GitHub\n• Проверка файлов сборки", "• New home page, library and Software section\n• Mod downloads with a free Nexus account\n• Modlist catalog updates from GitHub\n• Modlist file verification"),
        ["start.support"] = ("Поддержка", "Support"),
        ["start.support.text"] = ("Что-то не работает? Соберите отчёт и отправьте его нам в Telegram — разберёмся.", "Something not working? Make a report and send it to us on Telegram — we'll sort it out."),
        ["start.support.write"] = ("Написать в поддержку", "Contact support"),
        ["start.support.report"] = ("Собрать отчёт об ошибке", "Make an error report"),
        ["start.report.working"] = ("Собираем отчёт…", "Making the report…"),
        ["start.report.done"] = ("Отчёт сохранён на рабочий стол: {0}. Отправьте этот файл в поддержку.", "Report saved to the Desktop: {0}. Send this file to support."),
        ["start.report.error"] = ("Не удалось собрать отчёт: {0}", "Could not make the report: {0}"),

        ["soft.title"] = ("Полезный софт", "Useful software"),
        ["soft.subtitle"] = ("Проверенные программы для игр и модов. Кнопка открывает официальный сайт программы.", "Trusted programs for games and mods. The button opens the program's official website."),
        ["soft.open"] = ("Скачать с сайта", "Get from website"),

        ["setup.back"] = ("← Библиотека", "← Library"),
        ["lib.installed"] = ("Установлено", "Installed"),
        ["lib.available"] = ("Доступно · {0} ГБ", "Available · {0} GB"),
        ["lib.soon"] = ("Скоро", "Coming soon"),
        ["lib.open"] = ("Открыть", "Open"),
        ["lib.mods"] = ("{0} модов", "{0} mods"),
        ["nav.setup"] = ("Установка", "Install"),
        ["nav.settings"] = ("Настройки", "Settings"),

        ["home.tag"] = ("Cyberpunk 2077 · 2.5 + Phantom Liberty", "Cyberpunk 2077 · 2.5 + Phantom Liberty"),
        ["home.sub.installed"] = ("by {0} · версия {1} · установлено в {2}", "by {0} · version {1} · installed in {2}"),
        ["home.sub.notinstalled"] = ("by {0} · версия {1} · ещё не установлено", "by {0} · version {1} · not installed yet"),
        ["home.sub.soon"] = ("by {0} · скоро", "by {0} · coming soon"),
        ["home.play"] = ("ИГРАТЬ", "PLAY"),
        ["home.install"] = ("УСТАНОВИТЬ", "INSTALL"),
        ["home.folder"] = ("Папка сборки", "Modlist folder"),
        ["home.mo2"] = ("Открыть MO2", "Open MO2"),
        ["home.news"] = ("Новости FunkyPlay", "FunkyPlay news"),
        ["home.news.all"] = ("Все в Telegram →", "All in Telegram →"),
        ["home.news.loading"] = ("Загружаем новости…", "Loading news…"),
        ["home.news.empty"] = ("Новостей пока нет — канал скоро откроется.", "No news yet — the channel opens soon."),
        ["home.status"] = ("Статус", "Status"),
        ["home.ready"] = ("Готово к игре", "Ready to play"),
        ["home.notready"] = ("Сборка не установлена", "Modlist not installed"),
        ["home.play.started"] = ("Запускаем игру через Mod Organizer 2…", "Starting the game via Mod Organizer 2…"),
        ["home.play.error"] = ("Не удалось запустить MO2: {0}", "Could not start MO2: {0}"),

        ["setup.title"] = ("Установка {0}", "Install {0}"),
        ["setup.installed"] = ("Сборка уже установлена. Можно проверить файлы или поставить заново.", "The modlist is already installed. You can verify the files or reinstall."),
        ["setup.verify"] = ("ПРОВЕРИТЬ ФАЙЛЫ", "VERIFY FILES"),
        ["setup.reinstall"] = ("Переустановить", "Reinstall"),
        ["setup.subtitle"] = ("Сборка ставится в отдельную папку. Игра в Steam / GOG / Epic не меняется.", "The modlist goes into its own folder. Your Steam / GOG / Epic game is not changed."),
        ["setup.check"] = ("Проверка компьютера", "Computer check"),
        ["setup.recheck"] = ("Проверить ещё раз", "Check again"),
        ["setup.folder"] = ("Папка сборки", "Modlist folder"),
        ["setup.browse"] = ("Изменить…", "Change…"),
        ["setup.downloads"] = ("Архивы модов сохранятся в: {0}", "Mod archives will be saved to: {0}"),
        ["setup.install"] = ("УСТАНОВИТЬ", "INSTALL"),
        ["setup.cancel"] = ("Остановить", "Stop"),
        ["setup.nexus.login"] = ("Войти в Nexus (откроется Wabbajack)", "Log in to Nexus (opens Wabbajack)"),

        ["check.running"] = ("Проверяем…", "Checking…"),
        ["check.game.ok"] = ("{0} найдена", "{0} found"),
        ["check.game.no"] = ("{0} не найдена", "{0} not found"),
        ["check.game.hint"] = ("Установите игру в Steam, GOG или Epic и запустите её хотя бы раз. Для Epic: перезапустите Epic Games Launcher.", "Install the game in Steam, GOG or Epic and launch it once. For Epic: restart the Epic Games Launcher."),
        ["check.version.ok"] = ("Версия игры {0}", "Game version {0}"),
        ["check.version.no"] = ("Версия игры {0} — нужна {1}", "Game version {0} — {1} is required"),
        ["check.version.hint"] = ("Обновите игру в лаунчере магазина.", "Update the game in your store launcher."),
        ["check.dlc.ok"] = ("{0} установлено", "{0} installed"),
        ["check.dlc.no"] = ("{0} не найдено", "{0} not found"),
        ["check.dlc.hint"] = ("Дополнение обязательно для сборки.", "The expansion is required for this modlist."),
        ["check.disk.ok"] = ("Свободно {0} ГБ на диске {1}", "{0} GB free on drive {1}"),
        ["check.disk.no"] = ("Мало места: свободно {0} ГБ на диске {1}, нужно около {2} ГБ", "Low space: {0} GB free on drive {1}, about {2} GB needed"),
        ["check.disk.hint"] = ("Освободите место или выберите другую папку ниже.", "Free some space or choose another folder below."),
        ["check.folder.bad"] = ("Эта папка не подходит", "This folder is not suitable"),
        ["check.folder.hint"] = ("Не ставьте сборку в Program Files, на Рабочий стол, в OneDrive или в папку игры. Лучше короткий путь, например C:\\Games\\NeonNights.", "Do not install into Program Files, Desktop, OneDrive or the game folder. Use a short path like C:\\Games\\NeonNights."),
        ["check.folder.ok"] = ("Папка подходит", "Folder is fine"),
        ["check.wj.ok"] = ("Wabbajack готов ({0})", "Wabbajack ready ({0})"),
        ["check.wj.no"] = ("Wabbajack будет скачан с официального GitHub", "Wabbajack will be downloaded from the official GitHub"),
        ["check.nexus.ok"] = ("Вход в Nexus выполнен", "Logged in to Nexus"),
        ["check.nexus.no"] = ("Нужен вход в Nexus", "Nexus login required"),
        ["check.nexus.hint"] = ("Нажмите «Войти в Nexus»: откроется Wabbajack → Settings → Nexus Mods → Log in. Потом закройте Wabbajack.", "Click “Log in to Nexus”: Wabbajack opens → Settings → Nexus Mods → Log in. Then close Wabbajack."),
        ["check.modlist.ok"] = ("Файл сборки найден", "Modlist file found"),
        ["check.modlist.no"] = ("Файл сборки не найден", "Modlist file not found"),
        ["check.modlist.download"] = ("Файл сборки будет скачан при установке", "The modlist file will be downloaded during install"),
        ["check.modlist.hint"] = ("Положите файл .wabbajack рядом с лаунчером или укажите его в настройках.", "Put the .wabbajack file next to the launcher or set it in Settings."),

        ["inst.preparing"] = ("Готовимся…", "Preparing…"),
        ["inst.wj.download"] = ("Скачиваем Wabbajack… {0}%", "Downloading Wabbajack… {0}%"),
        ["inst.wj.extract"] = ("Распаковываем Wabbajack…", "Unpacking Wabbajack…"),
        ["inst.running"] = ("Устанавливаем сборку", "Installing the modlist"),
        ["inst.list.download"] = ("Скачиваем файл сборки… {0}%", "Downloading the modlist file… {0}%"),
        ["inst.verifying"] = ("Проверяем файлы сборки…", "Verifying modlist files…"),
        ["inst.verify.ok"] = ("Файлы в порядке — всё совпадает с оригиналом.", "Files are OK — everything matches the original."),
        ["inst.verify.ok.ignored"] = ("Файлы в порядке. Изменились только файлы, которые моды сами пишут во время игры ({0} шт.) — это нормально.", "Files are OK. Only files that mods write while you play have changed ({0}) — that is normal."),
        ["inst.verify.fail"] = ("Найдены повреждённые или изменённые файлы: {0}. Нажмите «Переустановить», чтобы исправить.", "Damaged or changed files found: {0}. Click “Reinstall” to fix."),
        ["inst.downloaded"] = ("Скачано модов: {0} из {1}", "Mods downloaded: {0} of {1}"),
        ["inst.done"] = ("Готово! Сборка установлена. Жмите ИГРАТЬ в библиотеке.", "Done! The modlist is installed. Press PLAY in the Library."),
        ["inst.manual"] = ("Осталось скачать вручную: {0} шт. Нажмите «УСТАНОВИТЬ», чтобы продолжить с того же места.", "Still to download by hand: {0}. Click “INSTALL” to continue where you left off."),
        ["inst.failed"] = ("Установка не удалась (код {0}). Подробности — в журнале ниже.", "Install failed (code {0}). See the log below."),
        ["inst.stopped"] = ("Остановлено. При следующем запуске установка продолжится.", "Stopped. Next time the install continues where it left off."),
        ["inst.continue"] = ("Можно закрыть лаунчер — при следующем запуске установка продолжится с того же места.", "You can close the launcher — next time the install continues where it left off."),

        ["manual.title"] = ("Ручная загрузка модов", "Manual mod download"),
        ["manual.hint"] = ("Бесплатный аккаунт Nexus: нажмите «Slow download» на странице справа. Файл сохранится сам, и откроется следующий мод. Если Nexus попросит войти — войдите один раз прямо здесь.", "Free Nexus account: click “Slow download” on the page on the right. The file is saved automatically and the next mod opens. If Nexus asks you to log in, log in once right here."),
        ["manual.progress"] = ("Скачано {0} из {1}", "Downloaded {0} of {1}"),
        ["manual.queue"] = ("Очередь", "Queue"),
        ["manual.saving"] = ("Сохраняем: {0}… {1}%", "Saving: {0}… {1}%"),
        ["manual.wrongsize"] = ("Скачался не тот файл ({0}). Нажмите «Slow download» у нужного файла ещё раз.", "Wrong file downloaded ({0}). Click “Slow download” for the right file again."),
        ["manual.done"] = ("Все файлы скачаны! Продолжаем установку…", "All files downloaded! Continuing the install…"),
        ["manual.skip"] = ("Пропустить", "Skip"),
        ["manual.close"] = ("Продолжить позже", "Continue later"),
        ["manual.tired"] = ("Устали? Закройте окно — продолжим с этого места. С Nexus Premium всё скачается само.", "Tired? Close the window — we continue from here. With Nexus Premium everything downloads by itself."),
        ["manual.nowebview"] = ("Не удалось открыть встроенный браузер (WebView2): {0}", "Could not open the built-in browser (WebView2): {0}"),

        ["settings.title"] = ("Настройки", "Settings"),
        ["settings.lang"] = ("Язык", "Language"),
        ["settings.modlist"] = ("Файл сборки (.wabbajack)", "Modlist file (.wabbajack)"),
        ["settings.about"] = ("О программе", "About"),
        ["settings.about.text"] = ("Funky Launcher {0} — открытый код, лицензия GPL-3.0. Моды скачиваются с Nexus Mods через официальный Wabbajack. Каталог сборок: {1}.", "Funky Launcher {0} — open source, GPL-3.0 license. Mods are downloaded from Nexus Mods through the official Wabbajack. Modlist catalog: {1}."),
        ["settings.modlist.for"] = ("Файл сборки {0} (.wabbajack)", "Modlist file for {0} (.wabbajack)"),
    };
}
