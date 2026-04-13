using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LiteNotes.Data; 
public class EvernoteDbContextFactory : IDesignTimeDbContextFactory<EvernoteDbContext>
{
    public EvernoteDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<EvernoteDbContext>();
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);

        var appFolder = System.IO.Path.Join(path, "LiteNotes");
        System.IO.Directory.CreateDirectory(appFolder);

        var dbPath = System.IO.Path.Join(appFolder, "LiteNotes.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new EvernoteDbContext(optionsBuilder.Options);
    }
}