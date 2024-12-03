using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
//var messages = new ConcurrentDictionary<long, List<string>>();
var messages = new ConcurrentDictionary<long, List<MessageModel>>();
var lastRequest = new ConcurrentDictionary<long, DateTime>();

try
{
    var token = Environment.GetEnvironmentVariable("TTOKEN");
    if (token == null)
    {
        Console.WriteLine("Please set TTOKEN environment variable");
        return;
    }

    var botClient = new TelegramBotClient(token);

    botClient.StartReceiving(
        HandleUpdateAsync,
        HandleErrorAsync
    );

    Console.WriteLine("Bot started. Press any key to exit");
    do
    {
        await Task.Delay(10000);
        await ClearOldMessages();
    } while (true);

}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
}

async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
{
    if (update.Message?.Text != null)
    {
        var chatId = update.Message.Chat.Id;
        var userName = update.Message.From?.FirstName ?? update.Message.From?.Username;

        Console.WriteLine($"Сhat id: {chatId}, User: {userName}, Message: {update.Message.Text}");

        if (!messages.ContainsKey(chatId)) messages[chatId] = new ();

        messages[chatId].Add(new MessageModel
        {
            ChatId = chatId,
            Name = userName,
            Timestamp = DateTime.Now,
            Text = update.Message.Text,
            ReplyTo = update.Message.ReplyToMessage?.From?.FirstName ?? update.Message.ReplyToMessage?.From?.Username
        });

        if (update.Message.Text.StartsWith("/summary") && !update.Message.Text.StartsWith("/summary_hour") && !update.Message.Text.StartsWith("/summary_day"))
        {
            var lastRequestTime = lastRequest.TryGetValue(chatId, out var value) ? value : DateTime.MinValue;
            var messagesForSummary = messages[chatId].Where(m => m.Timestamp > lastRequestTime).ToList();
            var summary = await GetSummary(messages[chatId]);
            await botClient.SendTextMessageAsync(chatId, summary);
            lastRequest[chatId] = DateTime.Now;
        }

        if (update.Message.Text.StartsWith("/summary_hour"))
        {
            var messagesForSummary = messages[chatId].Where(m => m.Timestamp > DateTime.Now.AddHours(-1)).ToList();
            var summary = await GetSummary(messagesForSummary);
            await botClient.SendTextMessageAsync(chatId, summary);
        }
        
        if(update.Message.Text.StartsWith("/summary_day"))
        {
            var messagesForSummary = messages[chatId].Where(m => m.Timestamp > DateTime.Now.AddDays(-1)).ToList();
            var summary = await GetSummary(messagesForSummary);
            await botClient.SendTextMessageAsync(chatId, summary);
        }
        
        // if(update.Message.Text.StartsWith("/summary_week"))
        // {
        //     var messagesForSummary = messages[chatId].Where(m => m.Timestamp > DateTime.Now.AddDays(-7)).ToList();
        //     var summary = await GetSummary(messagesForSummary);
        //     await botClient.SendTextMessageAsync(chatId, summary);
        // }
        
        if(update.Message.Text.StartsWith("/question"))
        {
            var question = update.Message.Text.Replace("/question", "").Trim();
            var answer = await GetAnswer(question, userName);
            await botClient.SendTextMessageAsync(chatId, answer);
        }
    }
}

Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
{
    Console.WriteLine(exception.Message);
    return Task.CompletedTask;
}


async Task<string> GetSummary(List<MessageModel> messages)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (apiKey == null) throw new Exception("Please set OPENAI_API_KEY environment variable");
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var formattedMessages = string.Join("\n", messages.Select(m => string.IsNullOrWhiteSpace(m.ReplyTo) ? $"{m.Name}///{m.Text}" : $"[replyto///{m.ReplyTo}]{m.Name}///{m.Text}"));
    var maxTokens = 300;

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
                    $"Make a summary of this messages in a few sentences or paragraphs. " +
                    $"Everyone should be addressed as Пан or Пані. " +
                    //$"First message in batch is always a previous summary. Use it for added context, do not repeat summaries" +
                    $"You can add a very little amount of sarcasm and very little amount of passive aggression to match chats tone. " +
                    $"Remember that your max token count is {maxTokens}. All messages in format name///message. " +
                    $"Replies on input are marked with [replyto///name]. Do not include [replyto///name] in answers" +
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


async Task<string> GetAnswer(string question, string user)
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (apiKey == null) throw new Exception("Please set OPENAI_API_KEY environment variable");
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

    var maxTokens = 200;

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
                    $"You have context from messages of this chat. " +
                    $"Answer a question from user. " +
                    $"Everyone should be addressed as Пан or Пані. " +
                    $"Remember that your max token count is {maxTokens}. All messages in format name///message. " +
                    $"Replies on input are marked with [replyto///name]. Do not include [replyto///name] in answers" +
                    $"User: {user} " +
                    $"Question: {question}"
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
        throw new Exception("Error while getting answear", ex);
    }
}


async Task ClearOldMessages()
{
    var now = DateTime.Now;
    foreach (var chatId in messages.Keys)
    {
        messages[chatId] = messages[chatId].Where(m => m.Timestamp > now.AddDays(-7)).ToList();
    }
}

public class MessageModel
{
    public long ChatId { get; set; }
    public string Name { get; set; }
    public DateTime Timestamp { get; set; }
    public string Text { get; set; }
    public string ReplyTo { get; set; }
}