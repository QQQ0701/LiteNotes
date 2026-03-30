using EvernoteClone.Mocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
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

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information() // 設定只記錄 Information 以上的層級
            .WriteTo.Console()          // 燈泡 1：寫入 Console (給工程師看)
            .WriteTo.File(              // 燈泡 2：寫入實體檔案 (給客訴查修用)
                path: "Logs/StockEvernote-.txt", // 檔案名稱，Serilog 會自動在後面加上日期
                rollingInterval: RollingInterval.Day, // 🌟 每天自動開一個新檔案！
                retainedFileCountLimit: 30,           // 最多保留 30 天的檔案，自動刪除舊檔防塞爆硬碟
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
        try
        {
            // 1. 讀取 DOTNET_ENVIRONMENT 環境變數，若無設定則預設為 "Production" 以防外洩內部設定
            var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

            // 2. 建置 Configuration 讀取器 (注意載入順序：後者會覆蓋前者)
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);

            IConfiguration configuration = builder.Build();

            // 3.建立人事招募名冊
            var services = new ServiceCollection();

            // ==========================================
            // 🛠️ 基礎建設註冊區 (Infrastructure)
            // ==========================================

            services.AddLogging(loggingBuilder =>
            {
               // loggingBuilder.ClearProviders(); // 清除預設的 Console 輸出 (WPF 看不到 Console)
                // 把前面設定好的 Serilog 塞進 DI 容器裡
                loggingBuilder.AddSerilog(dispose: true);
            });

            // ★ 註冊資料庫管家 (DbContext)

            // 1. 動態取得當前 Windows 使用者的 AppData/Local 路徑
            var folder = Environment.SpecialFolder.LocalApplicationData;
            var path = Environment.GetFolderPath(folder);
            var appFolder = System.IO.Path.Join(path, "StockEvernote");
            System.IO.Directory.CreateDirectory(appFolder);
            var dbPath = System.IO.Path.Join(appFolder, "StockEvernote.db");

            services.AddDbContext<EvernoteDbContext>(options =>
            {
                options.UseSqlite($"Data Source={dbPath}");
            }, ServiceLifetime.Scoped);

            //  🌟 註冊微軟官方日誌系統(讓全公司都能寫 Log)
            //services.AddLogging(configure =>
            //{
            //    configure.AddDebug();   // 讓 Log 顯示在 Visual Studio 的「輸出」視窗
            //    configure.AddConsole(); // 讓 Log 顯示在終端機 (如果有開的話)
            //});

            // 註冊設定檔手冊 (全公司共用一本)
            services.AddSingleton(configuration);

            // 註冊服務：開發階段使用 MockAuthService；WPF 對話框服務
            services.AddSingleton<IDialogService, WpfDialogService>();

            // 註冊全域的登入狀態保險箱 (必須是 Singleton)
            services.AddSingleton<IUserSession, UserSession>();


            // ==========================================
            // 🌐 外部 API 與業務邏輯註冊區 (Services)
            // ==========================================

            services.AddScoped<INotebookService, NotebookService>();
            services.AddScoped<INoteService, NoteService>();

            services.AddHttpClient<IFirestoreService, FirestoreService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(15);
            });
            services.AddScoped<IFirestoreService, FirestoreService>();

            services.AddHttpClient<ITelegramService, TelegramMessageServices>(client =>
            {
                string botToken = configuration["Telegram:BotToken"] ??
                throw new InvalidOperationException("找不到 Telegram:BotToken");

                client.BaseAddress = new Uri($"https://api.telegram.org/bot{botToken}/");
                client.Timeout = TimeSpan.FromSeconds(10);
            });

            if (environmentName == "Production")
            {
                // 正式環境：配發專屬 HttpClient 公務車給 Firebase 驗證員
                services.AddHttpClient<IAuthService, FirebaseAuthService>(client =>
                {
                    // 統一設定 Firebase Auth 的基礎網址
                    client.BaseAddress = new Uri("https://identitytoolkit.googleapis.com/");
                    client.Timeout = TimeSpan.FromSeconds(15);// 您甚至可以在這裡統一設定逾時時間
                });
            }
            else
            {
                // 開發環境：使用假資料驗證員，不消耗真實 API 額度
                services.AddSingleton<IAuthService, MockAuthService>();
            }
            // ==========================================
            // 🖥️ UI 視窗與視圖模型註冊區 (ViewModels & Windows)
            // ==========================================

            // Transient：免洗紙杯模式，每次打開視窗都是全新乾淨的狀態
            services.AddTransient<LoginViewModel>();
            services.AddTransient<LoginWindow>();

            services.AddScoped<NotesViewModel>();
            services.AddScoped<NotesWindow>();

            // 4. ✅ 人事系統正式上線！(建置 ServiceProvider)
            ServiceProvider = services.BuildServiceProvider();

            using (var scope = ServiceProvider.CreateScope())
            {
                // 從 DI 容器裡把剛剛註冊好的 DbContext 拿出來
                var dbContext = scope.ServiceProvider.GetRequiredService<EvernoteDbContext>();

                // 命令它：去檢查 AppData 裡有沒有資料表，沒有的話立刻照著設計圖建出來！
                dbContext.Database.Migrate();

                //DbInitializer.Seed(dbContext);//測試用
            }

            // 5. 自動解析並顯示 LoginWindow (DI 容器會幫我們把所有依賴組裝好)
           
         
            var appScope = ServiceProvider.CreateScope();
            var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
            loginWindow.Show();
            //var notesWindow = appScope.ServiceProvider.GetRequiredService<NotesWindow>();
            //notesWindow.Show();
        }
        catch (Exception ex)
        {
            // 🌟 萬一程式連啟動都失敗，Serilog 也能捕捉到！
            Log.Fatal(ex, "應用程式啟動時發生毀滅性錯誤！");
            MessageBox.Show(ex.InnerException?.Message ?? ex.Message);
        }

    }
    // 🌟 4. 覆寫 OnExit：程式關閉時的收尾動作
    protected override void OnExit(ExitEventArgs e)
    {
        // 這是 Serilog 最重要的一步：把還卡在記憶體裡的 Log，強制寫進 txt 檔案裡！
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}