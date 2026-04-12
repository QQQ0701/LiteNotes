using EvernoteClone.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using StockEvernote.Contracts;
using StockEvernote.Data;
using StockEvernote.Services;
using StockEvernote.View;
using StockEvernote.ViewModel;
using System.IO;
using System.Windows;

namespace StockEvernote;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    // 建立一個全局的 Scope 供應用程式生命週期使用，避免 Scoped 服務被提早釋放
    private IServiceScope? _appScope;
    protected override void OnStartup(StartupEventArgs e)
    {
        // 🌟 終極護城河：防堵 EF Core 幽靈測試員！
        // 檢查：如果目前的執行緒不是 STA (UI 專屬執行緒)，代表是 EF Core 工具在背景亂敲門
        if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
        {
            // 直接強制退出這個方法，不准往下執行畫畫面的邏輯！
            this.Shutdown();
            return;
        }
        base.OnStartup(e);

        // 1. 初始化 Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information() // 設定只記錄 Information 以上的層級
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")// 燈泡 1：寫入 Console (給工程師看)
            .WriteTo.File(              // 燈泡 2：寫入實體檔案 (給客訴查修用)
              path: "Logs/StockEvernote-.txt", // 檔案名稱，Serilog 會自動在後面加上日期
              rollingInterval: RollingInterval.Day, // 🌟 每天自動開一個新檔案！
              retainedFileCountLimit: 30,           // 最多保留 30 天的檔案，自動刪除舊檔防塞爆硬碟
              outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
      )
      .CreateLogger();

        try
        {
            Log.Information("系統啟動中...");

            var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            Log.Information("當前執行環境：{EnvironmentName}", environmentName);

            // 2. 建置 Configuration 讀取器 (注意載入順序：後者會覆蓋前者)
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

            IConfiguration configuration = builder.Build();
            Log.Information("設定檔載入完成");

            //  註冊 DI 容器
            var services = new ServiceCollection();
            ConfigureServices(services, configuration, environmentName);
     
            ServiceProvider = services.BuildServiceProvider();
            Log.Information("DI 容器建置完成");

            // 4. 初始化資料庫
            InitializeDatabase();

            // 5. 解析並啟動起始畫面
            _appScope = ServiceProvider.CreateScope();
            var loginWindow = _appScope.ServiceProvider.GetRequiredService<LoginWindow>();

            Log.Information("顯示 LoginWindow");
            loginWindow.Show();
            //var notesWindow = appScope.ServiceProvider.GetRequiredService<NotesWindow>();
            //notesWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "應用程式啟動時發生未預期的毀滅性錯誤");
            MessageBox.Show($"系統啟動失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    // ==========================================
    private void ConfigureServices(IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        // --- 基礎建設 (Infrastructure) ---
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.AddSerilog(dispose: true);
        });
        // 註冊設定檔手冊 (全公司共用一本)
        services.AddSingleton(configuration);
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUserSession, UserSession>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IFileUploadService, AzureBlobService>();

        // --- 資料庫 (Database) ---

        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        var appFolder = System.IO.Path.Join(path, "StockEvernote");
        System.IO.Directory.CreateDirectory(appFolder);
        var dbPath = System.IO.Path.Join(appFolder, "StockEvernote.db");

        services.AddDbContext<EvernoteDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        }, ServiceLifetime.Scoped);

        // --- 外部 API 與業務邏輯 (Services) ---

        services.AddScoped<INotebookService, NotebookService>();
        services.AddScoped<INoteService, NoteService>();

        services.AddHttpClient<IFirestoreService, FirestoreService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<ITelegramService, TelegramMessageServices>(client =>
        {
            string botToken = configuration["Telegram:BotToken"] ??
            throw new InvalidOperationException("找不到 Telegram:BotToken");

            client.BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        if (environmentName == "Production")
        {
            services.AddHttpClient<IAuthService, FirebaseAuthService>(client =>
            {
                client.BaseAddress = new Uri("https://identitytoolkit.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(15);
            });
        }
        else
        {
            services.AddSingleton<IAuthService, MockAuthService>();
        }

        // --- UI 與 ViewModel ---

        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();

        services.AddTransient<NotesViewModel>();
        services.AddTransient<NotesWindow>();
    }
    private void InitializeDatabase()
    {
        Log.Information("開始檢查並更新資料庫 Schema...");

        using var scope = ServiceProvider!.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EvernoteDbContext>();

        try
        {
            dbContext.Database.Migrate();
            Log.Information("資料庫 Schema 更新完成");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "資料庫遷移失敗");
            throw;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("系統準備關閉...");

        // 釋放全局 Scope
        _appScope?.Dispose();

        // 這是 Serilog 最重要的一步：把還卡在記憶體裡的 Log，強制寫進 txt 檔案裡！
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}