# Database Setup & Migrations

The application uses SQLite via EF Core 8. This guide documents how to manage migrations and where the database file lives.

## Database Location

- Path: `%LOCALAPPDATA%\ChyguiSlide\catalog.db`
- Managed by `DatabasePathProvider.GetDatabasePath()` to ensure the directory exists for both runtime and tooling scenarios.

## Tooling Prerequisites

```bash
dotnet tool install --global dotnet-ef
```

Ensure the global tools path is in your `PATH` environment variable. The solution already references `Microsoft.EntityFrameworkCore.Design`, so no further package setup is required.

## Adding a Migration

```bash
cd T:\cod\PC\ChyguiSlide
dotnet ef migrations add InitialCreate --project ChyguiSlide.csproj --output-dir Data/Migrations
```

Recommendations:

- Use descriptive migration names (e.g., `AddPlaylistHistory`, `UpdateTagColors`).
- Store migrations in `Data/Migrations` to keep context with the persistence layer.

## Updating the Database

```bash
dotnet ef database update --project ChyguiSlide.csproj
```

This command applies the latest migration to the local SQLite database. The runtime also calls `Database.Migrate()` on startup to ensure schema consistency.

## Resetting the Database

For local development you can delete the `%LOCALAPPDATA%\ChyguiSlide\catalog.db` file to start fresh. After deletion, run the application or `dotnet ef database update` to recreate the schema.

## Seeding Data

Seeding can be added inside `OnModelCreating` using `modelBuilder.Entity<...>().HasData(...)`. For more advanced bootstrap logic, consider an `IDatabaseInitializer` service invoked after migrations in `App.OnLaunched`.

