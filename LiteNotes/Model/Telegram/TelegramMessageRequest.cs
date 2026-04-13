using System.Text.Json.Serialization;

namespace LiteNotes.Model.Telegram;

public class TelegramMessageRequest
{
    [JsonPropertyName("chat_id")]
    public string Chatid { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("parse_mode")]
    public string? Parsemode { get; set; } 
}
