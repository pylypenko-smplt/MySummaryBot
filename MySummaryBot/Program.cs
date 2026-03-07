using Telegram.Bot;
using Telegram.Bot.Types;

var adminChatId = Environment.GetEnvironmentVariable("ADMIN_CHAT_ID");
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var token = Environment.GetEnvironmentVariable("TTOKEN");

if (string.IsNullOrEmpty(token))
{
    Console.WriteLine("Please set TTOKEN environment variable");
    return;
}

if (string.IsNullOrEmpty(apiKey))
{
    Console.WriteLine("Please set OPENAI_API_KEY environment variable");
    return;
}

var httpClient = new HttpClient();
httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
httpClient.Timeout = TimeSpan.FromMinutes(10);

var ogHttpClient = new HttpClient();
ogHttpClient.Timeout = TimeSpan.FromSeconds(3);
ogHttpClient.DefaultRequestHeaders.Add("User-Agent", "MySummaryBot/2.0");

var braveSearchKey = Environment.GetEnvironmentVariable("BRAVE_SEARCH_KEY") ?? "";
if (string.IsNullOrEmpty(braveSearchKey))
    Console.WriteLine("Warning: BRAVE_SEARCH_KEY not set — inline image search will not work");
var braveHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
braveHttpClient.DefaultRequestHeaders.Add("X-Subscription-Token", braveSearchKey);
braveHttpClient.DefaultRequestHeaders.Add("Accept", "application/json");
var imageSearch = new ImageSearchService(braveHttpClient);

var dbPath = Path.Combine(Environment.GetEnvironmentVariable("DATA_DIR") ?? "/data", "mysummarybot.db");
using var store = new MessageStore(dbPath);

var ai = new AiService(httpClient);

try
{
    var botClient = new TelegramBotClient(token);
    var bot = new BotService(botClient, ai, store, adminChatId, ogHttpClient, imageSearch);
    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await botClient.SetMyCommands([
        new BotCommand { Command = "summary", Description = "Підсумок за останню годину" },
        new BotCommand { Command = "summary_day", Description = "Підсумок за останні 24 години" },
        new BotCommand { Command = "stats", Description = "Активність учасників за 24г" },
        new BotCommand { Command = "digest", Description = "Дайджест учасника: /digest @username" },
        new BotCommand { Command = "question", Description = "Задати питання боту" },
        new BotCommand { Command = "respect", Description = "Виміряти рівень поваги" },
        new BotCommand { Command = "vote", Description = "Голосування за зустріч" },
        new BotCommand { Command = "help", Description = "Список команд" }
    ]);

    await bot.SendAdmin("Bot started");

    var receivingTask = bot.RunReceivingLoop(cts.Token);
    var backgroundTask = bot.RunBackgroundLoop(cts.Token);
    Console.WriteLine("Bot запущено. Натисніть Ctrl+C для виходу.");

    await Task.WhenAny(receivingTask, backgroundTask);
    Console.WriteLine("Одна з задач завершилася. Зупиняємо...");
    cts.Cancel();

    await Task.WhenAll(receivingTask, backgroundTask);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    Console.WriteLine(ex.StackTrace);
}
