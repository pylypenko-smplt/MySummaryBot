using System.Net.Http.Headers;

public class TranscriptionService(HttpClient httpClient)
{
    // Ліміт OpenAI Audio API на розмір файлу.
    public const long MaxFileSizeBytes = 25 * 1024 * 1024;

    public async Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken)
    {
        if (audio.Length > MaxFileSizeBytes)
        {
            Console.WriteLine($"[Transcribe Error] File too large: {audio.Length} bytes (limit {MaxFileSizeBytes})");
            return null;
        }

        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(audio);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent("whisper-1"), "model");
            content.Add(new StringContent("ru"), "language");
            content.Add(new StringContent("text"), "response_format");

            using var response = await httpClient.PostAsync(
                "https://api.openai.com/v1/audio/transcriptions", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"[Transcribe Error] {response.StatusCode}: {error}");
                return null;
            }

            var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Console.WriteLine($"[Transcribe Error] {ex.Message}");
            return null;
        }
    }
}
