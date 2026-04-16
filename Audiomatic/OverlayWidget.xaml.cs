using System.Runtime.InteropServices;
using Audiomatic.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT;
using WinRT.Interop;

namespace Audiomatic;

public sealed partial class OverlayWidget : Window
{
    private readonly AudioPlayerService _player;
    private readonly QueueManager _queue;
    private readonly Action _onPrev;
    private readonly Action _onNext;
    private readonly Action _onPlayPause;

    // Window drag
    private bool _isDragging;
    private POINT _dragStartCursor;
    private Windows.Graphics.PointInt32 _dragStartPos;

    // Backdrop controller (keeps effect visible when unfocused)
    private IDisposable? _backdropController;
    private SystemBackdropConfiguration? _configSource;

    // Borderless window subclassing
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProcDelegate;

    private const uint WM_NCCALCSIZE = 0x0083;
    private const int GWLP_WNDPROC = -4;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private IntPtr OverlayWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Remove all non-client area (border, title bar) by claiming entire rect is client area
        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
            return IntPtr.Zero;
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    public OverlayWidget(AudioPlayerService player, QueueManager queue,
        Action onPrev, Action onNext, Action onPlayPause)
    {
        _player = player;
        _queue = queue;
        _onPrev = onPrev;
        _onNext = onNext;
        _onPlayPause = onPlayPause;

        InitializeComponent();

        // Window size and style — compact overlay
        AppWindow.Resize(new Windows.Graphics.SizeInt32(320, 78));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Subclass window to intercept WM_NCCALCSIZE — removes the white border frame
        _hwnd = WindowNative.GetWindowHandle(this);
        _wndProcDelegate = new WndProcDelegate(OverlayWndProc);
        _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);

        ApplyOverlayBackdrop();
        ApplyTheme(SettingsManager.LoadTheme());
        RestorePosition();

        // Subscribe to player events
        _player.PositionChanged += OnPositionChanged;
        _player.PlaybackStarted += OnPlaybackStarted;
        _player.PlaybackPaused += OnPlaybackPaused;
        _player.PlaybackStopped += OnPlaybackStopped;
        _player.MediaOpened += OnMediaOpened;

        // Initial state sync
        SyncTrackInfo();
        SyncPlayPauseIcon();

