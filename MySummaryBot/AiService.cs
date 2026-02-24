using System.Text;
using System.Text.Json;

public class AiService(HttpClient httpClient)
{
    const string SystemPrompt =
        "You are a witty Ukrainian-speaking chat assistant for the revverb community — a friend group. " +
        "You're casual, concise, and have a dry sense of humor. " +
        "Always respond in Ukrainian. Address people as Пан or Пані.";

    const string DefaultSummaryPrompt =
        "Your task: summarize a group chat conversation.\n" +
        "- Use bullet points grouped by topic, not chronologically by message\n" +
        "- Focus on key topics, decisions, and notable events\n" +
        "- Keep it concise — skip greetings, small talk, and reactions\n" +
        "- If someone made a memorable joke or statement, briefly note it";

    const string DefaultRespectPrompt =
        "Your task: evaluate the vibe level (повага) of each chat participant on a 0–10 scale.\n\n" +
        "Rules:\n" +
        "- Good vibes, helpfulness, constructive discussion = high повага\n" +
        "- Negativity, trolling, toxicity = low повага\n" +
        "- Obscene words and playful teasing are normal in this informal chat — don't penalize them\n" +
        "- Use Пан for men, Пані for women, Паніні if gender is unknown\n\n" +
        "Output format:\n" +
        "1. Overall chat vibe: X/10\n" +
        "2. Per-user list sorted by score (descending):\n" +
        "   Пан Олексій (@alex): 8/10 — допомагав з кодом, жартував\n" +
        "   Пані Марія (@maria): 6/10 — мало писала, але по справі\n\n" +
        "Keep reasoning short, factual, per-user only. No general commentary or meta-analysis.";

    const string DefaultAnswerPrompt =
        "Your task: answer a question from the chat.\n" +
        "- Be direct and concise — no disclaimers or preambles\n" +
        "- If you don't know, say so honestly — no speculation\n" +
        "- If reply context is provided, use it to understand what the question is about\n" +
        "- Match the casual tone of a friend group chat";

    const string ChunkSummaryPrompt =
        "Your task: extract key points from a portion of a group chat.\n" +
        "- Maximum 10 bullet points\n" +
        "- Focus on topics, decisions, and notable events\n" +
        "- Refer to people as Пан or Пані";

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

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + RespectPrompt },
                new { role = "user", content = $"Messages:\n{formattedMessages}" }
            },
            max_completion_tokens = 4096
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

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + (promptOverride ?? SummaryPrompt) },
                new { role = "user", content = $"Messages:\n{formattedMessages}" }
            },
            max_completion_tokens = 4096
        };

        return await MakeApiRequest(requestBody);
    }

    public async Task<string> GetAnswer(MessageModel message, MessageModel? replyMessage = null)
    {
        const string smartModel = "gpt-5.2";

        var userContent = new StringBuilder();
        userContent.AppendLine($"Author: {message.FirstName}");
        userContent.AppendLine(message.Text);

        if (replyMessage != null)
        {
            userContent.AppendLine();
            userContent.AppendLine($"Reply context from {replyMessage.FirstName ?? "Unknown"}:");
            userContent.AppendLine(replyMessage.Text ?? string.Empty);
        }

        var requestBody = new
        {
            model = smartModel,
            temperature = 0.3,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + AnswerPrompt },
                new { role = "user", content = userContent.ToString() }
            },
            max_completion_tokens = 4096
        };

        return await MakeApiRequest(requestBody);
    }

    async Task<string> GetChunkSummary(List<MessageModel> chunk)
    {
        var formattedMessages = JsonSerializer.Serialize(chunk);

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + ChunkSummaryPrompt },
                new { role = "user", content = $"Messages:\n{formattedMessages}" }
            },
            max_completion_tokens = 2048
        };

        return await MakeApiRequest(requestBody, appendCost: false);
    }

    async Task<string> MergeSummaries(List<string> chunkSummaries, int totalMessages)
    {
        var combined = string.Join("\n\n", chunkSummaries);

        var mergeInstruction =
            SummaryPrompt +
            "\n\nYou are given partial summaries from different time periods. " +
            "Combine them into a single compact summary. " +
            "First list the main topics, then briefly note key events in chronological order. " +
            "Keep the total response under 2000 characters.";

        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + mergeInstruction },
                new { role = "user", content = $"Chat had {totalMessages} messages total.\n\nSummaries:\n{combined}" }
            },
            max_completion_tokens = 4096
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
