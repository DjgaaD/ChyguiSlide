# Import & Cloud Sync Strategy

This document outlines how the application ingests presentation assets and synchronizes with selected cloud providers (Yandex Disk, Mail.ru Cloud). The approach emphasizes background processing, resiliency, and metadata enrichment for the catalog.

## Import Pipeline

### Entry Points
- **Manual Import Wizard**: drag-drop files (`.ppt`, `.pptx`, `.odp`, `.doc`, `.docx`, `.txt`). Allows tagging, playlist selection, and theme preset suggestions before kickoff.
- **Watched Folders**: optional background watcher for rehearsal folders; drops create pending import jobs.
- **Cloud Sync Feed**: cloud connectors publish file change events into the same queue.

### Processing Flow

```text
UI Thread → ImportRequest → BackgroundChannel
BackgroundChannel → ImportWorker (hosted service)
ImportWorker → Strategy resolution by file type
Strategy → Extract / Normalize → Song + Sections + Attachments
EF Core transaction → store entities → enqueue post-processing (preview render)
```

1. **Queueing**: `IBackgroundChannel` (based on `Channel<ImportJobRequest>`) handles backpressure. Jobs persisted in `ImportJobs` table.
2. **Strategy Pattern**: `IImportStrategy` interface with implementations:
   - `PowerPointImportStrategy` (PPT/PPTX/ODP) using `Aspose.Slides` or `Open XML SDK` + LibreOffice headless as fallback.
   - `WordImportStrategy` (DOC/DOCX) leveraging `Open XML SDK` or `Aspose.Words`.
   - `PlainTextImportStrategy` for `.txt`, `.lyric`, `.chordpro` with stanza detection heuristics.
3. **Normalization**:
   - Extract text runs, split into sections (apply heuristics: headings, blank lines, repeated content).
   - Capture formatting cues (font size, bold/italic) as hints for section types.
   - Convert chords from bracket notation if detected.
4. **Post Processing**:
   - Generate projector preview assets (PNG thumbnails) via Win2D headless render.
   - Populate `Attachment` entries for original files and generated PDFs.
   - Update fuzzy search index (FTS5 table).
5. **Error Handling**:
   - Exceptions stored in `ImportJob.ErrorMessage` with user-facing toast + history list.
   - Partial results rolled back via transaction scope.

## Cloud Synchronization

### Common Infrastructure
- `ICloudConnector` interface with operations: `ListAsync`, `DownloadAsync`, `UploadAsync`, `SubscribeChangesAsync`.
- Connectors run in hosted services with token refresh, delta sync, retry policies (Polly).
- Files mirrored into local cache directory (`%LOCALAPPDATA%/ChyguiSlide/cloud-cache`).
- Metadata persisted via `CloudLocation` value object on `Attachment` entities.

### Yandex Disk Connector
- OAuth 2.0 Device Code flow (`https://oauth.yandex.ru`).
- REST API (`cloud-api.yandex.net/v1/disk/resources`) for listing, uploads, downloads.
- Delta sync using `resource/last-uploaded` and `public/resources` endpoints.
- Push notifications via long-polling `operations?limit=...` for near-real-time updates.
- Supports publishing to shared links for distribution to remote teams.

### Mail.ru Cloud Connector
- Authentication with OAuth 2.0 (`https://oauth.mail.ru`).
- API (`cloud-api.mail.ru/v2/`) for listing, uploading chunks.
- `ETag`-based change detection, incremental sync via `delta` endpoint.
- Handles provider-specific rate limits and retries with exponential backoff.

### Conflict Resolution
- File hash comparison (`SHA-256`) to detect modifications.
- When remote > local, create new `Attachment` version and mark previous as superseded.
- When local edits occur, prompt user to push updates or keep local copy only.

### Security & Secrets
- Tokens stored using Windows Credential Locker.
- Background services check token expiry and prompt UI via notification center.
- Sensitive operations logged at `Information` level with personally identifiable data scrubbed.

## Scheduling & Background Execution
- Hosted services registered in `App.xaml.cs` using `Host.CreateDefaultBuilder` to run DI pipeline.
- Import workers throttle concurrency (default 2 parallel jobs) to keep UI responsive.
- Sync services backoff when on battery power or metered connection (query via `PowerManager` / `ConnectionProfile`). 

## User Experience
- Import dashboard shows queue, progress bars, ability to cancel or retry jobs.
- Cloud panel lists connected accounts, storage usage, last sync time, manual refresh button.
- Notifications surface via `TeachingTip`/`InfoBar` for success, warnings, or errors.

