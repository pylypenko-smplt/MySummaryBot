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

    // Tracking params stripped from all URLs universally
    static readonly HashSet<string> _trackingParams =
    [
        // UTM
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        // auto.ria and similar r_* params
        "r_source", "r_medium", "r_campaign", "r_audience",
        // Meta / Facebook
        "fbclid", "fb_action_ids", "fb_action_types",
        // Instagram
        "igsh", "igshid",
        // Google
        "gclid", "gclsrc", "dclid",
        // TikTok
        "_t", "_r",
        // Other common trackers
        "ref", "referrer", "source",
    ];

    public static string Normalize(string rawUrl)
    {
        try
        {
            var uri = new Uri(rawUrl);
            var host = uri.Host.ToLower();
            var path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(uri.Query))
                return host + path;

            var qs = HttpUtility.ParseQueryString(uri.Query);
            foreach (var key in _trackingParams)
                qs.Remove(key);

            return qs.Count == 0
                ? host + path
                : host + path + "?" + qs;
        }
        catch
        {
            return rawUrl;
        }
    }

    public static bool IsInviteLink(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host.ToLower().Contains("t.me") && uri.AbsolutePath.StartsWith("/+");
        }
        catch
        {
            return false;
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
