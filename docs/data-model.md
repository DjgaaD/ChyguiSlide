# Data Model Overview

The application keeps content, presentation metadata, and operational history in a local SQLite database accessed through EF Core 8. The data model balances normalized storage with projection-friendly views for the WinUI presentation layer.

## Entity Catalogue

- **Song**
  - Core metadata: title, subtitle, language, tempo, default key, copyright.
  - Flags: `IsFavorite`, `IsArchived`, `IsPublished`.
  - Navigation: `Sections`, `Tags`, `Performances`, `Attachments`.
- **SongSection**
  - Owns lyrical content chunks (verse, chorus, bridge, etc.).
  - Maintains ordered position, section type enum, optional annotations.
  - Supports rich text payload (Markdown-like + chord markup) and compiled presentation blocks.
- **Tag**
  - Simple taxonomy for thematic grouping (season, mood, service type).
  - Many-to-many with songs.
- **Playlist**
  - Represents rehearsal sets and live services.
  - Holds ordering, scheduled date/time, context (event type, location).
  - Navigation: `PlaylistEntries` and performance notes.
- **PlaylistEntry**
  - Joins playlist with a specific song or attachment.
  - Stores transposition adjustments, tempo overrides, cues for lighting/video.
- **PerformanceHistory**
  - Auditable log of songs shown on projector.
  - Tracks playlist reference, operator, timestamps, projector theme, notes.
- **Attachment**
  - External resources linked to a song (PDF charts, audio refs, backing tracks).
  - Stores local path, cloud drive URI, MIME type, version info.
- **ImportJob**
  - Persists background import tasks (PPT, DOC, text, cloud sync).
  - Captures source, status, progress, error details.
- **ThemePreset**
  - Stores projector styling choices (fonts, palette, background media).
  - Referenced per playlist or performance session.

## Relationships

```text
Song 1 ── * SongSection
Song * ── * Tag          (bridge table SongTag)
Song 1 ── * PerformanceHistory
Song * ── * Playlist     (through PlaylistEntry)
Playlist 1 ── * PlaylistEntry
Playlist 1 ── * PerformanceHistory
ThemePreset 1 ── * PerformanceHistory
Song 1 ── * Attachment
ImportJob 1 ── * Attachment (optional, final result mapping)
```

## EF Core Configuration

- `AppDbContext` exposed via dependency injection; uses `UseSqlite` with connection resiliency and WAL mode.
- Entities mapped with Fluent API to keep classes clean; configure enums as strings for readability.
- `Owned` types for structured fields (e.g., `SectionTiming`, `ThemeColors`, `CloudLocation`).
- Soft delete maintained via `IsArchived` flags with query filters.
- Migrations stored under `Data/Migrations`; initial migration seeds starter tags and sample songs.

## Indexing & Constraints

- Unique constraint on `Song.Title` + `Language` for duplicate prevention.
- Full-text search via `FTS5` virtual table shadowing `Song` + `SongSection` content.
- Index combos:
  - `SongSection(SongId, Order)`, `PlaylistEntry(PlaylistId, Order)`.
  - `PerformanceHistory(PlayedAt DESC)` for reporting views.

## Future Extensions

- Multi-tenant profiles by adding `OrganizationId` or `CampusId` to core entities.
- Sync journals per entity to reconcile with cloud storage.
- Telemetry tables for user interactions (optional, behind privacy toggle).

