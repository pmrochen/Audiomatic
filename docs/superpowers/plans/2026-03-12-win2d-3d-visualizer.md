# Win2D 3D Audio Visualizer Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the XAML Rectangle visualizer with a Win2D GPU-accelerated renderer supporting 4 modes (Classic, Bars, Circle, Wave) with configurable color, glow, reflection, and dark background.

**Architecture:** A `VisualizerRenderer` orchestrates mode switching between `IVisualizerMode` implementations. Classic mode reuses the existing DispatcherTimer+Canvas approach. The 3 new modes render via `CanvasAnimatedControl` (Win2D) with shared effects pipeline (glow, reflection, background blur). A mode selector bar sits above the canvas.

**Tech Stack:** Win2D (`Microsoft.Graphics.Win2D`), WinUI 3, NAudio (existing SpectrumAnalyzer)

---

## File Structure

### New files
| File | Responsibility |
|------|---------------|
| `Audiomatic/Visualizer/IVisualizerMode.cs` | Interface + VisualizerSettings record |
| `Audiomatic/Visualizer/EffectsHelper.cs` | Shared Win2D effects (glow, reflection, blur) |
| `Audiomatic/Visualizer/BarsMode.cs` | Perspective bars with depth echo rows |
| `Audiomatic/Visualizer/CircleMode.cs` | Radial 360° bar layout with rotation |
| `Audiomatic/Visualizer/WaveMode.cs` | Wireframe surface grid |
| `Audiomatic/Visualizer/VisualizerRenderer.cs` | Orchestrates canvas, mode switching, selector UI |

### Modified files
| File | Changes |
|------|---------|
| `Audiomatic/Audiomatic.csproj` | Add Win2D NuGet reference |
| `Audiomatic/SettingsManager.cs` | Add visualizer fields to AppSettings |
| `Audiomatic/MainWindow.xaml` | Add `CanvasAnimatedControl` + selector row in WaveformContainer |
| `Audiomatic/MainWindow.xaml.cs` | Delegate to VisualizerRenderer, keep Classic mode logic in place |

### Unchanged
| File | Reason |
|------|--------|
| `Audiomatic/Services/SpectrumAnalyzer.cs` | Data source stays the same |
| `Audiomatic/Services/AudioPlayerService.cs` | Playback logic unchanged |

---

## Chunk 1: Foundation (Tasks 1-3)

### Task 1: Add Win2D dependency and visualizer settings

**Files:**
- Modify: `Audiomatic/Audiomatic.csproj`
- Modify: `Audiomatic/SettingsManager.cs`

- [ ] **Step 1: Add Win2D NuGet package**

```xml
<!-- Add to Audiomatic.csproj ItemGroup with other PackageReferences -->
<PackageReference Include="Microsoft.Graphics.Win2D" Version="1.3.2" />
```

- [ ] **Step 2: Add visualizer fields to AppSettings**

In `SettingsManager.cs`, update the `AppSettings` record:

```csharp
public record AppSettings(
    BackdropSettings Backdrop,
    double Volume,
    bool ShuffleEnabled,
    string RepeatMode,
    string SortBy,
    bool SortAscending,
    string Language,
    string Theme = "system",
    int VisualizerFps = 30,
    string VisualizerMode = "classic",       // "classic", "bars", "circle", "wave"
    string VisualizerColor = "",             // hex string, empty = system accent
    bool VisualizerGlow = true,
    bool VisualizerDarkBg = false,
    int? WindowX = null,
    int? WindowY = null);
```

Add helper methods:

```csharp
public static string LoadVisualizerMode() => Load().VisualizerMode ?? "classic";
public static void SaveVisualizerMode(string mode)
{
    var current = Load();
    Save(current with { VisualizerMode = mode });
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add Audiomatic/Audiomatic.csproj Audiomatic/SettingsManager.cs
git commit -m "feat: Add Win2D dependency and visualizer settings fields"
```

---

### Task 2: Create IVisualizerMode interface and EffectsHelper

**Files:**
- Create: `Audiomatic/Visualizer/IVisualizerMode.cs`
- Create: `Audiomatic/Visualizer/EffectsHelper.cs`

- [ ] **Step 1: Create IVisualizerMode.cs**

