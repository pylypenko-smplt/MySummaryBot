using System.Text.Json;
using System.Text.Json.Serialization;

public class ImageSearchService(HttpClient httpClient)
{
    internal async Task<List<ImageResult>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var q = Uri.EscapeDataString(query);
        var url = $"https://api.search.brave.com/res/v1/images/search?q={q}&count=20&safesearch=off";

        var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync(ct);
        return (JsonSerializer.Deserialize<BraveImageResponse>(raw)?.Results ?? [])
            .Where(r => !string.IsNullOrEmpty(r.ImageUrl) && !string.IsNullOrEmpty(r.ThumbnailUrl)
                        && r.ImageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    class BraveImageResponse
    {
        [JsonPropertyName("results")]
        public List<ImageResult> Results { get; set; } = [];
    }

    internal class ImageResult
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("properties")]
        public ImageProperties? Properties { get; set; }

        [JsonPropertyName("thumbnail")]
        public ImageThumbnail? Thumbnail { get; set; }

        public string ImageUrl => Properties?.Url ?? "";
        public string ThumbnailUrl => Thumbnail?.Src ?? "";
        public int Width => Properties?.Width ?? 0;
        public int Height => Properties?.Height ?? 0;
    }

    internal class ImageProperties
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    internal class ImageThumbnail
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";
    }
}
