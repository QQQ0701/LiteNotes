using StockEvernote.Model;

namespace StockEvernote.Data;

public static class DbInitializer
{
    public static void Seed(EvernoteDbContext context)
    {
        if (context.Notebooks.Any()) return; // 已經有資料就跳過，不重複塞

        context.Notebooks.AddRange(
            new Notebook
            {
                UserId = "TestUser123",
                Name = "台積電研究",
                IsSynced = false,
                IsDeleted = false
            },
            new Notebook
            {
                UserId = "TestUser123",
                Name = "聯發科筆記",
                IsSynced = false,
                IsDeleted = false
            },
            new Notebook
            {
                UserId = "TestUser123",
                Name = "大盤觀察",
                IsSynced = false,
                IsDeleted = false
            }
        );

        context.SaveChanges();
    }
}