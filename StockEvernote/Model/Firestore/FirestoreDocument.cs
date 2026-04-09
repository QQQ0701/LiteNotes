namespace StockEvernote.Model.Firestore;

public class FirestoreDocument
{
    public string? Name { get; set; }
    public Dictionary<string, FirestoreValue>? Fields { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime UpdateTime { get; set; }
}

public class FirestoreValue
{
    public string? StringValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? TimestampValue { get; set; }
    public string? IntegerValue { get; set; }
}

public class FirestoreListResponse
{
    public List<FirestoreDocument>? Documents { get; set; }
    public string? NextPageToken { get; internal set; }
}