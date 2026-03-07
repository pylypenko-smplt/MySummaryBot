using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.InlineQueryResults;

public class BotService(TelegramBotClient botClient, AiService ai, MessageStore store, string? adminChatId, HttpClient ogHttpClient, ImageSearchService imageSearch)
{
    readonly Random _rnd = new();

    public async Task RunReceivingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                await SendAdmin("Loop started", token);
                await botClient.DeleteWebhook(true, cancellationToken: token);
                await botClient.ReceiveAsync(
                    HandleUpdateAsync,
                    HandleErrorAsync,
                    cancellationToken: token);
            }
            catch (OperationCanceledException)
            {
                await SendAdmin("Loop stopped");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Receiving Error] {ex.Message}");
                await Task.Delay(5000);
            }
    }

    public async Task RunBackgroundLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
            try
            {
                await Task.Delay(10000, token);
                try
                {
                    store.ClearOld();

                    var utcNow = DateTime.UtcNow;
                    if (utcNow.Hour == 0 && utcNow.Minute == 0)
                    {
                        var today = utcNow.Date.ToString("yyyy-MM-dd");
                        var activeChats = store.GetActiveChats(utcNow.Date);
                        foreach (var digestChatId in activeChats)
                        {
                            if (store.HasDigestBeenSent(digestChatId, today)) continue;
                            store.MarkDigestSent(digestChatId, today);
                            try
                            {
                                var msgs = store.GetMessages(digestChatId, TimeSpan.FromHours(24));
                                if (msgs.Count == 0) continue;
                                var summary = await ai.GetSummary(msgs);
                                await botClient.SendMessage(digestChatId, "Дайджест дня:\n\n" + summary, cancellationToken: token);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[Digest Error] chatId={digestChatId}: {ex.Message}");
                                await SendAdmin($"[Digest Error] chatId={digestChatId}: {ex.Message}", token);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    await SendAdmin($"Error: {e.Message}", token);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Фоновий цикл зупинено.");
                break;
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Loop Error] {e.Message}");
                await SendAdmin($"[Loop Error] {e.Message}", token);
            }
    }

    public async Task SendAdmin(string msg, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(adminChatId))
            await botClient.SendMessage(adminChatId, msg, cancellationToken: ct);
    }

    async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        try
        {
            if (update.InlineQuery != null)
            {
                await HandleInlineQueryAsync(update.InlineQuery, cancellationToken);
                return;
            }

            if (update.Message == null)
                return;

            var messageText = update.Message.Text ?? update.Message.Caption;
            var mediaType = GetMediaType(update.Message);
            if (messageText == null && mediaType == null)
                return;

            var chatId = update.Message.Chat.Id;
            var replyParams = new ReplyParameters { MessageId = update.Message.MessageId };

            if (messageText != null && !messageText.Contains("http") && messageText.Split(' ').Any(t => t.Length > 100))
            {
                await bot.SendMessage(chatId, "Друже, ти дурачок?", replyParameters: replyParams, cancellationToken: cancellationToken);
                return;
            }

            var userName = update.Message.From?.FirstName ?? update.Message.From?.Username;
            var userId = update.Message.From?.Id ?? 0;

            Console.WriteLine($"Сhat id: {chatId}, User: {userName} | {userId}, Message: {messageText}");

            store.EnsureChat(chatId);

            if (update.Message.From?.IsBot == true)
                return;

            var message = new MessageModel
            {
                MessageId = update.Message.MessageId,
                UserId = update.Message.From?.Id ?? 0,
                ChatId = chatId,
                Username = update.Message.From?.Username,
                Language = update.Message.From?.LanguageCode,
                FirstName = userName,
                Timestamp = DateTime.UtcNow,
                Text = messageText,
                ReplyToMessageId = update.Message.ReplyToMessage?.Id
            };

            message.MediaType = mediaType;
            var rawUrl = UrlHelper.ExtractFirstUrl(update.Message);
            message.UrlNormalized = rawUrl != null ? UrlHelper.Normalize(rawUrl) : null;
            if (update.Message.ForwardOrigin is MessageOriginChannel originChannel)
            {
                message.FwdChannelId = originChannel.Chat.Id;
                message.FwdMessageId = originChannel.MessageId;
            }

            var isCommand = update.Message.Text?.StartsWith('/') == true && mediaType == null;
            if (!isCommand)
            {
                store.AddMessage(message);

                var dupCount = 0;
                if (message.UrlNormalized != null)
                    dupCount = store.CountUrlOccurrences(chatId, message.UrlNormalized);
                else if (message.FwdChannelId != null)
                    dupCount = store.CountFwdOccurrences(chatId, message.FwdChannelId.Value, message.FwdMessageId!.Value);
                if (dupCount > 1)
                    await bot.SendMessage(chatId, dupCount.ToString(), replyParameters: replyParams, cancellationToken: cancellationToken);

                if (rawUrl != null && !UrlHelper.IsSkippedDomain(rawUrl))
                    _ = FetchAndStoreLinkPreviewAsync(chatId, message.MessageId, rawUrl);
            }

            if (userId == 5612311136)
                return;

            if (messageText == null)
                return;

            if (_rnd.Next(0, 500) == 0)
                await bot.SendMessage(chatId, "Друже, ти дурачок?", replyParameters: replyParams, cancellationToken: cancellationToken);

            if ((messageText.Contains("twingo", StringComparison.InvariantCultureIgnoreCase) ||
                 messageText.Contains("твінго", StringComparison.InvariantCultureIgnoreCase) ||
                 messageText.Contains("твинго", StringComparison.InvariantCultureIgnoreCase)) &&
                !messageText.Contains("merci", StringComparison.InvariantCultureIgnoreCase))
                await bot.SendMessage(chatId, "MERCI TWINGO", replyParameters: replyParams, cancellationToken: cancellationToken);

            if ((messageText.Contains("lanos", StringComparison.InvariantCultureIgnoreCase) ||
                 messageText.Contains("ланос", StringComparison.InvariantCultureIgnoreCase)) &&
                !messageText.Contains("holy", StringComparison.InvariantCultureIgnoreCase))
                await bot.SendMessage(chatId, "HOLY LANOS", replyParameters: replyParams, cancellationToken: cancellationToken);

            if (messageText.Contains("сенс", StringComparison.InvariantCultureIgnoreCase))
                await bot.SendMessage(chatId, messageText.Replace("сенс", "ланос", StringComparison.InvariantCultureIgnoreCase), replyParameters: replyParams, cancellationToken: cancellationToken);
            if (messageText.Contains("sens", StringComparison.InvariantCultureIgnoreCase))
                await bot.SendMessage(chatId, messageText.Replace("sens", "lanos", StringComparison.InvariantCultureIgnoreCase), replyParameters: replyParams, cancellationToken: cancellationToken);

            if (mediaType != null)
                return;

            if (messageText.StartsWith("/підсумок_день") || messageText.StartsWith("/summary_day"))
            {
                var msgs = store.GetMessages(chatId, TimeSpan.FromDays(1));
                if (msgs.Count == 0)
                {
                    await bot.SendMessage(chatId, "Немає повідомлень за останню добу.", replyParameters: replyParams, cancellationToken: cancellationToken);
                    return;
                }
                await bot.SendMessage(chatId, $"Читаю ваші {msgs.Count} повідомлень, зачекайте трохи...", cancellationToken: cancellationToken);
                try
                {
                    var summary = await ai.GetSummary(msgs,
                        async msg => await bot.SendMessage(chatId, msg, cancellationToken: cancellationToken));
                    if (summary.Length < 4096)
                        await bot.SendMessage(chatId, summary, replyParameters: replyParams, cancellationToken: cancellationToken);
                    else
                    {
                        var parts = summary.Select((x, i) => new { Index = i, Value = x })
                            .GroupBy(x => x.Index / 4000)
                            .Select(g => string.Join("", g.Select(x => x.Value)))
                            .ToList();
                        foreach (var part in parts)
                        {
                            await bot.SendMessage(chatId, part, replyParameters: replyParams, cancellationToken: cancellationToken);
                            await Task.Delay(50, cancellationToken);
                        }
                    }
                }
                catch (Exception)
                {
                    await bot.SendMessage(chatId, "Не вдалося згенерувати підсумок, спробуйте ще раз трохи пізніше", cancellationToken: cancellationToken);
                    throw;
                }
            }
            else if (messageText.StartsWith("/підсумок") || messageText.StartsWith("/summary"))
            {
                var parts = messageText.Split(' ');
                var hours = 1;
                if (parts.Length > 1 && int.TryParse(parts[1], out var n))
                    hours = Math.Clamp(n, 1, 720);
                var msgs = store.GetMessages(chatId, TimeSpan.FromHours(hours));
                if (msgs.Count == 0)
                {
                    await bot.SendMessage(chatId, "Немає повідомлень за вказаний період.", replyParameters: replyParams, cancellationToken: cancellationToken);
                    return;
                }
                var totalCount = msgs.Count;
                if (msgs.Count > 2000) msgs = msgs.TakeLast(2000).ToList();
                await bot.SendMessage(chatId, $"Читаю ваші {totalCount} повідомлень, зачекайте трохи...", cancellationToken: cancellationToken);
                try
                {
                    var prefix = totalCount > 2000 ? $"(показано останні 2000 з {totalCount} повідомлень)\n\n" : "";
                    var summary = hours == 1 ? await ai.GetSummaryHour(msgs) : await ai.GetSummary(msgs);
                    await bot.SendMessage(chatId, prefix + summary, cancellationToken: cancellationToken);
                }
                catch (Exception)
                {
                    await bot.SendMessage(chatId, "Не вдалося згенерувати підсумок, спробуйте ще раз трохи пізніше", cancellationToken: cancellationToken);
                    throw;
                }
            }
            else if (messageText.StartsWith("/stats") || messageText.StartsWith("/статистика"))
            {
                var stats = store.GetStats(chatId, TimeSpan.FromHours(24));
                if (stats.Count == 0)
                {
                    await bot.SendMessage(chatId, "Немає повідомлень за останні 24 години.", cancellationToken: cancellationToken);
                    return;
                }
                var lines = stats.Select((s, i) =>
                {
                    var name = s.FirstName ?? s.Username ?? "Unknown";
                    var handle = s.Username != null ? $" ({s.Username})" : "";
                    return $"{i + 1}. {name}{handle} — {s.Count} повідомлень";
                });
                await bot.SendMessage(chatId, "Активність за 24 години:\n\n" + string.Join("\n", lines), cancellationToken: cancellationToken);
            }
            else if (messageText.StartsWith("/digest") || messageText.StartsWith("/дайджест"))
            {
                var parts = messageText.Split(' ');
                if (parts.Length < 2 || !parts[1].StartsWith("@"))
                {
                    await bot.SendMessage(chatId, "Вкажіть юзернейм: /digest @username", cancellationToken: cancellationToken);
                    return;
                }
                var handle = parts[1].TrimStart('@');
                var foundUserId = store.FindUserId(chatId, handle);
                if (foundUserId == null)
                {
                    await bot.SendMessage(chatId, $"Не знайшов {handle} в чаті за останні 24 години.", cancellationToken: cancellationToken);
                    return;
                }
                var msgs = store.GetUserMessages(chatId, foundUserId.Value, TimeSpan.FromHours(24));
                if (msgs.Count == 0)
                {
                    await bot.SendMessage(chatId, $"Не знайшов повідомлень від {handle} за останні 24 години.", cancellationToken: cancellationToken);
                    return;
                }
                var answer = await ai.GetDigest(msgs, parts[1]);
                await bot.SendMessage(chatId, answer, replyParameters: replyParams, cancellationToken: cancellationToken);
            }
            else if (messageText.StartsWith("/питання") || messageText.StartsWith("/question") || messageText.StartsWith("@revverb_bot"))
            {
                message.Text = message.Text!.Replace("/питання", "").Replace("/question", "").Trim();
                message.Text = message.Text.Replace("@revverb_bot", "").Trim();

                if (string.IsNullOrWhiteSpace(message.Text) && update.Message.ReplyToMessage == null)
                {
                    await bot.SendMessage(chatId, "Напишіть питання після команди, наприклад:\n/питання Що таке Docker?", replyParameters: replyParams);
                    return;
                }

                await bot.SendMessage(chatId, "Хмм...");

                MessageModel? replyMessage = null;
                if (update.Message.ReplyToMessage != null)
                    replyMessage = store.FindMessage(chatId, update.Message.ReplyToMessage.Id);

                try
                {
                    var answer = await ai.GetAnswer(message, replyMessage);
                    await bot.SendMessage(chatId, answer, replyParameters: replyParams);
                }
                catch (Exception)
                {
                    await bot.SendMessage(chatId, "Не вдалося згенерувати відповідь, спробуйте ще раз трохи пізніше");
                    throw;
                }
            }
            else if (messageText.StartsWith("/повага") || messageText.StartsWith("/respect"))
            {
                var msgs = store.GetMessages(chatId, TimeSpan.FromHours(3));
                if (msgs.Count == 0)
                {
                    await bot.SendMessage(chatId, "Немає повідомлень за останні 3 години.", replyParameters: replyParams);
                    return;
                }
                await bot.SendMessage(chatId, "Вимірюю рівень поваги, зачекайте трохи...");
                try
                {
                    var respect = await ai.GetRespectLevel(msgs);
                    await bot.SendMessage(chatId, respect, replyParameters: replyParams);
                }
                catch (Exception)
                {
                    await bot.SendMessage(chatId, "Рівень поваги не виміряно, спробуйте ще раз трохи пізніше");
                    throw;
                }
            }
            else if (messageText.StartsWith("/голосування") || messageText.StartsWith("/vote"))
            {
                if (!await IsUserAdminOrOwnerAsync(bot, chatId, update.Message.From!.Id))
                {
                    await bot.SendMessage(chatId, "Тільки адміни можуть створювати голосування 🙅‍♂️");
                    return;
                }

                var options = new List<InputPollOption>
                {
                    new("сб 14"), new("сб 16"), new("сб 18"), new("сб 20"),
                    new("нд 14"), new("нд 16"), new("нд 18"), new("нд 20"),
                    new(GetRandomEmoji())
                };

                await bot.SendPoll(chatId, "Коли збираємось?", options, false, allowsMultipleAnswers: true);
            }
            else if (messageText.StartsWith("/допомога") || messageText.StartsWith("/help"))
            {
                var helpMessage =
                    "/summary (/підсумок) [N] - підсумок за N годин (за замовч. 1г)\n" +
                    "/summary_day (/підсумок_день) - підсумок за останні 24 години\n" +
                    "/stats (/статистика) - активність учасників за 24 години\n" +
                    "/digest @username (/дайджест) - що написав учасник за 24 години\n" +
                    "/question (/питання) [питання] - відповідь на питання\n" +
                    "  також можна тегнути @revverb_bot з питанням\n" +
                    "/respect (/повага) - виміряти рівень поваги\n" +
                    "/vote (/голосування) - голосування за зустріч (для адмінів)\n" +
                    "/help (/допомога) - показати цей список команд";

                if (chatId.ToString() == adminChatId)
                    helpMessage +=
                        "\n/prompt_summary [prompt] - змінити промпт для підсумки\n" +
                        "/prompt_summary_reset - скинути промпт для підсумки\n" +
                        "/prompt_respect [prompt] - змінити промпт для вимірювання поваги\n" +
                        "/prompt_respect_reset - скинути промпт для вимірювання поваги\n" +
                        "/prompt_answer [prompt] - змінити промпт для відповіді на питання\n" +
                        "/prompt_answer_reset - скинути промпт для відповіді на питання\n" +
                        "/model [model] - змінити модель для генерації тексту\n" +
                        "/model_reset - скинути модель для генерації тексту";

                await bot.SendMessage(chatId, helpMessage);
            }
            else if (chatId.ToString() == adminChatId)
            {
                // _reset variants must be checked before their prefix counterparts
                if (messageText.StartsWith("/prompt_summary_reset"))
                {
                    ai.ResetSummaryPrompt();
                    await bot.SendMessage(chatId, "Prompt reset");
                }
                else if (messageText.StartsWith("/prompt_summary"))
                {
                    var value = messageText.Replace("/prompt_summary", "").Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        await bot.SendMessage(chatId, "Вкажіть промпт після команди, наприклад:\n/prompt_summary Summarize briefly.");
                        return;
                    }
                    ai.SummaryPrompt = value;
                    await bot.SendMessage(chatId, "Prompt updated");
                }
                else if (messageText.StartsWith("/prompt_respect_reset"))
                {
                    ai.ResetRespectPrompt();
                    await bot.SendMessage(chatId, "Prompt reset");
                }
                else if (messageText.StartsWith("/prompt_respect"))
                {
                    var value = messageText.Replace("/prompt_respect", "").Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        await bot.SendMessage(chatId, "Вкажіть промпт після команди, наприклад:\n/prompt_respect Evaluate respect level.");
                        return;
                    }
                    ai.RespectPrompt = value;
                    await bot.SendMessage(chatId, "Prompt updated");
                }
                else if (messageText.StartsWith("/prompt_answer_reset"))
                {
                    ai.ResetAnswerPrompt();
                    await bot.SendMessage(chatId, "Prompt reset");
                }
                else if (messageText.StartsWith("/prompt_answer"))
                {
                    var value = messageText.Replace("/prompt_answer", "").Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        await bot.SendMessage(chatId, "Вкажіть промпт після команди, наприклад:\n/prompt_answer Answer concisely.");
                        return;
                    }
                    ai.AnswerPrompt = value;
                    await bot.SendMessage(chatId, "Prompt updated");
                }
                else if (messageText.StartsWith("/model_reset"))
                {
                    ai.ResetModel();
                    await bot.SendMessage(chatId, "Model reset");
                }
                else if (messageText.StartsWith("/model"))
                {
                    var value = messageText.Replace("/model", "").Trim();
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        await bot.SendMessage(chatId, "Вкажіть модель після команди, наприклад:\n/model gpt-5-mini");
                        return;
                    }
                    ai.Model = value;
                    await bot.SendMessage(chatId, "Model updated");
                }
                else if (messageText.StartsWith("/"))
                {
                    await bot.SendMessage(chatId, "Невідома команда. Напишіть /допомога щоб побачити список доступних команд.", replyParameters: replyParams);
                }
            }
            else if (messageText.StartsWith("/"))
            {
                await bot.SendMessage(chatId, "Невідома команда. Напишіть /допомога щоб побачити список доступних команд.", replyParameters: replyParams);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[HandleUpdateAsync Error] {e.Message}");
            await SendAdmin("Error: " + e.Message);
        }
    }

    async Task HandleInlineQueryAsync(InlineQuery inlineQuery, CancellationToken ct)
    {
        try
        {
            var q = inlineQuery.Query ?? "";
            if (!q.StartsWith("pic ", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.AnswerInlineQuery(inlineQuery.Id, [], cancellationToken: ct);
                return;
            }

            var searchTerm = q["pic ".Length..].Trim();
            if (string.IsNullOrEmpty(searchTerm))
            {
                await botClient.AnswerInlineQuery(inlineQuery.Id, [], cancellationToken: ct);
                return;
            }

            var images = await imageSearch.SearchAsync(searchTerm, ct);
            var results = images.Select((img, i) => new InlineQueryResultPhoto(
                i.ToString(), img.ImageUrl, img.ThumbnailUrl)
            {
                PhotoWidth = img.Width > 0 ? img.Width : null,
                PhotoHeight = img.Height > 0 ? img.Height : null,
                Title = img.Title
            });

            await botClient.AnswerInlineQuery(inlineQuery.Id, results, cacheTime: 300, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Inline query error: {ex.Message}");
            try { await botClient.AnswerInlineQuery(inlineQuery.Id, [], cancellationToken: CancellationToken.None); }
            catch { /* ignore */ }
        }
    }

    async Task HandleErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        var emsg = $"[HandleErrorAsync] {exception.Message}; \n {exception.StackTrace}";
        Console.WriteLine(emsg);
        await SendAdmin(emsg, cancellationToken);
    }

    async Task FetchAndStoreLinkPreviewAsync(long chatId, int msgId, string url)
    {
        try
        {
            var title = await OgFetcher.FetchTitle(url, ogHttpClient);
            if (title != null) store.UpdateLinkPreview(chatId, msgId, title);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OgFetch Error] {ex.Message}");
        }
    }

    static string? GetMediaType(Telegram.Bot.Types.Message msg)
    {
        if (msg.Photo != null) return "photo";
        if (msg.Video != null) return "video";
        if (msg.Sticker != null) return "sticker";
        if (msg.Document != null) return "document";
        if (msg.Voice != null) return "voice";
        return null;
    }

    static async Task<bool> IsUserAdminOrOwnerAsync(ITelegramBotClient bot, long chatId, long userId)
    {
        try
        {
            var admins = await bot.GetChatAdministrators(chatId);
            if (admins.Any(admin => admin.User.Id == userId &&
                    (admin.Status == ChatMemberStatus.Creator || admin.Status == ChatMemberStatus.Administrator)))
                return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Помилка перевірки адміна: {ex.Message}");
            return true;
        }

        return false;
    }

    static string GetRandomEmoji()
    {
        string[] emojis =
        [
            "🤷", "🤔", "😐", "🙃", "🫠", "🤨", "😶", "😵‍💫", "😬", "😴",
            "😕", "🫤", "🤪", "😳", "😓", "🥴", "🫥", "🌀", "🧠", "👀",
            "🤯", "🤡", "💤", "😟", "😲", "😩", "😮‍💨", "😖", "😫", "🥺",
            "😢", "😭", "😤", "😠", "😡", "🤬", "🤯", "🥵", "🥶", "😨",
            "😰", "😥", "😓", "😩", "😫", "😵", "🤯", "🤬", "😤", "😡",
            "😿", "😞", "🤤", "🙄", "😔", "😧", "😢", "🤧", "😰", "😱",
            "😯", "🥶", "🫨", "🙁", "😒", "🫣", "😲", "😮", "🫡", "🤥",
            "😬", "😵", "😳", "😶‍🌫️", "😏", "🥱", "🤭", "🤫", "🫢", "🫡",
            "🫠", "🫥", "🫤", "🫣", "🫢", "🫡", "🤖", "👾", "👻", "💀",
            "👽", "🤖", "🦾", "🦿", "🤖", "👽", "👾", "👻", "💀", "👺",
            "🧚", "🧜", "🦄", "🐉", "🐲", "🦊", "🐻", "🐼", "🐨", "🦁",
            "🐯", "🐮", "🐷", "🐸", "🐵", "🦓", "🦒", "🦔", "🦇", "🦉",
            "🦅", "🦆", "🦢", "🦚", "🦜", "🐧", "🐦", "🐤", "🐣", "🐥",
            "🐺", "🐗", "🐴", "🦄", "🐝", "🐛", "🦋", "🐌", "🐞", "🦗",
            "🪲", "🪳", "🦟", "🦠", "🪰", "🪱", "🐢", "🐍", "🦎", "🐙",
            "🦑", "🦐", "🦞", "🦀", "🐚", "🐠", "🐟", "🐡", "🐬", "🐳",
            "🐋", "🦈", "🐊", "🐅", "🐆", "🦓", "🦍", "🦧", "🐘", "🦏",
            "🦛", "🐪", "🐫", "🦙", "🦒", "🦘", "🦥", "🦦", "🦨", "🦡",
            "🦮", "🐕", "🐩", "🐕‍🦺", "🐈", "🐈‍⬛", "🐓", "🦚", "🦜", "🐇",
            "🐁", "🐀", "🐿️", "🦔", "🦇", "🐉", "🦕", "🦖", "🦧", "🦣",
            "🦤", "🦥", "🦦", "🦨", "🦩", "🦪", "🦭", "🦮", "🐕", "🐩"
        ];
        return emojis[Random.Shared.Next(emojis.Length)];
    }
}
