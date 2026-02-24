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

var ai = new AiService(httpClient);
var store = new MessageStore();

try
{
    var botClient = new TelegramBotClient(token);
    var bot = new BotService(botClient, ai, store, adminChatId);
    var cts = new CancellationTokenSource();

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    await botClient.SetMyCommands([
        new BotCommand { Command = "summary", Description = "Підсумок за останню годину" },
        new BotCommand { Command = "summary_day", Description = "Підсумок за останні 24 години" },
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
