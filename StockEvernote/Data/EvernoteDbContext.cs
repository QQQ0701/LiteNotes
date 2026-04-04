using Microsoft.EntityFrameworkCore;
using StockEvernote.Model; 

namespace StockEvernote.Data;
public class EvernoteDbContext : DbContext
{
    public DbSet<Notebook> Notebooks { get; set; }
    public DbSet<Note> Notes { get; set; }
    public DbSet<Attachment> Attachments { get; set; }

    // 建構子：接收外部傳進來的設定（例如 SQLite 的檔案路徑）
    public EvernoteDbContext(DbContextOptions<EvernoteDbContext> options) : base(options) { }
   
}