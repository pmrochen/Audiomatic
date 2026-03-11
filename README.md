# Audiotomatic

A modern, compact desktop audio player for Windows 11, built with WinUI 3 and .NET 8.

![Windows](https://img.shields.io/badge/platform-Windows%2011-0078D4?logo=windows11)
![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![WinUI 3](https://img.shields.io/badge/WinUI-3-blue)

## Features

### Audio Playback

- **Dual audio engine** — MediaPlayer for standard formats, NAudio (WASAPI) for advanced ones
- **Supported formats**: MP3, FLAC, WAV, OGG, AAC, WMA, M4A, OPUS, APE, AIFF
- **Playback controls**: Play/Pause, Previous, Next, timeline seeking
- **Volume control** with mute toggle and dynamic icon states
- **Shuffle** (Fisher-Yates) and **Repeat** modes (None / All / One)

### Music Library

- Add folders and recursively scan for audio files
- Automatic metadata extraction (title, artist, album, genre, track number, year, duration, artwork)
- File hash-based deduplication
- Background async scanning with progress reporting
- SQLite-backed persistent storage

### Playlists

- Create, rename, and delete playlists
- Add/remove tracks and reorder within playlists
- Quick "Add to Playlist" from any track context menu

### Queue Management

- Add tracks to queue (end or next)
- Reorder tracks via up/down buttons
- Clear queue or remove individual items
- Queue state persisted across sessions (`queue.json`)

### Search & Sort

- Real-time filtering by title, artist, or album
- Sort by Title, Artist, Album, or Duration
- Ascending/Descending toggle

### Favorites

- Mark/unmark tracks as favorites
- Persisted in the local database

### Navigation Views

| View | Description |
|------|-------------|
| **Library** | Browse all tracks with search and sort controls |
| **Playlists** | Manage playlists and view track counts |
| **Playlist Detail** | View and reorder tracks within a playlist |
| **Queue** | View and manage the current playback queue |

### Window & UI

- **Compact design** — 380x710px (collapsible to 380x220px)
- **Collapse/Expand** with smooth animation anchored to bottom
- **Always-on-Top** pin mode
- **Backdrop options**: Acrylic, Mica, Mica Alt, None
- **Theme support**: System, Light, Dark
- **Window position** remembered across restarts
- **Custom draggable title bar**
- **Raycast-style context menus** for tracks, playlists, and queue items

### System Integration

- **System tray** — minimize to tray, left-click to show/hide, right-click menu
- **System Media Transport Controls (SMTC)** — play/pause, next/previous, track info and artwork displayed in Windows media overlay
- **Drag & Drop** — drop audio files from Explorer to play or queue them
- **Global hotkeys**:
  - `Ctrl+Alt+M` — Show/Hide window
  - `Ctrl+L` — Toggle compact mode
  - `Space` — Play/Pause (when not searching)
  - `Escape` — Close window

### Settings

- Library management (add folders, rescan, reset)
- Backdrop and theme selection
- Compact mode and pin-on-top toggles
- All preferences persisted in `settings.json`

## Tech Stack

| Component | Technology |
|-----------|------------|
| Framework | WinUI 3 (Windows App SDK 1.8) |
| Language | C# / .NET 8.0 |
| Audio | Windows MediaPlayer + NAudio 2.2.1 (WASAPI) |
| Metadata | TagLibSharp 2.3.0 |
| Database | Microsoft.Data.Sqlite 8.0.11 |
| Target | Windows 10.0.19041.0+ |

## Data Storage

All application data is stored in `%LOCALAPPDATA%\Audiomatic\`:

- `library.db` — SQLite database (tracks, playlists, favorites, folders)
- `settings.json` — User preferences
- `queue.json` — Current queue state

## Building

```bash
dotnet build Audiomatic.sln -c Release
```

## License

All rights reserved.
