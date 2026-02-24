using System.Text;
using System.Text.Json;

public class AiService(HttpClient httpClient)
{
    const string SystemPrompt =
        "You are a revverb chat helper. You always respond in Ukrainian language.";

    const string DefaultSummaryPrompt =
        "Summarize the conversation in bullet points, focusing only on key topics, main ideas, and important decisions or agreements. " +
        "Do not list each message separately. " +
        "Refer to people as Пан or Пані. " +
        "Ignore tone or emotions; focus on content.";

    const string DefaultRespectPrompt =
        "Evaluate the vibe level (good vibes = high повага, bad vibes = low повага) in the chat on a 0–10 scale. " +
        "First provide the overall score, then list each user sorted by score (descending). " +
        "Format strictly as: Пан/Пані/Паніні Name (username): score, short reasoning. " +
        "Use Пан for men, Пані for women, and Паніні if gender is unknown. " +
        "Obscene words and playful teasing are normal in informal chats and do not automatically reduce повага. " +
        "Explanations must be short, factual, and per-user only — no general commentary or meta-analysis.";

    const string DefaultAnswerPrompt =
        "Answer directly and concisely, without extra explanations or disclaimers. " +
        "If unknown—say so, no speculation. " +
        "Address everyone as Пан or Пані.";

    public const string DefaultModel = "gpt-5-mini";

    public string SummaryPrompt { get; set; } = DefaultSummaryPrompt;
    public string RespectPrompt { get; set; } = DefaultRespectPrompt;
    public string AnswerPrompt { get; set; } = DefaultAnswerPrompt;
    public string Model { get; set; } = DefaultModel;

    public void ResetSummaryPrompt() => SummaryPrompt = DefaultSummaryPrompt;
    public void ResetRespectPrompt() => RespectPrompt = DefaultRespectPrompt;
    public void ResetAnswerPrompt() => AnswerPrompt = DefaultAnswerPrompt;
    public void ResetModel() => Model = DefaultModel;

