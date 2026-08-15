using ChyguiSlide.Data;
using ChyguiSlide.Data.Entities;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ChyguiSlide;

/// <summary>
/// Утилита для импорта песен из HTML файлов "Песнь возрождения"
/// Запуск: dotnet run --project ChyguiSlide.csproj -- --import-pesn-vozr
/// </summary>
public class ImportPesnVozrUtility
{
    public static async Task RunAsync(string[] args)
    {
        var tempPath = Path.Combine(ChyguiSlide.Data.AppPaths.GetLocalAppDataRoot(), "Temp");
        var htmlFiles = Directory.GetFiles(tempPath, "*.htm", SearchOption.TopDirectoryOnly);
        
        if (htmlFiles.Length == 0)
        {
            Console.WriteLine("HTML файлы не найдены в папке Temp");
            return;
        }

        Console.WriteLine($"Найдено {htmlFiles.Length} HTML файлов");

        var host = Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                services.AddDbContext<AppDbContext>(options =>
                {
                    var dbPath = Data.DatabasePathProvider.GetDatabasePath();
                    options.UseSqlite($"Data Source={dbPath}");
                });

                services.AddScoped<ICatalogService, Services.Implementations.CatalogService>();
                services.AddSingleton<PesnVozrImportService>();
            })
            .Build();

        await host.StartAsync();

        using var scope = host.Services.CreateScope();
        var catalogService = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var importService = scope.ServiceProvider.GetRequiredService<PesnVozrImportService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Создаём сборник "Песнь возрождения 3055"
        var existingCollections = await catalogService.GetSongCollectionsAsync();
        var existingCollection = existingCollections.FirstOrDefault(c => 
            c.Name.Equals("Песнь возрождения 3055", StringComparison.OrdinalIgnoreCase));

        Guid collectionId;
        
        if (existingCollection != null)
        {
            collectionId = existingCollection.Id;
            Console.WriteLine($"Сборник '{existingCollection.Name}' уже существует (ID: {collectionId})");
        }
        else
        {
            var newCollection = new SongCollection
            {
                Id = Guid.NewGuid(),
                Name = "Песнь возрождения 3055",
                Description = "Сборник песен Песнь возрождения, редакция 3055",
                SortOrder = existingCollections.Count + 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var created = await catalogService.UpsertSongCollectionAsync(newCollection);
            collectionId = created.Id;
            Console.WriteLine($"Создан сборник '{created.Name}' (ID: {collectionId})");
        }

        // Импортируем песни
        var totalImported = 0;
        var totalSkipped = 0;

        foreach (var file in htmlFiles)
        {
            Console.WriteLine($"Обработка файла: {Path.GetFileName(file)}");
            
            try
            {
                var songs = await importService.ImportFromFileAsync(file, collectionId);
                Console.WriteLine($"  Найдено песен: {songs.Count}");

                foreach (var song in songs)
                {
                    // Проверяем, есть ли уже песня с таким номером в этом сборнике
                    var existingSongs = await catalogService.GetSongsByCollectionAsync(collectionId);
                    var existing = existingSongs.FirstOrDefault(s => 
                        s.Number == song.Number && 
                        s.CollectionId == collectionId);

                    if (existing != null)
                    {
                        Console.WriteLine($"  Пропущен: №{song.Number} - уже существует");
                        totalSkipped++;
                        continue;
                    }

                    await catalogService.UpsertSongAsync(song);
                    Console.WriteLine($"  Импортирован: №{song.Number} - {song.Title}");
                    totalImported++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Ошибка при обработке файла: {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"=== Импорт завершён ===");
        Console.WriteLine($"Всего импортировано: {totalImported}");
        Console.WriteLine($"Всего пропущено: {totalSkipped}");

        await host.StopAsync();
    }
}
