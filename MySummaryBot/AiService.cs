using System.Text;
using System.Text.Json;

public class AiService(HttpClient httpClient)
{
    // Models: summary tasks use fast non-reasoning model; analysis uses reasoning model
    public const string DefaultModel = "gpt-4.1-mini";
    const string AnalysisModel = "gpt-5-mini";
    const string SmartModel = "gpt-5.2";

    const string SystemPrompt =
        "You are a witty Ukrainian-speaking assistant for the revverb friend group chat. " +
        "Be casual, sharp, and concise. Always respond in Ukrainian. " +
        "Use Пан/Пані when addressing people. Skip formalities — this is an informal chat among friends.";

    const string DefaultSummaryPrompt =
        "Summarize this group chat. Use HTML formatting. Be concise.\n" +
        "- Group by topic (not chronologically)\n" +
        "- Each topic header: <b>topic name</b> [msg:ID] — where ID is from the first message of that topic\n" +
        "- Then bullet points (•) underneath the header\n" +
        "- Skip greetings, reactions, one-word replies\n" +
        "- Note decisions, plans, links shared, and memorable moments\n" +
        "- If the chat was quiet or boring, say so in one line\n" +
        "- Do NOT add a concluding paragraph or general wrap-up at the end\n" +
        "- Use only these HTML tags: <b>, <i>, <code>. Do NOT use <a> tags.\n" +
        "- Message format in input: [HH:mm|ID] name: text — use the ID number for [msg:ID]";

    const string DefaultRespectPrompt =
        "Your task: evaluate the вайб (повага) of each chat participant on a 0–10 scale.\n\n" +
        "Rules:\n" +
        "- Good vibes, helpfulness, constructive discussion = high повага\n" +
        "- Negativity, trolling, toxicity = low повага\n" +
        "- Obscene words and playful teasing are normal in this chat — don't penalize them\n" +
        "- Use Пан for men, Пані for women, Паніні if gender is unknown\n\n" +
        "Output format:\n" +
        "1. Overall chat vibe: X/10\n" +
        "2. Per-user list sorted by score (descending):\n" +
        "   Пан Олексій (alex): 8/10 — допомагав з кодом, жартував\n" +
        "   Пані Марія (maria): 6/10 — мало писала, але по справі\n\n" +
        "Keep reasoning short, factual, per-user only. No general commentary.";

    const string DefaultAnswerPrompt =
        "Answer a question from the chat.\n" +
        "- Be direct and concise — no disclaimers or preambles\n" +
        "- If you don't know, say so honestly\n" +
        "- If reply context is provided, use it\n" +
        "- Match the casual tone of a friend group chat";

    const string ChunkSummaryPrompt =
        "Extract key points from this chat segment. Max 8 bullets.\n" +
        "Focus on: topics discussed, decisions made, links/resources shared.\n" +
        "Skip small talk and reactions.\n" +
        "Message format in input: [HH:mm|ID] name: text\n" +
        "For each bullet, append [msg:ID] using the ID of the first relevant message.";

    const string DefaultDigestPrompt =
        "Write a short casual note about what this person was up to in the chat today. " +
        "What did they talk about, who did they interact with, any memorable moments? " +
        "Keep it under 150 words, no bullet points or headers, refer to them by first name.";

