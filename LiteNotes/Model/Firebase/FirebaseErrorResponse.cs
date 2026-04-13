using System.Text.Json.Serialization;

namespace LiteNotes.Model.Firebase;

public class FirebaseErrorResponse
{
    [JsonPropertyName("error")]
    public FirebaseErrorDetail? Error { get; set; }
}

public class FirebaseErrorDetail
{
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
