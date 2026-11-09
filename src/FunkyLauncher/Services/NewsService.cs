// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

public sealed record NewsPost(DateTimeOffset? Date, string Text, string Url);

/// <summary>Reads the latest posts from the public web page of the Telegram channel (t.me/s/...).</summary>
public static partial class NewsService
{
    [GeneratedRegex("<div class=\"tgme_widget_message_wrap.*?data-post=\"(?<post>[^\"]+)\"(?<body>.*?)(?=<div class=\"tgme_widget_message_wrap|$)", RegexOptions.Singleline)]
    private static partial Regex PostBlock();

    [GeneratedRegex("<div class=\"tgme_widget_message_text[^>]*>(?<text>.*?)</div>", RegexOptions.Singleline)]
    private static partial Regex TextBlock();

    [GeneratedRegex("<time datetime=\"(?<dt>[^\"]+)\"")]
    private static partial Regex TimeTag();

    [GeneratedRegex("<br\\s*/?>")]
    private static partial Regex Br();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    public static async Task<List<NewsPost>> LoadAsync(int count, CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 FunkyLauncher");
        var html = await http.GetStringAsync("https://t.me/s/" + AppConfig.TelegramChannel, token);

        var posts = new List<NewsPost>();
        foreach (Match block in PostBlock().Matches(html))
        {
            var body = block.Groups["body"].Value;
            if (block.Value.Contains("service_message")) continue; // "Channel created", pinned, etc.
            var t = TextBlock().Match(body);
            if (!t.Success) continue;
            var text = WebUtility.HtmlDecode(Tag().Replace(Br().Replace(t.Groups["text"].Value, "\n"), "")).Trim();
            if (text.Length == 0) continue;
            if (text.Length > 220) text = text[..220].TrimEnd() + "…";
            var dt = TimeTag().Match(body);
            DateTimeOffset? date = dt.Success && DateTimeOffset.TryParse(dt.Groups["dt"].Value, out var d) ? d : null;
            posts.Add(new NewsPost(date, text, "https://t.me/" + block.Groups["post"].Value));
        }
        // the page lists oldest first
        return posts.AsEnumerable().Reverse().Take(count).ToList();
    }
}
