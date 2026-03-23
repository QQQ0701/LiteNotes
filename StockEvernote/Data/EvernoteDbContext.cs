using Microsoft.EntityFrameworkCore;
using StockEvernote.Model; 

namespace StockEvernote.Data;
public class EvernoteDbContext : DbContext
{
    // 這兩行代表我們要在資料庫裡建立這兩張資料表
    public DbSet<Notebook> Notebooks { get; set; }
    public DbSet<Note> Notes { get; set; }

    // 建構子：接收外部傳進來的設定（例如 SQLite 的檔案路徑）
    public EvernoteDbContext(DbContextOptions<EvernoteDbContext> options) : base(options) { }
   
}