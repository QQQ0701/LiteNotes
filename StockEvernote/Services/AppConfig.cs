//using Microsoft.Extensions.Configuration;
//using System.IO;

//namespace StockEvernote.Services;

//public class AppConfig
//{
//    public static IConfiguration Configuration { get; }
//    static AppConfig()
//    {
//        var builder = new ConfigurationBuilder()
//            .SetBasePath(Directory.GetCurrentDirectory())
//            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

//        Configuration = builder.Build();
//    }
//    //Firebase Realtime Database / Authentication
//    public static string FirebaseApiKey => Configuration["Firebase:ApiKey"]!;
//    public static string FirebaseProjectId => Configuration["Firebase:ProjectId"]!;
//    public static string FirebaseDatabaseUrl => Configuration["Firebase:DatabaseUrl"]!;
    
//    //Azure Storage
//    public static string AzureStorageConnectionString => Configuration["Azure:Storage:ConnectionString"]!;
//    public static string AzureStorageContainerName => Configuration["Azure:Storage:ContainerName"]!;
//    //Azure Speech
//    public static string AzureSpeechApiKey => Configuration["Azure:Speech:ApiKey"]!;
//    public static string AzureSpeechRegion => Configuration["Azure:Speech:Region"]!;

//}
