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

    // ══════════════════════════════════════════════════════════
    //  應用程式生命週期
    // ══════════════════════════════════════════════════════════
    protected override void OnStartup(StartupEventArgs e)
    {
        // EF Core CLI 工具（dotnet ef migrations）會以非 STA 執行緒啟動 App，
        // 在此攔截避免觸發 UI 初始化導致崩潰
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            this.Shutdown();
            return;
        }
        base.OnStartup(e);

        InitializeSerilog();

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
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("系統準備關閉...");

        // 釋放全局 Scope
        _appScope?.Dispose();

        // 這是 Serilog 最重要的一步：把還卡在記憶體裡的 Log，強制寫進 txt 檔案裡！
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    // ══════════════════════════════════════════════════════════
    //  Serilog 初始化
    // ══════════════════════════════════════════════════════════

    private static void InitializeSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                path: "Logs/StockEvernote-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    // ══════════════════════════════════════════════════════════
    //  DI 容器註冊
    // ══════════════════════════════════════════════════════════

    private void ConfigureServices(IServiceCollection services, IConfiguration configuration, string environmentName)
    {
        // --- 基礎建設 (Infrastructure) ---
        services.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));
        services.AddSingleton(configuration);
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<IUserSession, UserSession>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IFileUploadService, AzureBlobService>();

        // --- 資料庫 (Database) ---

        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        var appFolder = Path.Join(path, "StockEvernote");
        Directory.CreateDirectory(appFolder);
        var dbPath = Path.Join(appFolder, "StockEvernote.db");

        services.AddDbContext<EvernoteDbContext>(options =>
        {
            options.UseSqlite($"Data Source={dbPath}");
        }, ServiceLifetime.Scoped);

        // 業務邏輯 (Services) ---

        services.AddScoped<INotebookService, NotebookService>();
        services.AddScoped<INoteService, NoteService>();

        // ── 外部 API ──

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

    // ══════════════════════════════════════════════════════════
    //  資料庫初始化
    // ══════════════════════════════════════════════════════════

    private static void InitializeDatabase()
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
}