        this.Closed += OnClosed;
    }

    private void ApplyOverlayBackdrop()
    {
        _backdropController?.Dispose();
        _backdropController = null;
        _configSource = null;
        SystemBackdrop = null;

        // Use controller API with IsInputActive = true so the backdrop stays
        // visible even when the overlay loses focus
        IDisposable? controller = DesktopAcrylicController.IsSupported()
            ? new DesktopAcrylicController()
            : null;

        if (controller == null) return;

        _backdropController = controller;
        _configSource = new SystemBackdropConfiguration { IsInputActive = true };
        if (Content is FrameworkElement fe)
            _configSource.Theme = (SystemBackdropTheme)fe.ActualTheme;

        var target = this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();
        if (controller is DesktopAcrylicController acrylic)
        {
            acrylic.AddSystemBackdropTarget(target);
            acrylic.SetSystemBackdropConfiguration(_configSource);
        }
    }

    private void ApplyTheme(string theme)
    {
        if (Content is not FrameworkElement root) return;
        root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private void RestorePosition()
    {
        var settings = SettingsManager.Load();
        if (settings.OverlayX.HasValue && settings.OverlayY.HasValue)
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            var workArea = displayArea.WorkArea;
            var x = settings.OverlayX.Value;
            var y = settings.OverlayY.Value;

            if (x >= workArea.X && x < workArea.X + workArea.Width - 100 &&
                y >= workArea.Y && y < workArea.Y + workArea.Height - 40)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
                return;
            }
        }

        // Default: top-right corner
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var wa = display.WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(wa.X + wa.Width - 320 - 16, wa.Y + 16));
    }

    private void SavePosition()
    {
        var pos = AppWindow.Position;
        var s = SettingsManager.Load();
        SettingsManager.Save(s with { OverlayX = pos.X, OverlayY = pos.Y });
    }

    // -- Track info sync --

    public void SyncTrackInfo()
    {
        var track = _player.CurrentTrack;
        if (track == null)
        {
            OverlayTitle.Text = Strings.T("No track");
            OverlayArtist.Text = "";
            OverlayAlbumArt.Source = null;
            OverlayArtPlaceholder.Visibility = Visibility.Visible;
            OverlayProgress.Value = 0;
            return;
        }

        OverlayTitle.Text = track.Title;
        OverlayArtist.Text = track.Artist;
        LoadAlbumArt(track.Path);
    }

    public void SyncAlbumArt(Microsoft.UI.Xaml.Media.ImageSource? source)
    {
        OverlayAlbumArt.Source = source;
        var hasArt = source != null;
        OverlayArtPlaceholder.Visibility = hasArt ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SyncPlayPauseIcon()
    {
        OverlayPlayPauseIcon.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
    }

    private async void LoadAlbumArt(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures.Length > 0)
            {
                var data = tagFile.Tag.Pictures[0].Data.Data;
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(data);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new BitmapImage();
                bitmap.SetSource(stream);
                OverlayAlbumArt.Source = bitmap;
                OverlayArtPlaceholder.Visibility = Visibility.Collapsed;
                return;
            }
        }
        catch { }

        try
        {
            var folder = Path.GetDirectoryName(filePath);
            if (folder != null)
            {
                var coverPath = FindCoverFile(folder);
                if (coverPath != null)
                {
                    OverlayAlbumArt.Source = new BitmapImage { UriSource = new Uri(coverPath) };
                    OverlayArtPlaceholder.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }
        catch { }

        OverlayAlbumArt.Source = null;
        OverlayArtPlaceholder.Visibility = Visibility.Visible;
    }

    private static readonly string[] CoverFileNames = { "cover", "folder", "album", "front", "artwork" };
    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private static string? FindCoverFile(string folder)
    {
        foreach (var name in CoverFileNames)
            foreach (var ext in CoverExtensions)
            {
                var path = Path.Combine(folder, name + ext);
                if (File.Exists(path)) return path;
            }
        return null;
    }

    // -- Player event handlers --

    private void OnPositionChanged(TimeSpan position)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var duration = _player.Duration;
            if (duration.TotalSeconds > 0)
                OverlayProgress.Value = position.TotalSeconds / duration.TotalSeconds * 1000;
            else
                OverlayProgress.Value = 0;
        });
    }

    private void OnPlaybackStarted()
    {
        DispatcherQueue.TryEnqueue(() => OverlayPlayPauseIcon.Glyph = "\uE769");
    }

    private void OnPlaybackPaused()
    {
        DispatcherQueue.TryEnqueue(() => OverlayPlayPauseIcon.Glyph = "\uE768");
    }

    private void OnPlaybackStopped()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            OverlayPlayPauseIcon.Glyph = "\uE768";
            OverlayProgress.Value = 0;
        });
    }

    private void OnMediaOpened()
    {
        DispatcherQueue.TryEnqueue(SyncTrackInfo);
    }

    // -- Controls --

    private void PlayPause_Click(object sender, RoutedEventArgs e) => _onPlayPause();
    private void Prev_Click(object sender, RoutedEventArgs e) => _onPrev();
    private void Next_Click(object sender, RoutedEventArgs e) => _onNext();

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        SavePosition();
        Close();
    }

    // -- Window drag --

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.OriginalSource is Microsoft.UI.Xaml.Controls.Button) return;
        if (e.OriginalSource is Microsoft.UI.Xaml.Controls.FontIcon) return;

        _isDragging = true;
        GetCursorPos(out _dragStartCursor);
        _dragStartPos = AppWindow.Position;
        ((UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        GetCursorPos(out var current);
        var dx = current.X - _dragStartCursor.X;
        var dy = current.Y - _dragStartCursor.Y;
        AppWindow.Move(new Windows.Graphics.PointInt32(_dragStartPos.X + dx, _dragStartPos.Y + dy));
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        SavePosition();
    }

    // -- Cleanup --

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _player.PositionChanged -= OnPositionChanged;
        _player.PlaybackStarted -= OnPlaybackStarted;
        _player.PlaybackPaused -= OnPlaybackPaused;
        _player.PlaybackStopped -= OnPlaybackStopped;
        _player.MediaOpened -= OnMediaOpened;

        _backdropController?.Dispose();
        _backdropController = null;

        // Restore original WndProc
        if (_oldWndProc != IntPtr.Zero)
            SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _oldWndProc);
    }
}
