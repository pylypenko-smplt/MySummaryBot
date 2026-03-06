using System.Text.RegularExpressions;

public static class OgFetcher
{
    static readonly Regex[] Patterns =
    [
        new(@"<meta[^>]+property=[""']og:title[""'][^>]+content=[""']([^""']+)[""']", RegexOptions.IgnoreCase),
        new(@"<meta[^>]+content=[""']([^""']+)[""'][^>]+property=[""']og:title[""']", RegexOptions.IgnoreCase)
    ];

    public static async Task<string?> FetchTitle(string url, HttpClient http)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[65536];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            var head = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            foreach (var pattern in Patterns)
            {
                var match = pattern.Match(head);
                if (match.Success)
                    return match.Groups[1].Value;
            }
        }
        catch
        {
            // best-effort
        }

        return null;
    }
}
