using ChyguiSlide.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ChyguiSlide.Data.DesignTime;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        var databasePath = DatabasePathProvider.GetDatabasePath();
        optionsBuilder.UseSqlite($"Data Source={databasePath}");
        return new AppDbContext(optionsBuilder.Options);
    }
}

