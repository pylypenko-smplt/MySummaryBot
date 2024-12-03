using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;

var token = Environment.GetEnvironmentVariable("TTOKEN");
if (token == null)
{
    Console.WriteLine("Please set TTOKEN environment variable");
    return;
}

var botClient = new TelegramBotClient(token);
var messages = new ConcurrentDictionary<long, List<string>>();

botClient.StartReceiving(
    updateHandler: HandleUpdateAsync,
    errorHandler: HandleErrorAsync
);

Console.WriteLine("Bot started. Press any key to exit");
Console.ReadLine();

async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    if (update.Message?.Text != null)
    {        long chatId = update.Message.Chat.Id;
        var userName = update.Message.From?.FirstName ?? update.Message.From?.Username;

        //Console.WriteLine($"Сhat id: {chatId}, User: {userName}, Message: {update.Message.Text}");

        if (!messages.ContainsKey(chatId))
        {
            messages[chatId] = new List<string>();
        }

        if (update.Message.ReplyToMessage != null)
        {
            var replyToName = update.Message.ReplyToMessage.From?.FirstName ??
                              update.Message.ReplyToMessage.From?.Username;
            messages[chatId].Add($"[replyto///{replyToName}]{userName}///{update.Message.Text}");
        }
        else
        {
            messages[chatId].Add($"{userName}///{update.Message.Text}");
        }

        if (update.Message.Text.StartsWith("/summary"))
        {
            string summary = await GetSummary(messages[chatId]);
            await botClient.SendTextMessageAsync(chatId, summary);

            messages[chatId].Clear();
            messages[chatId].Add($"Revverb Summary Bot///{summary}");
        }
    }
}

Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    Console.WriteLine(exception.Message);
    return Task.CompletedTask;
}


async Task<string> GetSummary(List<string> messages)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (apiKey == null)
    {
        throw new Exception("Please set OPENAI_API_KEY environment variable");
    }
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var formattedMessages = string.Join("\n", messages);
    var maxTokens = 500;

    var requestBody = new
    {
        model = "gpt-4o-mini",
        messages = new[]
        {
            new { role = "system", content = "You a revverb chat helper. You speak in Ukrainain language" },
            new
            {
                role = "user",
                content =
                    $"Make a summary of this messages in a few paragraphs. " +
                    $"Everyone should be addressed as Пан or Пані. " +
                    $"First message in batch is always a previous summary. Use it for added context, do not repeat summaries" +
                    $"You can add a little sarcasm and passive aggression to match chats' tone. " +
                    $"Remember that your max token count is {maxTokens}. All messages in format name///message. " +
                    $"Replies are marked with [replyto///name]. " +
                    $"Messages:\n{formattedMessages}"
            }
        },
        max_tokens = maxTokens
    };

    try
    {
        var response = await httpClient.PostAsync(
            "https://api.openai.com/v1/chat/completions",
            new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Error API: {response.StatusCode}, Details: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonSerializer.Deserialize<JsonDocument>(content);

        return jsonResponse.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }
    catch (Exception ex)
    {
        throw new Exception("Error while getting summary", ex);
    }
}