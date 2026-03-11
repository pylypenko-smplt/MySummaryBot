using System.Text.Json.Serialization;

public class MessageModel
{
    [JsonPropertyName("msg_id")] public int MessageId { get; set; }
    [JsonPropertyName("user_id")] public long UserId { get; set; }
    [JsonPropertyName("chat_id")] public long ChatId { get; set; }
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("lang")] public string? Language { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("ts")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("reply_to")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReplyToMessageId { get; set; }

    [JsonPropertyName("media_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonPropertyName("link_preview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LinkPreview { get; set; }

    [JsonIgnore] public string? UrlNormalized { get; set; }
    [JsonIgnore] public string? MediaUniqueId { get; set; }
    [JsonIgnore] public long? FwdChannelId { get; set; }
    [JsonIgnore] public int? FwdMessageId { get; set; }
}

public class OpenAiCompletionResponse
{
    [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    [JsonPropertyName("usage")] public Usage? Usage { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }
}

public class Choice
{
    [JsonPropertyName("message")] public Message? Message { get; set; }
}

public class Message
{
    [JsonPropertyName("content")] public string? Content { get; set; }
}

public class Usage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}
