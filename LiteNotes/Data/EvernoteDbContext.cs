using Microsoft.EntityFrameworkCore;
using LiteNotes.Model; 

namespace LiteNotes.Data;
public class EvernoteDbContext : DbContext
{
    public DbSet<Notebook> Notebooks { get; set; }
    public DbSet<Note> Notes { get; set; }
    public DbSet<Attachment> Attachments { get; set; }
    public EvernoteDbContext(DbContextOptions<EvernoteDbContext> options) : base(options) { }
   
}