```csharp
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;

namespace Audiomatic.Visualizer;

public record VisualizerSettings(
    string Color,           // hex string, empty = accent
    bool GlowEnabled,
    bool DarkBackground);

public interface IVisualizerMode
{
    /// <summary>Band count this mode wants for the given canvas size.</summary>
    int GetBandCount(float width, float height);

    /// <summary>Render one frame. Called from CanvasAnimatedControl.Draw.</summary>
    void Render(CanvasDrawingSession session, float[] bands, float width, float height,
        VisualizerSettings settings);
}
```

- [ ] **Step 2: Create EffectsHelper.cs**

```csharp
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Windows.UI;

namespace Audiomatic.Visualizer;

internal static class EffectsHelper
{
    /// <summary>Parse hex color string to Windows.UI.Color. Falls back to accent-like blue.</summary>
    internal static Color ParseColor(string hex)
    {
        hex = (hex ?? "").TrimStart('#');
        if (hex.Length != 6)
            return Color.FromArgb(255, 96, 165, 250); // fallback blue
        return Color.FromArgb(255,
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    /// <summary>Get the render color — user color or system accent.</summary>
    internal static Color GetRenderColor(VisualizerSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Color))
            return ParseColor(settings.Color);
        // Fallback: use a bright accent blue (system accent not accessible from non-UI thread)
        return Color.FromArgb(255, 96, 165, 250);
    }

    /// <summary>Draw dark semi-transparent background overlay.</summary>
    internal static void DrawDarkBackground(CanvasDrawingSession session, float width, float height)
    {
        session.FillRectangle(0, 0, width, height, Color.FromArgb(76, 0, 0, 0));
    }

    /// <summary>Apply glow effect by drawing a blurred copy of the command list.</summary>
    internal static void DrawGlow(CanvasDrawingSession session, CanvasCommandList commandList, float blurAmount = 12f)
    {
        var blur = new GaussianBlurEffect
        {
            Source = commandList,
            BlurAmount = blurAmount,
            BorderMode = EffectBorderMode.Soft
        };
        session.DrawImage(blur);
    }

    /// <summary>Draw reflection: flipped, faded, blurred copy below centerY.</summary>
    internal static void DrawReflection(CanvasDrawingSession session, CanvasCommandList commandList,
        float width, float height, float centerY, float opacity = 0.3f)
    {
        var transform = new Transform2DEffect
        {
            Source = commandList,
            TransformMatrix = Matrix3x2.CreateScale(1, -1, new Vector2(0, centerY))
        };
        var blur = new GaussianBlurEffect
        {
            Source = transform,
            BlurAmount = 4f
        };
        var old = session.Transform;
        session.DrawImage(blur, 0, 0, new Windows.Foundation.Rect(0, centerY, width, height - centerY), opacity);
        session.Transform = old;
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 4: Commit**

```bash
git add Audiomatic/Visualizer/
git commit -m "feat: Add IVisualizerMode interface and EffectsHelper"
```

---

### Task 3: Implement BarsMode

**Files:**
- Create: `Audiomatic/Visualizer/BarsMode.cs`

- [ ] **Step 1: Create BarsMode.cs**

```csharp
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Audiomatic.Visualizer;

public sealed class BarsMode : IVisualizerMode
{
    private const int DepthRows = 4;
    private const float BarWidth = 5f;
    private const float BarGap = 2f;
    private const float TiltAngle = 0.25f; // perspective tilt factor
    private const float DepthSpacing = 14f;

    // Ring buffer for previous frames
    private readonly float[][] _history = new float[DepthRows][];
    private int _historyIndex;
    private int _frameCount;

    public int GetBandCount(float width, float height)
    {
        return Math.Max(1, (int)(width / (BarWidth + BarGap)));
    }

