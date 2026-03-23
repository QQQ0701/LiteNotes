using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockEvernote.Data; // 換成您實際的命名空間

// ★ 實作 IDesignTimeDbContextFactory，這就是 EF Core 工人的專屬通道
public class EvernoteDbContextFactory : IDesignTimeDbContextFactory<EvernoteDbContext>
{
    public EvernoteDbContext CreateDbContext(string[] args)
    {
        // 直接在這裡告訴工人，要用 SQLite，且檔案叫什麼名字
        var optionsBuilder = new DbContextOptionsBuilder<EvernoteDbContext>();
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);

        // 1. 先定義出「資料夾」的路徑
        var appFolder = System.IO.Path.Join(path, "StockEvernote");
        System.IO.Directory.CreateDirectory(appFolder);

        // 2. 在裡面建一個我們軟體專屬的資料夾和檔案路徑
        var dbPath = System.IO.Path.Join(appFolder, "StockEvernote.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new EvernoteDbContext(optionsBuilder.Options);
    }
}