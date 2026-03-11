# Design: Animated Transitions, Mini-Player & Metadata Editor

**Date:** 2026-03-11
**Status:** Approved
**Branch:** 0.0.1

## Overview

Three features for Audiomatic:
1. Animated slide+fade transitions between views
2. Ultra-compact mini-player mode (3rd collapse state)
3. Inline metadata editor in the Raycast-style context menu

---

## 1. Animated View Transitions

### Behavior
When switching between Library, Playlists, Queue, and Visualizer views, the outgoing content slides left and fades out, then incoming content slides in from the right and fades in.

### Implementation
- New method `AnimateViewTransition(Action buildNewContent)` in `MainWindow.xaml.cs`
- Adds a `TranslateTransform` to the active container (`TrackListView` or `WaveformContainer`)
- Two-phase Storyboard animation:
  - **Exit phase** (~150ms): `Opacity` 1→0, `TranslateTransform.X` 0→-30, CubicBezier easing
  - **Enter phase** (~150ms): `Opacity` 0→1, `TranslateTransform.X` 30→0, CubicBezier easing
- Callback `buildNewContent` runs between phases to rebuild content
- Applied in `NavLibrary_Click`, `NavPlaylists_Click`, `NavQueue_Click`, `NavVisualizer_Click`
- No animation when navigating to the already-active view

### Files Modified
- `MainWindow.xaml.cs` — add `AnimateViewTransition()`, update Nav*_Click handlers

---

## 2. Mini-Player Ultra-Compact Mode

### Behavior
Third collapse state cycling: **expanded (710px)** → **compact (220px)** → **mini (60px)** → **expanded**.

Mini-player layout (380x60):
```
┌──────────────────────────────────────┐
│ [cover 40x40] Title - Artist    [>]  │
└──────────────────────────────────────┘
```

### Implementation
- New constant `_miniHeight = 60`
- `ToggleCollapse()` cycles through 3 states: expanded → compact → mini → expanded
- `Ctrl+L` cycles in the same order
- Collapse button icon updates per state:
  - Expanded: chevron down (compact)
  - Compact: chevron down (mini)
  - Mini: chevron up (expand)
- In mini mode, visible elements:
  - `NowPlayingCard` in compact inline layout: cover 40x40, title + artist on one line, play/pause button at right
- Hidden in mini mode: title bar buttons (except close), timeline, playback controls, volume, nav tabs, search/sort, track list, bottom bar
- Reuses existing `AnimTick` animation system with `_targetHeight`
- Pin on top respects existing user setting (no forced always-on-top)

### Files Modified
- `MainWindow.xaml` — add mini-player layout variant in Row 1 (inline NowPlayingCard with play/pause)
- `MainWindow.xaml.cs` — modify `ToggleCollapse()`, add mini state to cycle, visibility management

---

## 3. Inline Metadata Editor

### Behavior
New "Edit Tags" option in the track context menu (Raycast-style Flyout). Opens an inline edit panel within the same Flyout, replacing the menu content.

### Edit Panel Layout
```
┌─────────────────────────────┐
│ <- Edit Tags                │
│ ─────────────────────────── │
│ [artwork 64x64] [Change][x] │
│ Title   [_______________]   │
│ Artist  [_______________]   │
│ Album   [_______________]   │
│ ─────────────────────────── │
│ [Save]              [Cancel]│
└─────────────────────────────┘
```

### Implementation

**UI (MainWindow.xaml.cs):**
- New `BuildMetadataEditorContent(Flyout flyout, TrackInfo track)` method
- "Edit Tags" button added in `BuildTrackContextContent()` with separator, before destructive actions
- On click, replaces Flyout content with the editor panel
- Back button "←" restores the original context menu
- 3 TextBox fields: Title, Artist, Album (pre-filled from TrackInfo)
- Artwork section: 64x64 preview Image, "Change" button (opens FilePicker for .jpg/.png), "x" button (removes artwork)
- "Save" button: writes tags to file, updates DB, refreshes UI
- "Cancel" button: closes Flyout

**Tag Writing (new Services/MetadataWriter.cs):**
- `MetadataWriter.WriteTagsAsync(string filePath, string title, string artist, string album, byte[]? artwork)`
- Uses `TagLib.File.Create(path)` to read+write
- Sets `Tag.Title`, `Tag.FirstPerformer` (via `Tag.Performers`), `Tag.Album`
- For artwork: sets `Tag.Pictures` with new `TagLib.Picture` from byte array, or clears if null
- Calls `tagFile.Save()`
- Returns success/error result

**Database Update (LibraryManager.cs):**
- New `UpdateTrackMetadata(long trackId, string title, string artist, string album)` method
- Simple UPDATE query on tracks table

**Post-Save Refresh:**
- Reloads `_allTracks` and calls `ApplyFilterAndSort()`
- If the edited track is the current playing track, updates NowPlaying display and SMTC metadata

**Error Handling:**
- If file is locked (NAudio playback), TagLibSharp throws IOException
- Caught and displayed as inline error text in the editor panel ("File is currently playing, try again after stopping playback")
- No crash, no data loss

### Files Modified
- `MainWindow.xaml.cs` — add `BuildMetadataEditorContent()`, update `BuildTrackContextContent()`
- `Services/LibraryManager.cs` — add `UpdateTrackMetadata()`
- `Services/MetadataWriter.cs` — new file

---

## Dependencies

No new NuGet packages required. TagLibSharp (already referenced) handles both reading and writing metadata including artwork.

## Risks

- **File locking during playback**: NAudio may hold the file. Mitigated by catching IOException and showing user-friendly message.
- **Animation performance**: Storyboard with TranslateTransform is lightweight. No Composition API needed for 150ms transitions.
- **Mini-player cycling UX**: 3-state cycle may be unfamiliar. Tooltip on collapse button clarifies the next state.
