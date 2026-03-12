# Win2D 3D Audio Visualizer

## Summary

Replace the current XAML Rectangle-based spectrum visualizer with a GPU-accelerated Win2D renderer supporting 4 modes: Classic (existing), Bars (perspective), Circle (radial), and Wave (surface grid). Includes configurable color, glow, reflection, background blur, and dark overlay.

## Architecture

- `CanvasAnimatedControl` (Win2D) replaces direct Rectangle manipulation for the 3 new modes
- `SpectrumAnalyzer` remains unchanged — only the rendering layer changes
- `DispatcherTimer` kept for Classic mode only; Win2D modes use their own GPU draw loop

### Components

- **`IVisualizerMode`** — common interface for all 4 modes
- **`ClassicMode`** — wraps existing Rectangle/Canvas rendering (no Win2D)
- **`BarsMode`** — perspective bars with depth rows (3-4 echo rows)
- **`CircleMode`** — radial bar layout (360°) with slow rotation
- **`WaveMode`** — grid surface (64x16) with wireframe perspective view
- **`VisualizerRenderer`** — orchestrates canvas, mode switching, effects pipeline

### Data Flow

```
CanvasAnimatedControl.Draw (60fps native)
  → _spectrum.GetSpectrum(position, bandCount)
  → activeMode.Render(session, bands, settings)
    → draw shapes + effects (glow, reflection, blur)
```

## Visualization Modes

### Classic (existing)
- Current dual-bar Rectangle rendering via DispatcherTimer + WaveformCanvas
- Respects 30/60 FPS setting
- No Win2D dependency

### Bars (perspective)
- Rows of bars viewed at ~30° tilt angle
- Front row = current spectrum, back rows = previous frames (echo/trail)
- Perspective: back rows smaller and more transparent
- 3-4 depth rows

### Circle
- Bars arranged in a 360° circle, pointing outward
- Base radius proportional to container size
- Bar height = frequency magnitude
- Slow continuous rotation

### Wave
- Point grid (e.g., 64x16) forming a surface
- Y height of each point = frequency band magnitude
- Depth columns = previous frames (like Bars)
- Connected lines or wireframe mesh, perspective view

## Visual Effects (common to Bars/Circle/Wave)

- **Glow**: `GaussianBlurEffect` on shape copy, additive blending, intensity proportional to magnitude
- **Reflection**: inverted copy below center line, 30% opacity, slight blur
- **Background blur**: light `GaussianBlurEffect` on full canvas when enabled
- **Dark background**: semi-transparent black rectangle (opacity ~0.3) behind everything
- **Color**: user-configurable hex color (default: system accent)

## UI: Mode Selector

Positioned at the top of the visualizer area:

- 4 horizontal text buttons: **Classic · Bars · Circle · Wave**
- Active mode uses accent color, others use secondary text color
- Style matches existing SelectorBar pattern

Right side of selector:
- Toggle glow (icon button)
- Toggle dark background (icon button)
- Color: small colored square that opens hex input (like acrylic custom)

## Settings (persisted in AppSettings)

New fields added to `AppSettings`:
- `VisualizerMode`: `"classic"` | `"bars"` | `"circle"` | `"wave"` (default: `"classic"`)
- `VisualizerColor`: hex string (default: system accent color)
- `VisualizerGlow`: bool (default: `true`)
- `VisualizerDarkBg`: bool (default: `false`)

## Files

### New files
- `Audiomatic/Visualizer/IVisualizerMode.cs`
- `Audiomatic/Visualizer/ClassicMode.cs`
- `Audiomatic/Visualizer/BarsMode.cs`
- `Audiomatic/Visualizer/CircleMode.cs`
- `Audiomatic/Visualizer/WaveMode.cs`
- `Audiomatic/Visualizer/VisualizerRenderer.cs`

### Modified files
- `Audiomatic/MainWindow.xaml` — add CanvasAnimatedControl + mode selector
- `Audiomatic/MainWindow.xaml.cs` — delegate to VisualizerRenderer, keep DispatcherTimer for Classic only
- `Audiomatic/SettingsManager.cs` — add visualizer fields to AppSettings
- `Audiomatic/Audiomatic.csproj` — add Win2D NuGet reference

### Unchanged
- `Audiomatic/Services/SpectrumAnalyzer.cs`
- `Audiomatic/Services/AudioPlayerService.cs`

## Dependencies

- **NuGet**: `Microsoft.Graphics.Win2D` (only addition)