    const string HoroscopePrompt =
        "Ти пишеш щоденний автомобільний гороскоп для закритого чату ентузіастів \"revverb\" у Telegram. " +
        "Це не астрологія — це стьоб, замаскований під гороскоп. " +
        "Тон: як друг, який підйобує тебе на міті, але по-доброму.\n\n" +
        "Формат:\n" +
        "- Перший рядок — одне речення-інтро про загальний вайб дня. Без \"доброго ранку\", без привітань.\n" +
        "- Потім 12 знаків зодіаку по порядку (Овен...Риби), кожен з нового рядка.\n" +
        "- Формат знаку: <b>Назва emoji</b> — текст (2-3 речення).\n" +
        "- HTML теги: тільки <b> та <i>. Ніяких <a>.\n" +
        "- Після Риб — НІЧОГО. Ні висновків, ні побажань, ні підпису.\n\n" +
        "Стиль:\n" +
        "- Українська, неформальна, жаргон ентузіастів.\n" +
        "- Суржик та англійські авто-терміни там, де це природно (stage 2, blow-off, стенс, фітмент, даунпайп, вейстгейт, LSD, EGR).\n" +
        "- Можна \"дічь\", \"жесть\", \"капець\", але без мату.\n" +
        "- Пиши так, ніби це повідомлення в чат від свого, а не стаття з автожурналу.\n\n" +
        "Різноманітність структури — ОБОВ'ЯЗКОВО:\n" +
        "Для кожного знаку обирай ІНШИЙ тип подачі. Не використовуй один тип більше 2 разів на 12 знаків:\n" +
        "1. Порівняння (\"Ти сьогодні як дизель взимку — довго розкачуєшся...\")\n" +
        "2. Ситуація (\"Сьогодні на заправці хтось спитає тебе про витрату...\")\n" +
        "3. Порада (\"Не чіпай сьогодні налаштування — воно працює, не псуй\")\n" +
        "4. Попередження (\"ECU каже: ...\")\n" +
        "5. Діагноз (\"Симптоми: ... Діагноз: ...\")\n" +
        "6. Констатація (\"Твій ШРУС протримається. Твоє терпіння — ні.\")\n" +
        "7. Риторичне питання (\"Пам'ятаєш ту вібрацію на 120?.. Ось вона повертається.\")\n\n" +
        "Розподіл настрою серед 12 знаків:\n" +
        "- 3-4 знаки: поганий день (поломка, штраф, евакуатор, фейл). БЕЗ пом'якшень типу \"але все буде добре\".\n" +
        "- 4-5 знаків: нейтральний або іронічний день.\n" +
        "- 3-4 знаки: реально хороший день.\n\n" +
        "ЗАБОРОНЕНО:\n" +
        "- \"Зірки кажуть / шепочуть / вирівняються / сприяють\" та будь-яка езотерична лексика\n" +
        "- Мотиваційні фрази (\"удача на твоєму боці\", \"день сповнений можливостей\")\n" +
        "- \"Будь обережний на дорозі\" та подібні банальності\n" +
        "- Повторювати один авто-термін для різних знаків\n" +
        "- Писати всі 12 знаків у форматі \"Ти як X — робиш Y, але Z\"\n" +
        "- Загальні поради (\"перевір масло\", \"слідкуй за тиском у шинах\")\n" +
        "- Пафос і серйозність\n\n" +
        "Авто-лексика (бери різне для кожного знаку, НЕ використовуй все одразу):\n" +
        "підвіска, турбіна, boost, валити боком, розвал-сходження, діагностика, масложор, колодки, даунпайп, " +
        "чіп-тюнінг, вейстгейт, ковші, стенс, фітмент, рестайлінг, клін-лук, ойл кулер, інтеркулер, blow-off, " +
        "гребінка, лямбда-зонд, свічки, EGR, сайлентблок, ШРУС, двомасовик, коробка, варіатор, LSD, кардан, " +
        "тормозний суппорт, помпа, радіатор, патрубок, хомут, перекупський двіж, адаптація коробки, стейдж\n\n" +
        "Приклади стилю (це зразок тону і різноманітності — НЕ копіюй дослівно, створюй своє):\n\n" +
        "Приклад 1 (поганий день, тип \"порівняння\"):\n" +
        "<b>Овен ♈</b> — Сьогодні ти як турбіна — дуєш, але куди саме їдеш, не дуже ясно. " +
        "Є шанс влупитись у яму, яку ти ж і бачив учора. Притормози, Шумахер.\n\n" +
        "Приклад 2 (нейтральний день, тип \"діагноз\"):\n" +
        "<b>Рак ♋</b> — Симптоми: лінь, бажання все кинути і поїхати на трек. " +
        "Діагноз: <i>нормальний стан для середини тижня</i>. " +
        "Лікування — подивись відео з Нюрбургрінга і заспокойся.\n\n" +
        "Приклад 3 (хороший день, тип \"констатація\"):\n" +
        "<b>Терези ♎</b> — Той рідкісний день, коли все їде рівно: коробка перемикається без пинків, " +
        "підвіска мовчить, навіть у пробці пропустили без морок. " +
        "Насолоджуйся, бо завтра двомасовик нагадає про себе.";

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
        var requestBody = new
        {
            model = AnalysisModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + RespectPrompt },
                new { role = "user", content = $"Messages:\n{FormatMessages(messagesForRespect)}" }
            },
            max_completion_tokens = 8192
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
        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + (promptOverride ?? SummaryPrompt) },
                new { role = "user", content = $"Messages:\n{FormatMessages(msgs)}" }
            },
            max_completion_tokens = 4096
        };

        return await MakeApiRequest(requestBody);
    }

    public async Task<string> GetAnswer(MessageModel message, MessageModel? replyMessage = null)
    {
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
            model = SmartModel,
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

    public async Task<string> GetDigest(List<MessageModel> msgs, string displayName)
    {
        var requestBody = new
        {
            model = AnalysisModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + DefaultDigestPrompt },
                new { role = "user", content = $"User: {displayName}\nMessages:\n{FormatMessages(msgs)}" }
            },
            max_completion_tokens = 8192
        };
        return await MakeApiRequest(requestBody);
    }

    public async Task<string> GetHoroscope()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd, dddd");
        var requestBody = new
        {
            model = DefaultModel,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + HoroscopePrompt },
                new { role = "user", content = $"Дата: {today}. Згенеруй автомобільний гороскоп на сьогодні." }
            },
            max_completion_tokens = 4096
        };
        return await MakeApiRequest(requestBody);
    }

    async Task<string> GetChunkSummary(List<MessageModel> chunk)
    {
        var requestBody = new
        {
            model = Model,
            messages = new[]
            {
                new { role = "system", content = SystemPrompt + "\n\n" + ChunkSummaryPrompt },
                new { role = "user", content = $"Messages:\n{FormatMessages(chunk)}" }
            },
            max_completion_tokens = 4096
        };

        return await MakeApiRequest(requestBody, appendCost: false);
    }

    async Task<string> MergeSummaries(List<string> chunkSummaries, int totalMessages)
    {
        var combined = string.Join("\n\n", chunkSummaries);

        var mergeInstruction =
            SummaryPrompt +
            "\n\nYou are given partial summaries from different time periods. Each bullet may contain [msg:ID] tags — preserve them exactly as-is. " +
            "Combine into a single compact summary grouped by topic. " +
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

    static string FormatMessages(List<MessageModel> msgs)
    {
        var sb = new StringBuilder();
        foreach (var m in msgs)
        {
            var name = m.FirstName ?? m.Username ?? "Unknown";
            var time = m.Timestamp.ToString("HH:mm");

            var line = new StringBuilder($"[{time}|{m.MessageId}] {name}");

            if (m.MediaType != null)
                line.Append($" [{m.MediaType}]");

            if (m.Text != null)
                line.Append($": {m.Text}");

            if (m.LinkPreview != null)
                line.Append($" 🔗 {m.LinkPreview}");

            sb.AppendLine(line.ToString());
        }
        return sb.ToString();
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

        if (appendCost)
        {
            var (inputPrice, outputPrice) = completion?.Model switch
            {
                var m when m?.StartsWith("gpt-5-nano") == true    => (0.00000005m, 0.0000004m),
                var m when m?.StartsWith("gpt-5-mini") == true    => (0.00000025m, 0.000002m),
                var m when m?.StartsWith("gpt-5") == true         => (0.00000125m, 0.00001m),
                var m when m?.StartsWith("gpt-4.1-mini") == true  => (0.0000003m,  0.0000012m),
                var m when m?.StartsWith("gpt-4.1") == true       => (0.0000025m,  0.00001m),
                var m when m?.StartsWith("gpt-4o-mini") == true   => (0.0000003m,  0.0000012m),
                var m when m?.StartsWith("gpt-4o") == true        => (0.0000025m,  0.00001m),
                _                                                  => (0.00000125m, 0.00001m)
            };

            var promptTokens = completion?.Usage?.PromptTokens ?? 0;
            var completionTokens = completion?.Usage?.CompletionTokens ?? 0;
            var costUah = (promptTokens * inputPrice + completionTokens * outputPrice) * 44.50m;
            resp += $"\n\n*Витрачено: {costUah:F2} грн*";
        }

        return resp;
    }
}