    public void Render(CanvasDrawingSession session, float[] bands, float width, float height,
        VisualizerSettings settings)
    {
        var baseColor = EffectsHelper.GetRenderColor(settings);
        float centerY = height * 0.55f;
        float halfMax = height * 0.40f;
        int bandCount = bands.Length;

        // Store current frame in history
        if (_history[0] == null || _history[0].Length != bandCount)
        {
            for (int i = 0; i < DepthRows; i++)
                _history[i] = new float[bandCount];
        }
        Array.Copy(bands, _history[_historyIndex], bandCount);
        _historyIndex = (_historyIndex + 1) % DepthRows;
        _frameCount++;

        if (settings.DarkBackground)
            EffectsHelper.DrawDarkBackground(session, width, height);

        using var glowLayer = settings.GlowEnabled ? new CanvasCommandList(session) : null;
        var drawSession = settings.GlowEnabled ? glowLayer!.CreateDrawingSession() : session;

        float totalWidth = bandCount * (BarWidth + BarGap);
        float offsetX = (width - totalWidth) / 2f;

        // Draw back rows first (painter's order)
        for (int row = DepthRows - 1; row >= 0; row--)
        {
            int histIdx = ((_historyIndex - 1 - row) % DepthRows + DepthRows) % DepthRows;
            var rowBands = _history[histIdx];
            if (_frameCount <= row) continue;

            float depthFactor = 1f - row * 0.18f; // scale down with depth
            float rowAlpha = (byte)(255 * (1f - row * 0.22f));
            float yOffset = row * DepthSpacing * TiltAngle;

            var color = Color.FromArgb((byte)rowAlpha, baseColor.R, baseColor.G, baseColor.B);

            for (int i = 0; i < bandCount && i < rowBands.Length; i++)
            {
                float x = offsetX + i * (BarWidth + BarGap);
                float barH = Math.Max(2f, rowBands[i] * halfMax * depthFactor);
                float y = centerY - barH - yOffset;

                drawSession.FillRoundedRectangle(x, y, BarWidth * depthFactor, barH, 2, 2, color);
            }
        }

        if (settings.GlowEnabled && glowLayer != null)
        {
            drawSession.Dispose();
            // Draw the glow (blurred copy) first, then the sharp shapes on top
            EffectsHelper.DrawGlow(session, glowLayer);
            session.DrawImage(glowLayer);

            // Reflection
            EffectsHelper.DrawReflection(session, glowLayer, width, height, centerY);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Audiomatic/Visualizer/BarsMode.cs
git commit -m "feat: Implement BarsMode with perspective depth rows"
```

---

## Chunk 2: Circle and Wave Modes (Tasks 4-5)

### Task 4: Implement CircleMode

**Files:**
- Create: `Audiomatic/Visualizer/CircleMode.cs`

- [ ] **Step 1: Create CircleMode.cs**

```csharp
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace Audiomatic.Visualizer;

public sealed class CircleMode : IVisualizerMode
{
    private const int BandCount = 64;
    private const float MinRadius = 0.15f; // fraction of min(w,h)/2
    private const float MaxBarLength = 0.30f; // fraction of min(w,h)/2
    private const float BarWidth = 3f;

    private float _rotation;

    public int GetBandCount(float width, float height) => BandCount;

    public void Render(CanvasDrawingSession session, float[] bands, float width, float height,
        VisualizerSettings settings)
    {
        var baseColor = EffectsHelper.GetRenderColor(settings);
        float cx = width / 2f;
        float cy = height / 2f;
        float radius = MathF.Min(width, height) / 2f;
        float innerR = radius * MinRadius;
        float maxLen = radius * MaxBarLength;

        _rotation += 0.003f; // slow continuous rotation

        if (settings.DarkBackground)
            EffectsHelper.DrawDarkBackground(session, width, height);

        using var glowLayer = settings.GlowEnabled ? new CanvasCommandList(session) : null;
        var drawSession = settings.GlowEnabled ? glowLayer!.CreateDrawingSession() : session;

        int count = bands.Length;
        float angleStep = MathF.Tau / count;

        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep + _rotation;
            float barLen = Math.Max(2f, bands[i] * maxLen);
            float alpha = 0.5f + bands[i] * 0.5f;

            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);

            float x1 = cx + cos * innerR;
            float y1 = cy + sin * innerR;
            float x2 = cx + cos * (innerR + barLen);
            float y2 = cy + sin * (innerR + barLen);

            var color = Color.FromArgb((byte)(255 * alpha), baseColor.R, baseColor.G, baseColor.B);
            drawSession.DrawLine(x1, y1, x2, y2, color, BarWidth);
        }

        if (settings.GlowEnabled && glowLayer != null)
        {
            drawSession.Dispose();
            EffectsHelper.DrawGlow(session, glowLayer, 16f);
            session.DrawImage(glowLayer);

            EffectsHelper.DrawReflection(session, glowLayer, width, height, cy);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Audiomatic/Visualizer/CircleMode.cs
git commit -m "feat: Implement CircleMode with radial bar layout"
```

---

### Task 5: Implement WaveMode

**Files:**
- Create: `Audiomatic/Visualizer/WaveMode.cs`

- [ ] **Step 1: Create WaveMode.cs**

```csharp
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace Audiomatic.Visualizer;

public sealed class WaveMode : IVisualizerMode
{
    private const int Cols = 64;  // frequency bands
    private const int Rows = 16;  // depth (time history)
    private const float Perspective = 0.6f; // depth shrink factor
    private const float TiltY = 0.35f; // vertical tilt for 3D look

    private readonly float[][] _history = new float[Rows][];
    private int _historyIndex;
    private int _frameCount;

    public int GetBandCount(float width, float height) => Cols;

    public void Render(CanvasDrawingSession session, float[] bands, float width, float height,
        VisualizerSettings settings)
    {
        var baseColor = EffectsHelper.GetRenderColor(settings);
        int bandCount = Math.Min(bands.Length, Cols);

        // Store current frame
        if (_history[0] == null || _history[0].Length != bandCount)
        {
            for (int i = 0; i < Rows; i++)
                _history[i] = new float[bandCount];
        }
        Array.Copy(bands, 0, _history[_historyIndex], 0, bandCount);
        _historyIndex = (_historyIndex + 1) % Rows;
        _frameCount++;

        if (settings.DarkBackground)
            EffectsHelper.DrawDarkBackground(session, width, height);

        using var glowLayer = settings.GlowEnabled ? new CanvasCommandList(session) : null;
        var drawSession = settings.GlowEnabled ? glowLayer!.CreateDrawingSession() : session;

        float baseY = height * 0.7f;
        float maxH = height * 0.4f;
        float totalWidth = width * 0.8f;

        // Draw rows from back to front
        for (int row = Rows - 1; row >= 0; row--)
        {
            int histIdx = ((_historyIndex - 1 - row) % Rows + Rows) % Rows;
            var rowBands = _history[histIdx];
            if (_frameCount <= row) continue;

            float depthT = row / (float)(Rows - 1); // 0=front, 1=back
            float scale = 1f - depthT * Perspective;
            float rowY = baseY - row * (height * 0.025f);
            float alpha = 1f - depthT * 0.7f;
            var color = Color.FromArgb((byte)(255 * alpha), baseColor.R, baseColor.G, baseColor.B);

            float rowWidth = totalWidth * scale;
            float offsetX = (width - rowWidth) / 2f;
            float step = rowWidth / bandCount;

            // Draw connected line segments for this row
            Vector2? prev = null;
            for (int i = 0; i < bandCount && i < rowBands.Length; i++)
            {
                float x = offsetX + i * step;
                float barH = rowBands[i] * maxH * scale;
                float y = rowY - barH;

                var point = new Vector2(x, y);
                if (prev.HasValue)
                    drawSession.DrawLine(prev.Value, point, color, 1.5f * scale);
                prev = point;
            }
        }

        if (settings.GlowEnabled && glowLayer != null)
        {
            drawSession.Dispose();
            EffectsHelper.DrawGlow(session, glowLayer, 10f);
            session.DrawImage(glowLayer);

            EffectsHelper.DrawReflection(session, glowLayer, width, height, baseY);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Audiomatic/Visualizer/WaveMode.cs
git commit -m "feat: Implement WaveMode with wireframe surface grid"
```

---

## Chunk 3: Renderer and UI Integration (Tasks 6-8)

### Task 6: Create VisualizerRenderer

**Files:**
- Create: `Audiomatic/Visualizer/VisualizerRenderer.cs`

- [ ] **Step 1: Create VisualizerRenderer.cs**

The renderer manages the `CanvasAnimatedControl`, mode switching, and the selector bar.

```csharp
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Audiomatic.Services;

namespace Audiomatic.Visualizer;

public sealed class VisualizerRenderer
{
    private readonly SpectrumAnalyzer _spectrum;
    private readonly Func<TimeSpan> _getPosition;
    private readonly Func<bool> _hasTrack;

    private CanvasAnimatedControl? _canvas;
    private IVisualizerMode _currentMode;
    private string _currentModeName;
    private VisualizerSettings _settings;

    // Selector buttons for highlighting
    private readonly Dictionary<string, Button> _modeButtons = new();

    private static readonly Dictionary<string, Func<IVisualizerMode>> ModeFactories = new()
    {
        ["bars"] = () => new BarsMode(),
        ["circle"] = () => new CircleMode(),
        ["wave"] = () => new WaveMode(),
    };

    public VisualizerRenderer(SpectrumAnalyzer spectrum, Func<TimeSpan> getPosition, Func<bool> hasTrack)
    {
        _spectrum = spectrum;
        _getPosition = getPosition;
        _hasTrack = hasTrack;

        var s = SettingsManager.Load();
        _currentModeName = s.VisualizerMode ?? "classic";
        _settings = new VisualizerSettings(
            Color: s.VisualizerColor ?? "",
            GlowEnabled: s.VisualizerGlow,
            DarkBackground: s.VisualizerDarkBg);

        _currentMode = ModeFactories.TryGetValue(_currentModeName, out var factory)
            ? factory()
            : new BarsMode(); // won't be used in classic mode
    }

    /// <summary>Whether the current mode is "classic" (handled by MainWindow's existing code).</summary>
    public bool IsClassicMode => _currentModeName == "classic";

    /// <summary>Build the selector bar + CanvasAnimatedControl. Returns the root element to add to the container.</summary>
    public StackPanel BuildUI()
    {
        var root = new StackPanel { Spacing = 0 };

        // Mode selector row
        var selector = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 0,
            Margin = new Thickness(0, 4, 0, 4)
        };

        void AddModeButton(string mode, string label)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = label, FontSize = 11 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(4),
                MinHeight = 0, MinWidth = 0
            };
            btn.Click += (_, _) => SwitchMode(mode);
            _modeButtons[mode] = btn;
            selector.Children.Add(btn);
        }

        AddModeButton("classic", "Classic");
        AddModeButton("bars", "Bars");
        AddModeButton("circle", "Circle");
        AddModeButton("wave", "Wave");

        // Spacer
        selector.Children.Add(new Border { Width = 12 });

        // Glow toggle
        var glowBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE706", FontSize = 12 }, // brightness
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0, MinWidth = 0,
            Tag = "glow"
        };
        glowBtn.Click += (_, _) =>
        {
            _settings = _settings with { GlowEnabled = !_settings.GlowEnabled };
            SaveVisualizerSettings();
            UpdateToggleButtons(glowBtn, null);
        };
        selector.Children.Add(glowBtn);

        // Dark bg toggle
        var darkBgBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE708", FontSize = 12 }, // contrast
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(4),
            MinHeight = 0, MinWidth = 0,
            Tag = "darkbg"
        };
        darkBgBtn.Click += (_, _) =>
        {
            _settings = _settings with { DarkBackground = !_settings.DarkBackground };
            SaveVisualizerSettings();
            UpdateToggleButtons(glowBtn, darkBgBtn);
        };
        selector.Children.Add(darkBgBtn);

        // Color box
        var colorPreview = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeHelper.Brush("ControlStrokeColorDefaultBrush"),
            Background = new SolidColorBrush(EffectsHelper.GetRenderColor(_settings)),
            Margin = new Thickness(4, 0, 0, 0)
        };
        var colorBox = new TextBox
        {
            Text = _settings.Color, FontSize = 11, MaxLength = 7,
            PlaceholderText = "Accent", Width = 72, MinHeight = 0,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(4, 0, 0, 0)
        };
        colorBox.TextChanged += (_, _) =>
        {
            _settings = _settings with { Color = colorBox.Text };
            try { colorPreview.Background = new SolidColorBrush(EffectsHelper.GetRenderColor(_settings)); } catch { }
            if (string.IsNullOrEmpty(colorBox.Text) || (colorBox.Text.StartsWith('#') && colorBox.Text.Length == 7))
                SaveVisualizerSettings();
        };
        selector.Children.Add(colorPreview);
        selector.Children.Add(colorBox);

        root.Children.Add(selector);
        UpdateModeHighlight();
        UpdateToggleButtons(glowBtn, darkBgBtn);

        // Win2D canvas — fills remaining space
        _canvas = new CanvasAnimatedControl
        {
            ClearColor = Windows.UI.Color.FromArgb(0, 0, 0, 0), // transparent
            IsFixedTimeStep = true,
            TargetElapsedTime = TimeSpan.FromMilliseconds(16), // 60fps
        };
        _canvas.Draw += Canvas_Draw;

        root.Children.Add(_canvas);

        return root;
    }

    public void Start()
    {
        if (_canvas != null && !IsClassicMode)
            _canvas.Paused = false;
    }

    public void Stop()
    {
        if (_canvas != null)
            _canvas.Paused = true;
    }

    public void SetCanvasVisibility(bool win2dVisible)
    {
        if (_canvas != null)
            _canvas.Visibility = win2dVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (IsClassicMode) return;

        float w = (float)sender.Size.Width;
        float h = (float)sender.Size.Height;
        if (w <= 0 || h <= 0) return;

        int bandCount = _currentMode.GetBandCount(w, h);
        float[] bands;

        if (_hasTrack())
            bands = _spectrum.GetSpectrum(_getPosition(), bandCount);
        else
            bands = _spectrum.GetSpectrum(TimeSpan.Zero, bandCount); // will decay

        _currentMode.Render(args.DrawingSession, bands, w, h, _settings);
    }

    private void SwitchMode(string mode)
    {
        _currentModeName = mode;

        if (ModeFactories.TryGetValue(mode, out var factory))
            _currentMode = factory();

        SettingsManager.Save(SettingsManager.Load() with { VisualizerMode = mode });
        UpdateModeHighlight();

        // Toggle canvas vs classic
        SetCanvasVisibility(!IsClassicMode);
        if (IsClassicMode)
            Stop();
        else
            Start();
    }

    private void UpdateModeHighlight()
    {
        var accent = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush");
        var normal = ThemeHelper.Brush("TextFillColorSecondaryBrush");

        foreach (var (mode, btn) in _modeButtons)
        {
            if (btn.Content is TextBlock tb)
                tb.Foreground = mode == _currentModeName ? accent : normal;
        }
    }

    private void UpdateToggleButtons(Button glowBtn, Button? darkBgBtn)
    {
        var accent = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush");
        var normal = ThemeHelper.Brush("TextFillColorSecondaryBrush");
        if (glowBtn.Content is FontIcon gi)
            gi.Foreground = _settings.GlowEnabled ? accent : normal;
        if (darkBgBtn?.Content is FontIcon di)
            di.Foreground = _settings.DarkBackground ? accent : normal;
    }

    private void SaveVisualizerSettings()
    {
        var s = SettingsManager.Load();
        SettingsManager.Save(s with
        {
            VisualizerColor = _settings.Color,
            VisualizerGlow = _settings.GlowEnabled,
            VisualizerDarkBg = _settings.DarkBackground
        });
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Audiomatic/Visualizer/VisualizerRenderer.cs
git commit -m "feat: Add VisualizerRenderer with mode switching and selector UI"
```

---

### Task 7: Update MainWindow XAML

**Files:**
- Modify: `Audiomatic/MainWindow.xaml:339-346`

- [ ] **Step 1: Replace WaveformContainer content**

Replace the current WaveformContainer (lines 339-346):

```xml
<!-- Row 7: Waveform visualizer (shown when Visualizer view is active) -->
<Grid x:Name="WaveformContainer" Grid.Row="7"
      Visibility="Collapsed"
      Background="Transparent"
      SizeChanged="WaveformContainer_SizeChanged"
      PointerPressed="WaveformCanvas_PointerPressed">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="*"/>
    </Grid.RowDefinitions>
    <!-- Selector + Win2D canvas injected by VisualizerRenderer -->
    <StackPanel x:Name="VisualizerHost" Grid.RowSpan="2"/>
    <!-- Classic mode canvas (existing) -->
    <Canvas x:Name="WaveformCanvas" Grid.Row="1"/>
</Grid>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 3: Commit**

```bash
git add Audiomatic/MainWindow.xaml
git commit -m "feat: Add VisualizerHost and row definitions to WaveformContainer"
```

---

### Task 8: Integrate VisualizerRenderer into MainWindow.xaml.cs

**Files:**
- Modify: `Audiomatic/MainWindow.xaml.cs`

- [ ] **Step 1: Add field and using**

Add at the top of the file (imports section):
```csharp
using Audiomatic.Visualizer;
```

Add field near the other visualizer fields (~line 39):
```csharp
private VisualizerRenderer? _vizRenderer;
```

- [ ] **Step 2: Initialize renderer**

In the constructor or in `UpdateSpectrumTimer()`, initialize the renderer the first time the Visualizer view is shown. Modify `UpdateSpectrumTimer()` (currently lines 1726-1748):

```csharp
private void UpdateSpectrumTimer()
{
    bool needsViz = _viewMode == ViewMode.Visualizer;

    if (needsViz)
    {
        PrepareSpectrumForCurrentTrack();

        // Initialize renderer once
        if (_vizRenderer == null)
        {
            _vizRenderer = new VisualizerRenderer(
                _spectrum,
                () => _player.Position,
                () => _player.CurrentTrack != null);
            var ui = _vizRenderer.BuildUI();
            VisualizerHost.Children.Clear();
            VisualizerHost.Children.Add(ui);
        }

        if (_vizRenderer.IsClassicMode)
        {
            // Classic mode: use DispatcherTimer + WaveformCanvas
            WaveformCanvas.Visibility = Visibility.Visible;
            _vizRenderer.SetCanvasVisibility(false);
            _vizRenderer.Stop();

            if (_spectrumTimer == null)
            {
                int ms = _vizFps >= 60 ? 16 : 33;
                _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                _spectrumTimer.Tick += (_, _) =>
                {
                    if (_viewMode == ViewMode.Visualizer && (_vizRenderer?.IsClassicMode ?? true))
                        DrawVisualization();
                };
                _spectrumTimer.Start();
            }
        }
        else
        {
            // Win2D mode: hide classic canvas, start renderer
            WaveformCanvas.Visibility = Visibility.Collapsed;
            _vizRenderer.SetCanvasVisibility(true);
            _vizRenderer.Start();

            // Stop classic timer if running
            if (_spectrumTimer != null)
            {
                _spectrumTimer.Stop();
                _spectrumTimer = null;
            }
        }
    }
    else
    {
        // Not in visualizer view — stop everything
        if (_spectrumTimer != null)
        {
            _spectrumTimer.Stop();
            _spectrumTimer = null;
            _vizBandCount = 0;
            _vizNoTrackText = null;
        }
        _vizRenderer?.Stop();
    }
}
```

- [ ] **Step 3: Update mode switch callback**

The `VisualizerRenderer.SwitchMode` already saves settings. We need to hook the mode switch to restart the timer logic. Add to the `VisualizerRenderer` a callback or make `SwitchMode` call back to MainWindow.

Simpler approach: add a public `Action? OnModeChanged` callback to `VisualizerRenderer`:

In `VisualizerRenderer.cs`, add field:
```csharp
public Action? OnModeChanged { get; set; }
```

In `SwitchMode()`, at the end add:
```csharp
OnModeChanged?.Invoke();
```

In `MainWindow.xaml.cs`, after creating the renderer:
```csharp
_vizRenderer.OnModeChanged = () => UpdateSpectrumTimer();
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add Audiomatic/MainWindow.xaml.cs Audiomatic/Visualizer/VisualizerRenderer.cs
git commit -m "feat: Integrate VisualizerRenderer into MainWindow with mode switching"
```

---

## Chunk 4: Final Integration and Polish (Task 9)

### Task 9: End-to-end test and polish

- [ ] **Step 1: Run the application**

Run: `dotnet run`

Manual test checklist:
- Navigate to Visualizer view
- Verify Classic mode works as before (rectangle bars)
- Switch to Bars mode — verify perspective depth bars render
- Switch to Circle mode — verify radial bars render with rotation
- Switch to Wave mode — verify wireframe surface renders
- Toggle glow on/off — verify blur effect appears/disappears
- Toggle dark background — verify overlay
- Change color hex — verify bars change color
- Clear color field — verify fallback to accent blue
- Switch back to Classic — verify original rendering resumes
- Close and reopen app — verify mode/settings persisted

- [ ] **Step 2: Fix any rendering issues found during testing**

Common issues to watch for:
- Canvas not sizing correctly (may need `VerticalAlignment="Stretch"` on CanvasAnimatedControl)
- Transparency not working (check `ClearColor` is transparent)
- Classic mode bars not showing (check WaveformCanvas visibility)

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: Win2D 3D audio visualizer with 4 modes"
```
