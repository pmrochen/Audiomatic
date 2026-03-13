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

### Radio Streaming

- **Listen to online radio** — enter any HTTP/HTTPS stream URL to play live radio
- **Station management** — rename and delete saved stations via Raycast-style context menu
- **Persistent stations** — saved to `radio_stations.json`, restored on next launch
- **Live indicator** — timeline shows "LIVE" with disabled seek bar
- **Smart transport controls** — shuffle, repeat, previous, and next are disabled during radio playback
- **Visualizer support** — real-time WASAPI loopback spectrum analysis for live streams

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
| **Radio** | Play online radio streams with station management |
| **Visualizer** | Real-time FFT spectrum analyzer with mirror mode |
| **Media Control** | Monitor and control background media players (SMTC) |

### Metadata Editor

- **Inline tag editing** — edit title, artist, and album directly from the track context menu
- **Artwork management** — change cover art from file (JPG/PNG) or remove existing artwork
- Writes tags back to the audio file via TagLibSharp
- Automatically updates the library database and current playback display

### Media Control

- **Background media monitoring** — detect and display all active system media sessions
- **Per-session cards** — thumbnail, app name, title, artist for each media source
- **Playback controls** — play/pause, previous, next per session
- **Timeline scrubbing** — seek within tracks with real-time position and duration display
- **Live updates** — automatically refreshes when sessions start, stop, or change tracks

### Window & UI

- **Three display modes** — Expanded (710px), Compact (220px), and Mini-player (60px)
- **Mini-player** — ultra-compact mode showing album art, track info, and play/pause button
- **Animated transitions** — Fluent slide+fade animations between views
- **Collapse cycling** with smooth animation anchored to bottom (`Ctrl+L`)
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
  - `Ctrl+L` — Cycle display modes (Expanded → Compact → Mini → Expanded)
  - `Space` — Play/Pause (when not searching)
  - `Escape` — Close window

### Settings

- Library management (add folders, rescan, reset)
- Backdrop and theme selection
- Display mode cycling and pin-on-top toggles
- Visualizer FPS selection (30 / 60 FPS)
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
- `radio_stations.json` — Saved radio stations

## Building

```bash
dotnet build Audiomatic.sln -c Release
```

## License

All rights reserved.
