# UI Architecture & Screen Flow

The WinUI 3 client follows MVVM with CommunityToolkit.Mvvm planned for view models. Shell navigation is handled by `NavigationView` + `Frame`, while projector output uses a dedicated window hosting high-performance composition surfaces.

## Layers

| Layer | Responsibility | Key Tech |
|-------|----------------|----------|
| Presentation | WinUI 3 XAML views, resource dictionaries, theme system | `NavigationView`, `Page`, `Window`, `ResourceDictionary` |
| ViewModel | State coordination, commands, projection orchestrators | `ObservableObject`, `ObservableCollection`, `ICommand` |
| Services | Domain logic (catalog, playlists, projector, imports) | DI via `Microsoft.Extensions.Hosting`, EF Core repositories |
| Infrastructure | Persistence, background jobs, cloud connectors | EF Core, SQLite, BackgroundTask queue |

## Windows & Shell

1. **Primary Shell (`MainWindow`)**
   - Hosts navigation sidebar, app commands, status indicators.
   - Content frame displays catalog, editor, playlists, analytics.
   - Command palette overlay triggered via `Ctrl+Shift+P` with incremental search.
2. **Projector Window**
   - Secondary window full-screen on external display.
   - Renders lyrics using `CanvasAnimatedControl` (Win2D) for smooth transitions.
   - Receives state updates through `ProjectionController` service and message bus.
3. **Preview/Notes Pane**
   - Optional detachable window showing next sections, operator notes.

## Navigation Flow

```text
MainWindow (NavigationView)
├── Dashboard (summary, quick actions)
├── CatalogView
│   ├── SongList (grid w/ filters, fuzzy search, tags)
│   ├── SongDetails (sections, metadata, attachments)
│   └── AnalyticsPanel (usage stats)
├── EditorView
│   ├── SectionOutline (TreeView)
│   ├── LyricsEditor (Markdown/chord editor w/ preview)
│   └── PropertiesPanel (tempo, tags, themes)
├── PlaylistView
│   ├── PlaylistBoard (drag/drop ordering)
│   ├── LiveNotes ( cues, transpositions)
│   └── SetPreview (projection mockup)
├── LiveControlView
│   ├── CurrentSlide (synchronized with projector)
│   ├── NextQueue (upcoming sections)
│   ├── Hotkeys & MIDI mapper
│   └── ThemeSwitcher (one-click presets)
└── SettingsView
    ├── Themes
    ├── Imports & Cloud
    └── Advanced (diagnostics, database tools)
```

## Component Highlights

- **Catalog Filters**: `AutoSuggestBox`, `TagChips`, pivot for favorites/history.
- **Lyrics Editor**: `TabView` for split view (raw vs preview), syntax highlighting via `TextEditor` control (WinUI Community Toolkit).
- **Playlists**: `ItemsRepeater` + `DragDrop` APIs for ordering, context menu for overrides.
- **Live Control**: `CommandBar` with grouped actions, `NumberBox` for transpose, `ToggleSwitch` for projector freeze/blackout.
- **Projector Rendering**: `CompositionProjectionSurface` for layering background video, fog, blur; text layout via Win2D `CanvasTextLayout` and gradient fills.

## State Synchronization

- **ProjectionController Service** broadcasts `ProjectionState` using `ObservableObject` + `WeakReferenceMessenger` (CommunityToolkit).
- **Undo/Redo**: `IHistoryService` maintains command stack per document; UI binds to `CanUndo`/`CanRedo` properties.
- **Command Palette**: uses `ICommandPaletteProvider` to aggregate commands from modules; results rendered in overlay `ContentDialog` style.

## Accessibility & Theming

- Resource dictionaries for light/dark + high-contrast variants.
- Typography scales with `ThemePreset` data; preview updates live before applying to projector.
- Keyboard-first navigation: assign access keys, focus visuals, screen reader names.

## Extension Points

- Modular pages register with shell via `INavigationRegistry` for late-loading (e.g., analytics module built with WebView2/MAUI hybrid).
- Projector effects pipeline exposes `IVisualEffect` interface for custom transitions.
- Import wizards plug in using `IImportStrategy` with metadata for supported formats.