    public async Task<string> GetRespectLevel(List<MessageModel> messagesForRespect)
    {
        var formattedMessages = JsonSerializer.Serialize(messagesForRespect);
        const int maxTokens = 4096;

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = RespectPrompt +
                              $"\nMessages:\n{formattedMessages}"
                }
            },
            max_completion_tokens = maxTokens
        };

        return await MakeApiRequest(requestBody);
    }

    public async Task<string> GetSummary(List<MessageModel> messagesForSummary, Func<string, Task>? onProgress = null)
    {
        if (messagesForSummary.Count == 0)
            return "Немає повідомлень для підсумку.";

        const int chunkSize = 200;
        var ordered = messagesForSummary.OrderBy(m => m.Timestamp).ToList();

        var chunks = ordered
            .Select((m, i) => new { Message = m, Index = i })
            .GroupBy(x => x.Index / chunkSize)
            .Select(g => g.Select(x => x.Message).ToList())
            .ToList();

        if (chunks.Count == 1)
            return await GetSummaryHour(chunks[0]);

        var chunkSummaries = new List<string>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (onProgress != null)
                await onProgress($"📖 Обробляю частину {i + 1}/{chunks.Count}...");

            var timeRange = $"{chunk[0].Timestamp:HH:mm}–{chunk[^1].Timestamp:HH:mm}";
            var summary = await GetChunkSummary(chunk);
            chunkSummaries.Add($"[{timeRange}, {chunk.Count} повідомлень]\n{summary}");
            await Task.Delay(1000);
        }

        if (onProgress != null)
            await onProgress("✍️ Формую фінальний підсумок...");

        return await MergeSummaries(chunkSummaries, messagesForSummary.Count);
    }

    public async Task<string> GetSummaryHour(List<MessageModel> msgs, string? promptOverride = null)
    {
        var formattedMessages = JsonSerializer.Serialize(msgs);
        const int maxTokens = 4096;

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = (promptOverride ?? SummaryPrompt) +
                              $"\nMessages:\n{formattedMessages}"
                }
            },
            max_completion_tokens = maxTokens
        };

        return await MakeApiRequest(requestBody);
    }

    public async Task<string> GetAnswer(MessageModel message, MessageModel? replyMessage = null)
    {
        const int maxTokens = 4096;
        const string smartModel = "gpt-5.2";

        var shortContext = new StringBuilder();
        shortContext.AppendLine($"Author: {message.FirstName}");
        shortContext.AppendLine("Message:");
        shortContext.AppendLine(message.Text);

        if (replyMessage != null)
        {
            shortContext.AppendLine();
            shortContext.AppendLine($"Reply to: {replyMessage.FirstName ?? "Unknown"}");
            shortContext.AppendLine("Reply context:");
            shortContext.AppendLine(replyMessage.Text ?? string.Empty);
        }

        var requestBody = new
        {
            model = smartModel,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content = AnswerPrompt + "\n\n" + shortContext
                }
            },
            max_completion_tokens = maxTokens
        };

        return await MakeApiRequest(requestBody);
    }

    async Task<string> GetChunkSummary(List<MessageModel> chunk)
    {
        var formattedMessages = JsonSerializer.Serialize(chunk);
        const int maxTokens = 2048;

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content =
                        "Extract the key topics, decisions, and notable events from these messages. " +
                        "Be brief — maximum 10 bullet points. Refer to people as Пан or Пані. " +
                        $"Messages:\n{formattedMessages}"
                }
            },
            max_completion_tokens = maxTokens
        };

        return await MakeApiRequest(requestBody, appendCost: false);
    }

    async Task<string> MergeSummaries(List<string> chunkSummaries, int totalMessages)
    {
        var combined = string.Join("\n\n", chunkSummaries);
        const int maxTokens = 4096;

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt },
                new
                {
                    role = "user",
                    content =
                        SummaryPrompt +
                        $"\n\nBelow are summaries of different time periods from a chat ({totalMessages} messages total). " +
                        "Combine them into a single compact summary. " +
                        "First list the main topics of the day, then briefly note key events in chronological order. " +
                        "Keep the total response under 2000 characters. " +
                        $"Summaries:\n{combined}"
                }
            },
            max_completion_tokens = maxTokens
        };

        return await MakeApiRequest(requestBody);
    }

    async Task<string> MakeApiRequest(object request, bool appendCost = true)
    {
        var response = await httpClient.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error API: {response.StatusCode}, Details: {errorContent}");
        }

        var rawResponse = await response.Content.ReadAsStringAsync();
        var completion = JsonSerializer.Deserialize<OpenAiCompletionResponse>(rawResponse);
        var resp = completion?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(resp))
            throw new Exception("Empty response from OpenAI API. Raw response: " + rawResponse);

        var (inputPricePerToken, outputPricePerToken) = completion?.Model switch
        {
            "gpt-5" or "gpt-5.1" or "gpt-5.2" => (0.00000125m, 0.00001m),
            "gpt-5-mini" => (0.00000025m, 0.000002m),
            "gpt-5-nano" => (0.00000005m, 0.0000004m),
            "gpt-4.1" or "gpt-4o" => (0.0000025m, 0.00001m),
            "gpt-4.1-mini" or "gpt-4o-mini" => (0.0000003m, 0.0000012m),
            _ => (0.00000125m, 0.00001m)
        };

        var promptTokens = completion?.Usage?.PromptTokens ?? 0;
        var completionTokens = completion?.Usage?.CompletionTokens ?? 0;

        if (appendCost)
        {
            var costUsd = promptTokens * inputPricePerToken + completionTokens * outputPricePerToken;
            var costUah = costUsd * 44.50m;
            resp += $"\n\n*Витрачено: {costUah:F2} грн*";
        }

        return resp;
    }
}
