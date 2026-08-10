using System;
using System.Collections.Generic;
using System.IO;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using ChyguiSlide.Data;
using ChyguiSlide.Services;
using ChyguiSlide.Services.Abstractions;
using ChyguiSlide.Services.Implementations;
using ChyguiSlide.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Navigation;
using WinRT.Interop;

namespace ChyguiSlide
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? window;
        public static Window MainWindow { get; private set; } = null!;
        public static IntPtr MainWindowHandle { get; private set; }
        public static IHost AppHost { get; private set; } = null!;
        public static DispatcherQueue MainDispatcherQueue { get; private set; } = DispatcherQueue.GetForCurrentThread()!;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            // Глобальный перехват, чтобы не падать без сообщения
            UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppHost = CreateHostBuilder();
            MainDispatcherQueue = DispatcherQueue.GetForCurrentThread() ??
                                  DispatcherQueueController.CreateOnDedicatedThread().DispatcherQueue;
        }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            LogToFile("=== ЗАПУСК ПРИЛОЖЕНИЯ ===");
            LogToFile($"Время запуска: {DateTimeOffset.Now:O}");
            LogToFile($"Аргументы запуска: {e.Arguments ?? "(нет)"}");
            
            try
            {
                LogToFile("Инициализация AppHost...");
                AppHost.StartAsync().GetAwaiter().GetResult();
                LogToFile("AppHost инициализирован успешно");

                // Ранний старт глобальных хоткеев (Esc/стрелки при фокусе на проекторе)
                _ = AppHost.Services.GetRequiredService<HotkeyDispatcher>();

                using (var scope = AppHost.Services.CreateScope())
                {
                    LogToFile("Получение AppDbContext...");
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    LogToFile("AppDbContext получен");
                    
                    // EnsureCreated: true только когда файл БД создан впервые
                    LogToFile("Проверка/создание базы данных...");
                    var databaseCreated = db.Database.EnsureCreated();
                    LogToFile(databaseCreated
                        ? "База данных создана впервые"
                        : "База данных уже существует");
                    
                    LogToFile("Применение миграций...");
                    ApplyLightweightMigrations(db);
                    LogToFile("Миграции применены");

                    // Сид только при первом создании БД — не при каждом запуске
                    if (databaseCreated)
                    {
                        await ImportPesnVozrSeedAsync();
                    }
                }

                LogToFile("Создание главного окна...");
                window ??= new Window();
                window.Title = "ChyguiSlide";
                MainWindow = window;
                MainWindowHandle = WindowNative.GetWindowHandle(window);
                window.Closed += OnMainWindowClosed;
                LogToFile($"Главное окно создано, Handle: {MainWindowHandle}");

                if (window.DispatcherQueue is DispatcherQueue dispatcherQueue)
                {
                    MainDispatcherQueue = dispatcherQueue;
                    LogToFile("DispatcherQueue установлен");
                }
                else
                {
                    LogToFile("ПРЕДУПРЕЖДЕНИЕ: DispatcherQueue не найден");
                }

                if (window.Content is not Frame rootFrame)
                {
                    LogToFile("Создание Frame для навигации...");
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    window.Content = rootFrame;
                    LogToFile("Frame создан и установлен");
                }
                else
                {
                    LogToFile("Frame уже существует");
                }

                LogToFile("Навигация на MainPage...");
                var navigationResult = rootFrame.Navigate(typeof(MainPage), e.Arguments);
                LogToFile($"Результат навигации: {navigationResult}");

                LogToFile("Активация окна...");
                window.Activate();
                LogToFile("Окно активировано");

                try
                {
                    var uiTheme = await AppHost.Services
                        .GetRequiredService<IDisplaySettingsService>()
                        .GetAppUiThemeAsync();
                    AppUiThemeApplier.Apply(uiTheme);
                }
                catch (Exception themeEx)
                {
                    LogToFile($"Тема UI: {themeEx.Message}");
                }

                LogToFile("=== ЗАПУСК ЗАВЕРШЕН УСПЕШНО ===");
            }
            catch (Exception ex)
            {
                LogToFile($"ОШИБКА ПРИ ЗАПУСКЕ: {ex}");
                LogToFile($"Тип ошибки: {ex.GetType().FullName}");
                LogToFile($"Сообщение: {ex.Message}");
                LogToFile($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    LogToFile($"Внутренняя ошибка: {ex.InnerException}");
                }
                throw;
            }
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// При закрытии главного окна закрываем проектор и освобождаем ресурсы,
        /// иначе окно трансляции остаётся жить отдельно.
        /// </summary>
        private static void OnMainWindowClosed(object sender, WindowEventArgs args)
        {
            try
            {
                LogToFile("Главное окно закрывается — останавливаем проектор...");

                if (AppHost.Services.GetService(typeof(IProjectionDisplayService)) is IProjectionDisplayService projection)
                {
                    projection.Hide();
                }

                if (AppHost.Services.GetService(typeof(HotkeyDispatcher)) is HotkeyDispatcher hotkeys)
                {
                    hotkeys.Dispose();
                }

                try
                {
                    AppHost.StopAsync().GetAwaiter().GetResult();
                }
                catch (Exception stopEx)
                {
                    LogToFile($"Ошибка остановки AppHost: {stopEx.Message}");
                }

                LogToFile("Завершение приложения после закрытия главного окна.");
            }
            catch (Exception ex)
            {
                LogToFile($"Ошибка при закрытии главного окна: {ex}");
            }
        }

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Логируем сразу, чтобы увидеть исключение до Debugger.Break()
            System.Diagnostics.Debug.WriteLine("========================================");
            System.Diagnostics.Debug.WriteLine($"[App] UNHANDLED EXCEPTION CAUGHT!");
            System.Diagnostics.Debug.WriteLine($"[App] Exception type: {e.Exception.GetType().FullName}");
            System.Diagnostics.Debug.WriteLine($"[App] Message: {e.Exception.Message}");
            System.Diagnostics.Debug.WriteLine($"[App] StackTrace: {e.Exception.StackTrace}");
            if (e.Exception.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"[App] InnerException type: {e.Exception.InnerException.GetType().FullName}");
                System.Diagnostics.Debug.WriteLine($"[App] InnerException message: {e.Exception.InnerException.Message}");
                System.Diagnostics.Debug.WriteLine($"[App] InnerException StackTrace: {e.Exception.InnerException.StackTrace}");
            }
            System.Diagnostics.Debug.WriteLine("========================================");
            
            e.Handled = true;
            LogUnhandled("UI", e.Exception);
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            LogUnhandled("Task", e.Exception);
        }

        private static void LogUnhandled(string source, Exception exception)
        {
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "error.log");
                var lines = new List<string>
                {
                    $"[{DateTimeOffset.Now:O}] {source} exception",
                    $"Type: {exception.GetType().FullName}",
                    $"Message: {exception.Message}",
                    $"HResult: 0x{exception.HResult:X8}",
                    $"StackTrace:",
                    exception.StackTrace ?? "(null)"
                };
                
                if (exception.InnerException != null)
                {
                    lines.Add($"InnerException Type: {exception.InnerException.GetType().FullName}");
                    lines.Add($"InnerException Message: {exception.InnerException.Message}");
                    lines.Add($"InnerException HResult: 0x{exception.InnerException.HResult:X8}");
                    lines.Add($"InnerException StackTrace:");
                    lines.Add(exception.InnerException.StackTrace ?? "(null)");
                }
                
                lines.Add(string.Empty);
                lines.Add("Full exception details:");
                lines.Add(exception.ToString());
                lines.Add(string.Empty);
                lines.Add("========================================");
                lines.Add(string.Empty);
                
                File.AppendAllLines(logPath, lines);
            }
            catch
            {
                // Игнорируем проблемы с логированием, чтобы не сорвать приложение
            }
        }

        private static void LogToFile(string message)
        {
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "startup.log");
                var logLine = $"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(logPath, logLine + Environment.NewLine);
                
                // Вывод в консоль (если запущено через dotnet run)
                System.Diagnostics.Debug.WriteLine(logLine);
                Console.WriteLine(logLine);
            }
            catch
            {
                // Игнорируем проблемы с логированием, чтобы не сорвать приложение
            }
        }

    /// <summary>
    /// Первичное наполнение каталога (только при создании новой БД): Песнь возрождения 3300 из SoftProjector .sps.
    /// </summary>
    private static async Task ImportPesnVozrSeedAsync()
    {
        try
        {
            var seedCandidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "Seed", "PesnVozr", "pv3300.sps"),
                Path.Combine(AppContext.BaseDirectory, "pv3300.sps"),
            };

            var spsPath = seedCandidates.FirstOrDefault(File.Exists);
            if (spsPath is null)
            {
                LogToFile("Сид pv3300.sps не найден (Assets/Seed/PesnVozr/pv3300.sps).");
                return;
            }

            LogToFile($"Сид «Песнь возрождения 3300» из {spsPath}");

            var catalogService = AppHost.Services.GetRequiredService<ICatalogService>();
            var importService = AppHost.Services.GetRequiredService<SoftProjectorSpsImportService>();

            var parsed = await importService.ImportFromFileAsync(spsPath);
            if (parsed.Songs.Count == 0)
            {
                LogToFile($"Сид: в файле нет песен. {parsed.Warning}");
                return;
            }

            var collectionName = string.IsNullOrWhiteSpace(parsed.SongbookName)
                ? "Песнь возрождения 3300"
                : parsed.SongbookName.Trim();

            var created = await catalogService.UpsertSongCollectionAsync(new Data.Entities.SongCollection
            {
                Id = Guid.NewGuid(),
                Name = collectionName,
                Description = string.IsNullOrWhiteSpace(parsed.Description)
                    ? "Сборник песен Песнь возрождения, редакция 3300"
                    : parsed.Description.Trim(),
                SortOrder = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            LogToFile($"Создан сборник '{created.Name}' (ID: {created.Id}), песен в файле: {parsed.Songs.Count}");

            var totalImported = 0;
            var totalSkipped = 0;
            var existingNumbers = new HashSet<int>();

            foreach (var song in parsed.Songs)
            {
                song.CollectionId = created.Id;

                if (song.Number is int number && !existingNumbers.Add(number))
                {
                    LogToFile($"  Пропущен: №{song.Number} - дубликат в сиде");
                    totalSkipped++;
                    continue;
                }

                try
                {
                    await catalogService.UpsertSongAsync(song);
                    totalImported++;
                }
                catch (Exception ex)
                {
                    LogToFile($"  Ошибка №{song.Number} «{song.Title}»: {ex.Message}");
                    totalSkipped++;
                }
            }

            if (!string.IsNullOrWhiteSpace(parsed.Warning))
            {
                LogToFile($"Сид warning: {parsed.Warning}");
            }

            LogToFile($"=== Сид завершён: импортировано {totalImported}, пропущено {totalSkipped} ===");
        }
        catch (Exception ex)
        {
            LogToFile($"Ошибка при сиде песен: {ex.Message}");
        }
    }

    private static void ApplyLightweightMigrations(AppDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        connection.Open();

        try
        {
            var hasNumberColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('Songs');";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "Number", StringComparison.OrdinalIgnoreCase))
                    {
                        hasNumberColumn = true;
                        break;
                    }
                }
            }

            if (!hasNumberColumn)
            {
                using var addColumnCommand = connection.CreateCommand();
                addColumnCommand.CommandText = "ALTER TABLE Songs ADD COLUMN Number INTEGER NULL;";
                addColumnCommand.ExecuteNonQuery();
            }

            // Сборники и CollectionId — до любых EF-запросов к Songs
            using (var createCollections = connection.CreateCommand())
            {
                createCollections.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SongCollections (
                        Id TEXT NOT NULL PRIMARY KEY,
                        Name TEXT NOT NULL,
                        Description TEXT NULL,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_SongCollections_Name ON SongCollections (Name);";
                createCollections.ExecuteNonQuery();
            }

            var hasCollectionId = false;
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info('Songs');";
                using var reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "CollectionId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCollectionId = true;
                        break;
                    }
                }
            }

            if (!hasCollectionId)
            {
                using var alter = connection.CreateCommand();
                alter.CommandText = "ALTER TABLE Songs ADD COLUMN CollectionId TEXT NULL;";
                alter.ExecuteNonQuery();
            }

            using (var dropOldNumberIndex = connection.CreateCommand())
            {
                dropOldNumberIndex.CommandText = "DROP INDEX IF EXISTS IX_Songs_Number;";
                dropOldNumberIndex.ExecuteNonQuery();
            }

            using (var createCollectionNumberIndex = connection.CreateCommand())
            {
                createCollectionNumberIndex.CommandText =
                    "CREATE UNIQUE INDEX IF NOT EXISTS IX_Songs_Collection_Number ON Songs (CollectionId, Number);";
                createCollectionNumberIndex.ExecuteNonQuery();
            }

            // Одинаковые названия допустимы (разные сборники / редакции)
            using (var rebuildTitleIndex = connection.CreateCommand())
            {
                rebuildTitleIndex.CommandText = @"
                    DROP INDEX IF EXISTS IX_Songs_Title_Language;
                    CREATE INDEX IF NOT EXISTS IX_Songs_Title_Language ON Songs (Title, Language);";
                rebuildTitleIndex.ExecuteNonQuery();
            }

            var existingNumbers = new HashSet<int>(db.Songs
                .Where(s => s.Number != null)
                .Select(s => s.Number!.Value));

            var songsWithoutNumber = db.Songs
                .Where(s => s.Number == null)
                .OrderBy(s => s.CreatedAt)
                .ThenBy(s => s.Title)
                .ToList();

            var nextCandidate = 1;
            foreach (var song in songsWithoutNumber)
            {
                while (existingNumbers.Contains(nextCandidate))
                {
                    nextCandidate++;
                }

                song.Number = nextCandidate;
                existingNumbers.Add(nextCandidate);
                nextCandidate++;
            }

            if (songsWithoutNumber.Count > 0)
            {
                db.SaveChanges();
            }

            // Сбрасываем отслеживание, чтобы избежать конфликтов при последующих апдейтах через UI
            db.ChangeTracker.Clear();

            // Миграция: добавление колонки IsBold в ThemePresets
            var hasIsBoldColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ThemePresets');";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "IsBold", StringComparison.OrdinalIgnoreCase))
                    {
                        hasIsBoldColumn = true;
                        break;
                    }
                }
            }

            if (!hasIsBoldColumn)
            {
                using var addIsBoldCommand = connection.CreateCommand();
                addIsBoldCommand.CommandText = "ALTER TABLE ThemePresets ADD COLUMN IsBold INTEGER NOT NULL DEFAULT 0;";
                addIsBoldCommand.ExecuteNonQuery();
            }

            // Миграция: добавление колонки TextAlignment в ThemePresets
            var hasTextAlignmentColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ThemePresets');";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "TextAlignment", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTextAlignmentColumn = true;
                        break;
                    }
                }
            }

            if (!hasTextAlignmentColumn)
            {
                using var addTextAlignmentCommand = connection.CreateCommand();
                addTextAlignmentCommand.CommandText = "ALTER TABLE ThemePresets ADD COLUMN TextAlignment TEXT DEFAULT 'Center';";
                addTextAlignmentCommand.ExecuteNonQuery();
            }

            // Миграция: анимация переключения секций
            var hasSectionTransitionColumn = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ThemePresets');";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "SectionTransitionMode", StringComparison.OrdinalIgnoreCase))
                    {
                        hasSectionTransitionColumn = true;
                        break;
                    }
                }
            }

            if (!hasSectionTransitionColumn)
            {
                using var addTransitionCommand = connection.CreateCommand();
                // 1 = Fade (по умолчанию для новых/старых стилей)
                addTransitionCommand.CommandText = "ALTER TABLE ThemePresets ADD COLUMN SectionTransitionMode INTEGER NOT NULL DEFAULT 1;";
                addTransitionCommand.ExecuteNonQuery();
            }

            EnsureThemePresetColumn(connection, "TextOutlineEnabled", "ALTER TABLE ThemePresets ADD COLUMN TextOutlineEnabled INTEGER NOT NULL DEFAULT 0;");
            EnsureThemePresetColumn(connection, "TextOutlineThickness", "ALTER TABLE ThemePresets ADD COLUMN TextOutlineThickness REAL NOT NULL DEFAULT 2;");
            EnsureThemePresetColumn(connection, "TextOutlineColor", "ALTER TABLE ThemePresets ADD COLUMN TextOutlineColor TEXT NOT NULL DEFAULT '#000000';");
            EnsureThemePresetColumn(connection, "TextOutlineOpacity", "ALTER TABLE ThemePresets ADD COLUMN TextOutlineOpacity REAL NOT NULL DEFAULT 1;");
            EnsureThemePresetColumn(connection, "UseSeparateBackgrounds", "ALTER TABLE ThemePresets ADD COLUMN UseSeparateBackgrounds INTEGER NOT NULL DEFAULT 0;");
            EnsureThemePresetColumn(connection, "BackgroundPickMode", "ALTER TABLE ThemePresets ADD COLUMN BackgroundPickMode INTEGER NOT NULL DEFAULT 0;");
            EnsureThemePresetColumn(connection, "SelectedSharedWallpaperId", "ALTER TABLE ThemePresets ADD COLUMN SelectedSharedWallpaperId TEXT NULL;");
            EnsureThemePresetColumn(connection, "SelectedSongWallpaperId", "ALTER TABLE ThemePresets ADD COLUMN SelectedSongWallpaperId TEXT NULL;");
            EnsureThemePresetColumn(connection, "SelectedBibleWallpaperId", "ALTER TABLE ThemePresets ADD COLUMN SelectedBibleWallpaperId TEXT NULL;");

            using (var createWallpapers = connection.CreateCommand())
            {
                createWallpapers.CommandText = @"
                    CREATE TABLE IF NOT EXISTS ThemeWallpapers (
                        Id TEXT NOT NULL PRIMARY KEY,
                        ThemePresetId TEXT NOT NULL,
                        FilePath TEXT NOT NULL,
                        DisplayName TEXT NOT NULL,
                        Pool INTEGER NOT NULL DEFAULT 0,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (ThemePresetId) REFERENCES ThemePresets(Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_ThemeWallpapers_ThemePool
                        ON ThemeWallpapers (ThemePresetId, Pool, SortOrder);";
                createWallpapers.ExecuteNonQuery();
            }

            MigrateLegacyBackgroundMediaToWallpapers(connection);

            // Миграция: удаление колонок ColorSecondary и ColorAccent из ThemePresets
            // Проверяем наличие колонок ColorSecondary и ColorAccent
            var hasColorSecondary = false;
            var hasColorAccent = false;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info('ThemePresets');";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var columnName = reader["name"]?.ToString();
                    if (string.Equals(columnName, "ColorSecondary", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColorSecondary = true;
                    }
                    if (string.Equals(columnName, "ColorAccent", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColorAccent = true;
                    }
                }
            }

            // Удаляем колонки, если они существуют (SQLite 3.35.0+)
            // Если версия SQLite не поддерживает DROP COLUMN, используем пересоздание таблицы
            if (hasColorSecondary || hasColorAccent)
            {
                try
                {
                    // Пытаемся использовать DROP COLUMN (поддерживается в SQLite 3.35.0+)
                    if (hasColorSecondary)
                    {
                        using var dropSecondaryCommand = connection.CreateCommand();
                        dropSecondaryCommand.CommandText = "ALTER TABLE ThemePresets DROP COLUMN ColorSecondary;";
                        dropSecondaryCommand.ExecuteNonQuery();
                    }
                    if (hasColorAccent)
                    {
                        using var dropAccentCommand = connection.CreateCommand();
                        dropAccentCommand.CommandText = "ALTER TABLE ThemePresets DROP COLUMN ColorAccent;";
                        dropAccentCommand.ExecuteNonQuery();
                    }
                }
                catch
                {
                    // Если DROP COLUMN не поддерживается, пересоздаем таблицу
                    // Создаем временную таблицу без этих колонок
                    using (var createTempCommand = connection.CreateCommand())
                    {
                        createTempCommand.CommandText = @"
                            CREATE TABLE ThemePresets_new (
                                Id TEXT NOT NULL PRIMARY KEY,
                                Name TEXT NOT NULL,
                                FontFamily TEXT,
                                BaseFontSize REAL,
                                BackgroundMediaPath TEXT,
                                LoopBackgroundMedia INTEGER NOT NULL,
                                ColorPrimary TEXT NOT NULL,
                                ColorBackground TEXT NOT NULL
                            );";
                        createTempCommand.ExecuteNonQuery();
                    }

                    // Копируем данные
                    using (var copyCommand = connection.CreateCommand())
                    {
                        copyCommand.CommandText = @"
                            INSERT INTO ThemePresets_new (Id, Name, FontFamily, BaseFontSize, BackgroundMediaPath, LoopBackgroundMedia, ColorPrimary, ColorBackground)
                            SELECT Id, Name, FontFamily, BaseFontSize, BackgroundMediaPath, LoopBackgroundMedia, ColorPrimary, ColorBackground
                            FROM ThemePresets;";
                        copyCommand.ExecuteNonQuery();
                    }

                    // Удаляем старую таблицу
                    using (var dropOldCommand = connection.CreateCommand())
                    {
                        dropOldCommand.CommandText = "DROP TABLE ThemePresets;";
                        dropOldCommand.ExecuteNonQuery();
                    }

                    // Переименовываем новую таблицу
                    using (var renameCommand = connection.CreateCommand())
                    {
                        renameCommand.CommandText = "ALTER TABLE ThemePresets_new RENAME TO ThemePresets;";
                        renameCommand.ExecuteNonQuery();
                    }
                }
            }

            // Таблица объявлений
            using (var createAnnouncements = connection.CreateCommand())
            {
                createAnnouncements.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Announcements (
                        Id TEXT NOT NULL PRIMARY KEY,
                        Title TEXT NOT NULL,
                        Content TEXT NOT NULL,
                        IsPermanent INTEGER NOT NULL DEFAULT 1,
                        IsPinned INTEGER NOT NULL DEFAULT 0,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_Announcements_List
                        ON Announcements (IsPinned, SortOrder, UpdatedAt);";
                createAnnouncements.ExecuteNonQuery();
            }
        }
        finally
        {
            connection.Close();
        }
    }

    private static void EnsureThemePresetColumn(DbConnection connection, string columnName, string alterSql)
    {
        var exists = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('ThemePresets');";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = alterSql;
        alter.ExecuteNonQuery();
    }

    /// <summary>Переносит BackgroundMediaPath в ThemeWallpapers без SQL-функций, которых нет в bundled SQLite.</summary>
    private static void MigrateLegacyBackgroundMediaToWallpapers(DbConnection connection)
    {
        var toMigrate = new List<(string PresetId, string FilePath)>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = @"
                SELECT p.Id, p.BackgroundMediaPath
                FROM ThemePresets p
                WHERE p.BackgroundMediaPath IS NOT NULL
                  AND trim(p.BackgroundMediaPath) <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM ThemeWallpapers w
                      WHERE w.ThemePresetId = p.Id
                        AND w.Pool = 0
                        AND w.FilePath = p.BackgroundMediaPath
                  );";
            using var reader = select.ExecuteReader();
            while (reader.Read())
            {
                var presetId = reader.GetString(0);
                var filePath = reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(presetId) && !string.IsNullOrWhiteSpace(filePath))
                {
                    toMigrate.Add((presetId, filePath.Trim()));
                }
            }
        }

        foreach (var (presetId, filePath) in toMigrate)
        {
            var wallpaperId = Guid.NewGuid().ToString();
            var displayName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = filePath;
            }

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = @"
                    INSERT INTO ThemeWallpapers (Id, ThemePresetId, FilePath, DisplayName, Pool, SortOrder)
                    VALUES ($id, $themeId, $path, $name, 0, 0);";
                var pId = insert.CreateParameter();
                pId.ParameterName = "$id";
                pId.Value = wallpaperId;
                insert.Parameters.Add(pId);
                var pTheme = insert.CreateParameter();
                pTheme.ParameterName = "$themeId";
                pTheme.Value = presetId;
                insert.Parameters.Add(pTheme);
                var pPath = insert.CreateParameter();
                pPath.ParameterName = "$path";
                pPath.Value = filePath;
                insert.Parameters.Add(pPath);
                var pName = insert.CreateParameter();
                pName.ParameterName = "$name";
                pName.Value = displayName;
                insert.Parameters.Add(pName);
                insert.ExecuteNonQuery();
            }

            using var update = connection.CreateCommand();
            update.CommandText = @"
                UPDATE ThemePresets
                SET SelectedSharedWallpaperId = $wallpaperId
                WHERE Id = $presetId
                  AND SelectedSharedWallpaperId IS NULL;";
            var uWall = update.CreateParameter();
            uWall.ParameterName = "$wallpaperId";
            uWall.Value = wallpaperId;
            update.Parameters.Add(uWall);
            var uPreset = update.CreateParameter();
            uPreset.ParameterName = "$presetId";
            uPreset.Value = presetId;
            update.Parameters.Add(uPreset);
            update.ExecuteNonQuery();
        }
    }

        private static IHost CreateHostBuilder()
        {
            return Host.CreateDefaultBuilder()
                .UseContentRoot(AppContext.BaseDirectory)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddDbContext<AppDbContext>(options =>
                    {
                        var dbPath = DatabasePathProvider.GetDatabasePath();
                        options.UseSqlite($"Data Source={dbPath}");

                        if (context.HostingEnvironment.IsDevelopment())
                        {
                            options.EnableDetailedErrors();
                            options.EnableSensitiveDataLogging();
                        }
                    });

                    services.AddScoped<ICatalogService, CatalogService>();
                    services.AddScoped<IAnnouncementService, AnnouncementService>();
                    services.AddSingleton<IProjectionStateService, ProjectionStateService>();
                    services.AddSingleton<IDisplaySettingsService, DisplaySettingsService>();
                    services.AddSingleton<IHotkeyService, HotkeyService>();
                    services.AddSingleton<HotkeyDispatcher>();
                    services.AddSingleton<ICameraStreamService, CameraStreamService>();
                    
                    // NDI receiver service — опциональный.
                    // Важно: NdiReceiverService кидает DllNotFoundException в конструкторе,
                    // поэтому регистрируем через фабрику: при отсутствии NDI подставляем "null"-сервис,
                    // чтобы приложение не падало при открытии Settings/Live.
                    services.AddSingleton<INdiReceiverService>(_ =>
                    {
                        try
                        {
                            return new NdiReceiverService();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[App] NDI disabled: {ex.Message}");
                            return new NullNdiReceiverService();
                        }
                    });
                    
                    services.AddSingleton<IProjectionDisplayService, ProjectionDisplayService>();
                    services.AddSingleton<IBibleService, BibleService>();
                    services.AddSingleton<MainViewModel>();
                    services.AddSingleton<CatalogViewModel>();
                    services.AddSingleton<BibleViewModel>();
                    services.AddSingleton<AnnouncementsViewModel>();
                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<LiveControlViewModel>();
                    services.AddSingleton<ThemePresetEditorViewModel>();
                    services.AddSingleton<SongEditorViewModel>();
                    services.AddSingleton<ProjectionDisplayViewModel>();
                    services.AddSingleton<IYandexDiskService, YandexDiskService>();
                    services.AddSingleton<ICatalogBackupService, CatalogBackupService>();
                    services.AddSingleton<IThemeBackgroundMediaService, ThemeBackgroundMediaService>();
                    services.AddSingleton<IAppUpdateService, AppUpdateService>();
                    services.AddHostedService<CatalogBackupScheduler>();
                    services.AddSingleton<IPresentationImportService, PresentationImportService>();
                    services.AddSingleton<IWebSongImportService, WebSongImportService>();
                    services.AddSingleton<PesnVozrImportService>();
                    services.AddSingleton<SoftProjectorSpsImportService>();
                })
                .Build();
        }
    }
}
