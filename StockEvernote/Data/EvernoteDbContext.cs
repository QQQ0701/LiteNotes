using Microsoft.EntityFrameworkCore;
using StockEvernote.Model; 

namespace StockEvernote.Data;
public class EvernoteDbContext : DbContext
{
    public DbSet<Notebook> Notebooks { get; set; }
    public DbSet<Note> Notes { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public EvernoteDbContext(DbContextOptions<EvernoteDbContext> options) : base(options) { }
   
}