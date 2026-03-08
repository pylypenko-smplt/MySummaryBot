using System.Web;
using Telegram.Bot.Types.Enums;
using TelegramMessage = Telegram.Bot.Types.Message;

public static class UrlHelper
{
    public static string? ExtractFirstUrl(TelegramMessage message)
    {
        var entities = message.Entities ?? message.CaptionEntities;
        if (entities == null)
            return null;

        var text = message.Text ?? message.Caption;
        foreach (var e in entities)
        {
            if (e.Type == MessageEntityType.Url && text != null)
                return text.Substring(e.Offset, e.Length);
            if (e.Type == MessageEntityType.TextLink && e.Url != null)
                return e.Url;
        }

        return null;
    }

    // Domains where query params are tracking garbage and should be stripped
    static readonly HashSet<string> _stripQueryDomains =
    [
        "instagram.com", "www.instagram.com",
        "facebook.com", "www.facebook.com", "fb.com",
        "tiktok.com", "www.tiktok.com", "vm.tiktok.com",
        "twitter.com", "www.twitter.com", "x.com", "www.x.com",
    ];

    public static string Normalize(string rawUrl)
    {
        try
        {
            var uri = new Uri(rawUrl);
            var host = uri.Host.ToLower();
            var path = uri.AbsolutePath.TrimEnd('/');

            if (_stripQueryDomains.Contains(host))
                return host + path;

            return string.IsNullOrEmpty(uri.Query)
                ? host + path
                : host + path + uri.Query;
        }
        catch
        {
            return rawUrl;
        }
    }

    public static bool IsSkippedDomain(string url)
    {
        try
        {
            var host = new Uri(url).Host.ToLower();
            return host.Contains("t.me") || host.Contains("twitter.com") || host.Contains("x.com");
        }
        catch
        {
            return false;
        }
    }
}
