using System.Text.Json.Serialization;

namespace StockEvernote.Model.Firebase;

public class FirebaseAuthResponse
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("idToken")]
    public string? IdToken { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("expiresIn")]
    public string? ExpiresIn { get; set; }

    [JsonPropertyName("localId")]
    public string? LocalId { get; set; }
}