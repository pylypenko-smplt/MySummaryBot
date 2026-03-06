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

    public static string Normalize(string rawUrl)
    {
        try
        {
            var uri = new Uri(rawUrl);
            var host = uri.Host.ToLower();
            var path = uri.AbsolutePath.TrimEnd('/');

            var query = HttpUtility.ParseQueryString(uri.Query);
            foreach (var key in query.AllKeys.Where(k => k != null && k.StartsWith("utm_")).ToList())
                query.Remove(key);

            var normalized = host + path;
            var queryString = query.ToString();
            if (!string.IsNullOrEmpty(queryString))
                normalized += "?" + queryString;

            return normalized;
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
