using System.Runtime.InteropServices;
using Audiomatic.Models;
using Audiomatic.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Control;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Audiomatic;

public sealed partial class MainWindow : Window
{
    private readonly AudioPlayerService _player = new();
    private readonly QueueManager _queue = new();
    private List<TrackInfo> _allTracks = [];
    private List<TrackInfo> _displayedTracks = [];
    private bool _isSeeking;
    private bool _isPinnedOnTop;
    private string _sortBy = "title";
    private bool _sortAscending = true;

    // Playlist navigation
    private enum ViewMode { Library, PlaylistList, PlaylistDetail, Queue, Visualizer, MediaControl }
    private ViewMode _viewMode = ViewMode.Library;
    private PlaylistInfo? _currentPlaylist;

    // Visualizer
    private readonly SpectrumAnalyzer _spectrum = new();
    private DispatcherTimer? _spectrumTimer;
    private int _vizFps = 30;
    private int _vizBandCount;  // tracks current bar count for reuse
    private TextBlock? _vizNoTrackText;

    // View transition animation
    private bool _isViewTransitioning;

    // Media control
    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private readonly Dictionary<string, MediaSessionPanel> _mediaSessionPanels = new();
    private DispatcherTimer? _mediaTickTimer;

    // Collapse animation
    private enum CollapseState { Expanded, Compact, Mini }
    private CollapseState _collapseState = CollapseState.Expanded;
    private readonly int _expandedHeight = 710;
    private readonly int _collapsedHeight = 220;
    private readonly int _miniHeight = 60;
    private DispatcherTimer? _animTimer;
    private int _targetHeight;
    private int _currentAnimHeight;
    private int _animStartY;
    private int _targetY;

    // Window drag
    private bool _isDragging;
    private POINT _dragStartCursor;
    private Windows.Graphics.PointInt32 _dragStartPos;

    // System integration
    private IntPtr _hwnd;
    private IntPtr _oldWndProc;
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProcDelegate;
    private bool _isVisible = true;
    private NOTIFYICONDATA _trayIcon;
    private bool _isQuitting;

    // Constants
    private const int HOTKEY_ID = 1;
    private const int HOTKEY_COLLAPSE_ID = 2;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint VK_M = 0x4D;
    private const uint VK_L = 0x4C;
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAYICON = WM_APP + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const int GWLP_WNDPROC = -4;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIM_ADD = 0x00;
    private const uint NIM_DELETE = 0x02;
    private const uint WM_COMMAND = 0x0111;
    private const uint MF_SEPARATOR = 0x800;
    private const int IDM_SHOW = 1000;
    private const int IDM_QUIT = 1001;

    // P/Invoke
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string? lpNewItem);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr hMenu);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadImage(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    public MainWindow()
    {
        this.InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(CustomTitleBar);

        _hwnd = WindowNative.GetWindowHandle(this);
        WindowShadow.Apply(this);

        // Window size and style
        AppWindow.Resize(new Windows.Graphics.SizeInt32(380, _expandedHeight));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.SetBorderAndTitleBar(true, false);
        }

        // Restore window position (default: bottom-right)
        RestoreWindowPosition();

        // Apply backdrop and theme
        ApplyBackdrop(SettingsManager.LoadBackdrop());
        ApplyTheme(SettingsManager.LoadTheme());

        // Set up audio player
        _player.SetDispatcherQueue(DispatcherQueue);
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += OnMediaEnded;
        _player.MediaFailed += OnMediaFailed;
        _player.PositionChanged += OnPositionChanged;

        // Load settings
        var settings = SettingsManager.Load();
        VolumeSlider.Value = settings.Volume * 100;
        _player.Volume = settings.Volume;
        _sortBy = settings.SortBy;
        _sortAscending = settings.SortAscending;
        SortAscending.IsChecked = _sortAscending;
        UpdateSortChecks();

        // Initialize library and load tracks
        LibraryManager.Initialize();
        LoadTracks();
        UpdateNavigation();

        // Restore queue state (display info only, not playing yet)
        _queue.LoadState(_allTracks);
        if (_queue.CurrentTrack != null)
        {
            var t = _queue.CurrentTrack;
            TrackTitle.Text = t.Title;
            TrackArtist.Text = t.Artist;
            TrackAlbum.Text = t.Album;
            LoadAlbumArt(t.Path);
            UpdateMiniPlayer(t);
        }

        // Restore shuffle/repeat
        _queue.Shuffle = settings.ShuffleEnabled;
        if (settings.ShuffleEnabled)
            ShuffleIcon.Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush");

        if (Enum.TryParse<RepeatMode>(settings.RepeatMode, true, out var rm))
        {
            _queue.Repeat = rm;
            UpdateRepeatIcon();
        }

        _vizFps = settings.VisualizerFps;

        // Subclass window for hotkey/tray messages
        _wndProcDelegate = new WndProcDelegate(WndProc);
        _oldWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        // Register global hotkeys
        RegisterHotKey(_hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_M);
        RegisterHotKey(_hwnd, HOTKEY_COLLAPSE_ID, MOD_CONTROL, VK_L);

        // Add tray icon
        AddTrayIcon();

        // Window close: save state
        this.Closed += (_, _) =>
        {
            _isQuitting = true;
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            UnregisterHotKey(_hwnd, HOTKEY_COLLAPSE_ID);
            RemoveTrayIcon();
            _queue.SaveState();
            var s = SettingsManager.Load();
            var pos = AppWindow.Position;
            SettingsManager.Save(s with
            {
                Volume = _player.Volume,
                ShuffleEnabled = _queue.Shuffle,
                RepeatMode = _queue.Repeat.ToString(),
                SortBy = _sortBy,
                SortAscending = _sortAscending,
                VisualizerFps = _vizFps,
                WindowX = pos.X,
                WindowY = pos.Y
            });
            _mediaTickTimer?.Stop();
            foreach (var p in _mediaSessionPanels.Values) p.Detach();
            _mediaSessionPanels.Clear();
            _spectrumTimer?.Stop();
            _spectrum.Dispose();
            _player.Dispose();
        };
    }

    // -- Library --------------------------------------------------

    private void LoadTracks()
    {
        _allTracks = LibraryManager.GetAllTracks();
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        if (_viewMode == ViewMode.PlaylistList)
        {
            LoadPlaylistList();
            return;
        }

        if (_viewMode == ViewMode.Queue)
        {
            BuildQueueView();
            return;
        }

        if (_viewMode == ViewMode.Visualizer)
            return;

        List<TrackInfo> source = _viewMode == ViewMode.PlaylistDetail && _currentPlaylist != null
            ? LibraryManager.GetPlaylistTracks(_currentPlaylist.Id)
            : _allTracks;

        var query = SearchBox.Text?.Trim() ?? "";
        _displayedTracks = string.IsNullOrEmpty(query)
            ? [.. source]
            : source.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // Sort (only in library mode; playlists keep their order)
        if (_viewMode == ViewMode.Library)
        {
            _displayedTracks = _sortBy switch
            {
                "artist" => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber)]
                    : [.. _displayedTracks.OrderByDescending(t => t.Artist).ThenByDescending(t => t.Album).ThenByDescending(t => t.TrackNumber)],
                "album" => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber)]
                    : [.. _displayedTracks.OrderByDescending(t => t.Album).ThenByDescending(t => t.TrackNumber)],
                "duration" => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.DurationMs)]
                    : [.. _displayedTracks.OrderByDescending(t => t.DurationMs)],
                _ => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.Title)]
                    : [.. _displayedTracks.OrderByDescending(t => t.Title)]
            };
        }

        RebuildTrackList();
        TrackCountText.Text = _viewMode == ViewMode.Library
            ? $"{_allTracks.Count:N0} tracks"
            : $"{_displayedTracks.Count:N0} tracks";
    }

    private void RebuildTrackList()
    {
        // Save scroll position
        var scrollViewer = FindScrollViewer(TrackListView);
        var scrollOffset = scrollViewer?.VerticalOffset ?? 0;

        var isPlaylistDetail = _viewMode == ViewMode.PlaylistDetail;

        TrackListView.CanReorderItems = false;
        TrackListView.Items.Clear();
        for (int i = 0; i < _displayedTracks.Count; i++)
        {
            var track = _displayedTracks[i];
            var isPlaying = _queue.CurrentTrack?.Id == track.Id;

            var grid = new Grid { Padding = new Thickness(2, 4, 2, 4), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (isPlaylistDetail)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Speaker icon or music note
            var icon = new FontIcon
            {
                Glyph = isPlaying ? "\uE767" : "\uE8D6",
                FontSize = 12,
                Foreground = isPlaying
                    ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                    : ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 16
            };
            Grid.SetColumn(icon, 0);

            // Track info
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = track.Title,
                FontSize = 13,
                FontWeight = isPlaying
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isPlaying
                    ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                    : ThemeHelper.Brush("TextFillColorPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });

            var subtitle = new List<string>();
            if (!string.IsNullOrEmpty(track.Artist)) subtitle.Add(track.Artist);
            if (!string.IsNullOrEmpty(track.Album)) subtitle.Add(track.Album);

            if (subtitle.Count > 0)
            {
                info.Children.Add(new TextBlock
                {
                    Text = string.Join(" \u00B7 ", subtitle),
                    FontSize = 11,
                    Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                });
            }
            Grid.SetColumn(info, 1);

            // Duration
            var dur = new TextBlock
            {
                Text = track.DurationFormatted,
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dur, 2);

            grid.Children.Add(icon);
            grid.Children.Add(info);
            grid.Children.Add(dur);

            // Reorder Up/Down buttons for playlist detail
            if (isPlaylistDetail && _currentPlaylist != null)
            {
                var reorderPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 0 };
                if (i > 0)
                {
                    var fromIdx = i;
                    var playlistId = _currentPlaylist.Id;
                    var upBtn = new Button
                    {
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 1, 4, 1),
                        MinHeight = 0, MinWidth = 0,
                        Content = new FontIcon
                        {
                            Glyph = "\uE74A", FontSize = 9,
                            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
                        }
                    };
                    upBtn.Click += (_, _) =>
                    {
                        LibraryManager.MoveTrackInPlaylist(playlistId, fromIdx, fromIdx - 1);
                        ApplyFilterAndSort();
                    };
                    reorderPanel.Children.Add(upBtn);
                }
                if (i < _displayedTracks.Count - 1)
                {
                    var fromIdx = i;
                    var playlistId = _currentPlaylist.Id;
                    var downBtn = new Button
                    {
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(4, 1, 4, 1),
                        MinHeight = 0, MinWidth = 0,
                        Content = new FontIcon
                        {
                            Glyph = "\uE74B", FontSize = 9,
                            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
                        }
                    };
                    downBtn.Click += (_, _) =>
                    {
                        LibraryManager.MoveTrackInPlaylist(playlistId, fromIdx, fromIdx + 1);
                        ApplyFilterAndSort();
                    };
                    reorderPanel.Children.Add(downBtn);
                }
                Grid.SetColumn(reorderPanel, 3);
                grid.Children.Add(reorderPanel);
            }

            grid.Tag = track;

            // Context menu (Raycast style)
            var ctxFlyout = new Flyout();
            ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
            var capturedTrack = track;
            ctxFlyout.Opening += (_, _) =>
            {
                ctxFlyout.Content = BuildTrackContextContent(ctxFlyout, capturedTrack);
            };
            grid.ContextFlyout = ctxFlyout;

            TrackListView.Items.Add(grid);
        }

        // Restore scroll position
        if (scrollViewer != null && scrollOffset > 0)
        {
            TrackListView.UpdateLayout();
            scrollViewer.ChangeView(null, scrollOffset, null, disableAnimation: true);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is ScrollViewer sv) return sv;
            var result = FindScrollViewer(child);
            if (result != null) return result;
        }
        return null;
    }

    // -- Playback events ------------------------------------------

    private void OnMediaOpened()
    {
        var dur = _player.Duration;
        if (dur.TotalSeconds > 0)
        {
            _isSeeking = true;
            TimelineSlider.Maximum = dur.TotalSeconds;
            TimelineSlider.Value = 0;
            DurationText.Text = FormatTime(dur);
            PositionText.Text = "0:00";
            _isSeeking = false;
        }
    }

    private async void OnMediaEnded()
    {
        var next = _queue.Next();
        if (next != null)
        {
            try
            {
                await _player.PlayTrackAsync(next);
            }
            catch (Exception ex)
            {
                TrackArtist.Text = $"Error: {ex.Message}";
            }
            UpdateNowPlaying(next);
        }
        else
        {
            PlayPauseIcon.Glyph = "\uE768";
            MiniPlayPauseIcon.Glyph = "\uE768";
        }
    }

    private void OnMediaFailed(string error)
    {
        PlayPauseIcon.Glyph = "\uE768";
        TrackArtist.Text = $"Error: {error}";
    }

    private void OnPositionChanged(TimeSpan pos)
    {
        if (_isSeeking) return;
        _isSeeking = true;
        TimelineSlider.Value = pos.TotalSeconds;
        PositionText.Text = FormatTime(pos);
        _isSeeking = false;

    }

    private void UpdateNowPlaying(TrackInfo track)
    {
        TrackTitle.Text = track.Title;
        TrackArtist.Text = track.Artist;
        TrackAlbum.Text = track.Album;
        PlayPauseIcon.Glyph = "\uE769"; // Pause icon
        MiniPlayPauseIcon.Glyph = "\uE769";
        LoadAlbumArt(track.Path);
        UpdateMiniPlayer(track);
        // Re-highlight current track in the appropriate view
        if (_viewMode == ViewMode.Visualizer)
            PrepareSpectrumForCurrentTrack();
        else if (_viewMode == ViewMode.Queue)
            BuildQueueView();
        else if (_viewMode != ViewMode.PlaylistList)
            RebuildTrackList();
    }

    private void UpdateMiniPlayer(TrackInfo? track)
    {
        if (track == null)
        {
            MiniTrackText.Text = "No track";
            MiniAlbumArt.Source = null;
            MiniAlbumArt.Visibility = Visibility.Collapsed;
            MiniAlbumArtPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        var display = string.IsNullOrEmpty(track.Artist)
            ? track.Title
            : $"{track.Title} \u2014 {track.Artist}";
        MiniTrackText.Text = display;

        // Share album art from main display
        MiniAlbumArt.Source = AlbumArtImage.Source;
        var hasArt = AlbumArtImage.Source != null;
        MiniAlbumArt.Visibility = hasArt ? Visibility.Visible : Visibility.Collapsed;
        MiniAlbumArtPlaceholder.Visibility = hasArt ? Visibility.Collapsed : Visibility.Visible;
    }

    private static readonly string[] CoverFileNames = { "cover", "folder", "album", "front", "artwork" };
    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

    private async void LoadAlbumArt(string filePath)
    {
        byte[]? embeddedArtData = null;
        string? coverFilePath = null;

        // 1. Try embedded tag artwork (single TagLib read — also used for SMTC)
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures.Length > 0)
            {
                embeddedArtData = tagFile.Tag.Pictures[0].Data.Data;

                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(embeddedArtData);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new BitmapImage();
                bitmap.SetSource(stream);
                AlbumArtImage.Source = bitmap;
                AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                AlbumArtImage.Visibility = Visibility.Visible;

                _player.UpdateSmtcArtwork(embeddedArtData, null);
                return;
            }
        }
        catch { }

        // 2. Try cover image file in the same folder
        try
        {
            var folder = System.IO.Path.GetDirectoryName(filePath);
            if (folder != null)
            {
                coverFilePath = FindCoverFile(folder);
                if (coverFilePath != null)
                {
                    var bitmap = new BitmapImage();
                    bitmap.UriSource = new Uri(coverFilePath);
                    AlbumArtImage.Source = bitmap;
                    AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                    AlbumArtImage.Visibility = Visibility.Visible;

                    _player.UpdateSmtcArtwork(null, coverFilePath);
                    return;
                }
            }
        }
        catch { }

        AlbumArtImage.Source = null;
        AlbumArtImage.Visibility = Visibility.Collapsed;
        AlbumArtPlaceholder.Visibility = Visibility.Visible;
        _player.UpdateSmtcArtwork(null, null);
    }

    private static string? FindCoverFile(string folder)
    {
        foreach (var name in CoverFileNames)
        {
            foreach (var ext in CoverExtensions)
            {
                var path = System.IO.Path.Combine(folder, name + ext);
                if (System.IO.File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    // -- Controls -------------------------------------------------

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_player.CurrentTrack == null)
        {
            // Start playing first track if nothing is loaded
            if (_displayedTracks.Count > 0)
            {
                _queue.SetQueue(_displayedTracks, 0);
                var track = _queue.CurrentTrack!;
                try
                {
                    await _player.PlayTrackAsync(track);
                }
                catch (Exception ex)
                {
                    TrackArtist.Text = $"Error: {ex.Message}";
                }
                UpdateNowPlaying(track);
            }
            return;
        }

        _player.TogglePlayPause();
        PlayPauseIcon.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        MiniPlayPauseIcon.Glyph = PlayPauseIcon.Glyph;
    }

    private async void Prev_Click(object sender, RoutedEventArgs e)
    {
        // If more than 3 seconds in, restart current track
        if (_player.Position.TotalSeconds > 3)
        {
            _player.Seek(TimeSpan.Zero);
            return;
        }

        var prev = _queue.Previous();
        if (prev != null)
        {
            try
            {
                await _player.PlayTrackAsync(prev);
            }
            catch (Exception ex)
            {
                TrackArtist.Text = $"Error: {ex.Message}";
            }
            UpdateNowPlaying(prev);
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        var next = _queue.Next();
        if (next != null)
        {
            try
            {
                await _player.PlayTrackAsync(next);
            }
            catch (Exception ex)
            {
                TrackArtist.Text = $"Error: {ex.Message}";
            }
            UpdateNowPlaying(next);
        }
    }

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        _queue.Shuffle = !_queue.Shuffle;
        ShuffleIcon.Foreground = _queue.Shuffle
            ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
            : ThemeHelper.Brush("TextFillColorPrimaryBrush");
    }

    private void Repeat_Click(object sender, RoutedEventArgs e)
    {
        _queue.Repeat = _queue.Repeat switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None
        };
        UpdateRepeatIcon();
    }

    private void UpdateRepeatIcon()
    {
        RepeatIcon.Glyph = _queue.Repeat == RepeatMode.One ? "\uE8ED" : "\uE8EE";
        RepeatIcon.Foreground = _queue.Repeat != RepeatMode.None
            ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
            : ThemeHelper.Brush("TextFillColorPrimaryBrush");
    }

    // -- Timeline -------------------------------------------------

    private void TimelineSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isSeeking || _player.CurrentTrack == null) return;
        _player.Seek(TimeSpan.FromSeconds(e.NewValue));
        PositionText.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
    }

    // -- Volume ---------------------------------------------------

    private void Volume_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _player.Volume = e.NewValue / 100.0;
        UpdateVolumeIcon(e.NewValue);
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        _player.IsMuted = !_player.IsMuted;
        VolumeIcon.Glyph = _player.IsMuted ? "\uE992" : GetVolumeGlyph(VolumeSlider.Value);
    }

    private void UpdateVolumeIcon(double value)
    {
        if (!_player.IsMuted)
            VolumeIcon.Glyph = GetVolumeGlyph(value);
    }

    private static string GetVolumeGlyph(double value) =>
        value == 0 ? "\uE992" : value < 50 ? "\uE993" : "\uE995";

    // -- Search & Sort --------------------------------------------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilterAndSort();
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem item && item.Tag is string sortBy)
        {
            _sortBy = sortBy;
            UpdateSortChecks();
            ApplyFilterAndSort();
        }
    }

    private void UpdateSortChecks()
    {
        SortTitle.IsChecked = _sortBy == "title";
        SortArtist.IsChecked = _sortBy == "artist";
        SortAlbum.IsChecked = _sortBy == "album";
        SortDuration.IsChecked = _sortBy == "duration";
    }

    private void SortDirection_Click(object sender, RoutedEventArgs e)
    {
        _sortAscending = SortAscending.IsChecked;
        ApplyFilterAndSort();
    }

    // -- Track list -----------------------------------------------

    private async void TrackList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Grid grid) return;

        // Playlist item clicked — open detail view
        if (grid.Tag is PlaylistInfo playlist)
        {
            _currentPlaylist = playlist;
            _viewMode = ViewMode.PlaylistDetail;
            UpdateNavigation();
            ApplyFilterAndSort();
            return;
        }

        if (grid.Tag is TrackInfo track)
        {
            // Queue item — play at index without resetting queue
            if (_viewMode == ViewMode.Queue)
            {
                for (int i = 0; i < TrackListView.Items.Count; i++)
                {
                    if (ReferenceEquals(TrackListView.Items[i], grid))
                    {
                        var t = _queue.PlayIndex(i);
                        if (t != null)
                        {
                            try { await _player.PlayTrackAsync(t); }
                            catch (Exception ex) { TrackArtist.Text = $"Error: {ex.Message}"; }
                            UpdateNowPlaying(t);
                        }
                        return;
                    }
                }
                return;
            }

            // Library/playlist track — set queue and play
            var idx = _displayedTracks.FindIndex(t => t.Id == track.Id);
            if (idx < 0) return;

            _queue.SetQueue(_displayedTracks, idx);
            try { await _player.PlayTrackAsync(track); }
            catch (Exception ex) { TrackArtist.Text = $"Error: {ex.Message}"; }
            UpdateNowPlaying(track);
        }
    }

    // -- Playlists ------------------------------------------------

    private void NavLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Library) return;
        _viewMode = ViewMode.Library;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => ApplyFilterAndSort());
    }

    private void NavPlaylists_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.PlaylistList) return;
        _viewMode = ViewMode.PlaylistList;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => LoadPlaylistList());
    }

    private void NavBack_Click(object sender, RoutedEventArgs e)
    {
        NavPlaylists_Click(sender, e);
    }

    private async void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "New Playlist",
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot
        };
        var input = new TextBox { PlaceholderText = "Playlist name" };
        dialog.Content = input;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary
            && !string.IsNullOrWhiteSpace(input.Text))
        {
            LibraryManager.CreatePlaylist(input.Text.Trim());
            if (_viewMode == ViewMode.PlaylistList)
                LoadPlaylistList();
        }
    }

    private void UpdateNavigation()
    {
        // Toggle tab bar vs playlist detail header
        NavTabs.Visibility = _viewMode != ViewMode.PlaylistDetail
            ? Visibility.Visible : Visibility.Collapsed;
        PlaylistHeader.Visibility = _viewMode == ViewMode.PlaylistDetail
            ? Visibility.Visible : Visibility.Collapsed;
        NewPlaylistBtn.Visibility = _viewMode == ViewMode.PlaylistList
            ? Visibility.Visible : Visibility.Collapsed;
        ClearQueueBtn.Visibility = _viewMode == ViewMode.Queue
            ? Visibility.Visible : Visibility.Collapsed;

        // Highlight active tabs
        void SetTab(TextBlock tb, bool active)
        {
            tb.FontWeight = active
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            tb.Foreground = active
                ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                : ThemeHelper.Brush("TextFillColorPrimaryBrush");
        }
        SetTab(NavLibraryText, _viewMode == ViewMode.Library);
        SetTab(NavPlaylistsText, _viewMode == ViewMode.PlaylistList);
        SetTab(NavQueueText, _viewMode == ViewMode.Queue);
        SetTab(NavVisualizerText, _viewMode == ViewMode.Visualizer);
        SetTab(NavMediaText, _viewMode == ViewMode.MediaControl);

        // Show/hide search & sort
        SearchSortRow.Visibility = (_viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistDetail)
            ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide content containers based on view mode
        var isTrackView = _viewMode != ViewMode.Visualizer && _viewMode != ViewMode.MediaControl;
        TrackListView.Visibility = isTrackView ? Visibility.Visible : Visibility.Collapsed;
        WaveformContainer.Visibility = _viewMode == ViewMode.Visualizer
            ? Visibility.Visible : Visibility.Collapsed;
        MediaContainer.Visibility = _viewMode == ViewMode.MediaControl
            ? Visibility.Visible : Visibility.Collapsed;

        // Playlist detail name
        if (_currentPlaylist != null)
            PlaylistNameText.Text = _currentPlaylist.Name;
    }

    private void AnimateViewTransition(Action buildNewContent, bool slideFromRight = true)
    {
        if (_isViewTransitioning) return;
        _isViewTransitioning = true;

        // Target the visible content container
        FrameworkElement target = _viewMode == ViewMode.Visualizer ? WaveformContainer
            : _viewMode == ViewMode.MediaControl ? MediaContainer
            : TrackListView;

        if (target.RenderTransform is not TranslateTransform)
            target.RenderTransform = new TranslateTransform();

        var transform = (TranslateTransform)target.RenderTransform;
        double direction = slideFromRight ? -1 : 1;

        // Phase 1: Exit — slide out + fade
        var exitX = new DoubleAnimation
        {
            From = 0,
            To = 30 * direction,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(exitX, transform);
        Storyboard.SetTargetProperty(exitX, "X");

        var exitOpacity = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(120)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(exitOpacity, target);
        Storyboard.SetTargetProperty(exitOpacity, "Opacity");

        var exitStoryboard = new Storyboard();
        exitStoryboard.Children.Add(exitX);
        exitStoryboard.Children.Add(exitOpacity);

        exitStoryboard.Completed += (_, _) =>
        {
            buildNewContent();

            // Re-target if container changed (e.g. Library→Visualizer)
            FrameworkElement newTarget = _viewMode == ViewMode.Visualizer ? WaveformContainer
                : _viewMode == ViewMode.MediaControl ? MediaContainer
                : TrackListView;

            if (newTarget.RenderTransform is not TranslateTransform)
                newTarget.RenderTransform = new TranslateTransform();

            var newTransform = (TranslateTransform)newTarget.RenderTransform;

            // Phase 2: Enter — slide in + fade
            var enterX = new DoubleAnimation
            {
                From = -30 * direction,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(enterX, newTransform);
            Storyboard.SetTargetProperty(enterX, "X");

            var enterOpacity = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(150)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(enterOpacity, newTarget);
            Storyboard.SetTargetProperty(enterOpacity, "Opacity");

            var enterStoryboard = new Storyboard();
            enterStoryboard.Children.Add(enterX);
            enterStoryboard.Children.Add(enterOpacity);

            enterStoryboard.Completed += (_, _) =>
            {
                _isViewTransitioning = false;
            };

            enterStoryboard.Begin();
        };

        exitStoryboard.Begin();
    }

    private void LoadPlaylistList()
    {
        TrackListView.CanReorderItems = false;
        var playlists = LibraryManager.GetPlaylists();
        TrackListView.Items.Clear();

        foreach (var playlist in playlists)
        {
            var tracks = LibraryManager.GetPlaylistTracks(playlist.Id);

            var grid = new Grid { Padding = new Thickness(2, 4, 2, 4), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = "\uE8FD",
                FontSize = 14,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 16
            };
            Grid.SetColumn(icon, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = playlist.Name,
                FontSize = 13,
                Foreground = ThemeHelper.Brush("TextFillColorPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
            info.Children.Add(new TextBlock
            {
                Text = $"{tracks.Count} track{(tracks.Count != 1 ? "s" : "")}",
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                MaxLines = 1
            });
            Grid.SetColumn(info, 1);

            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 2);

            grid.Children.Add(icon);
            grid.Children.Add(info);
            grid.Children.Add(chevron);
            grid.Tag = playlist;

            // Context menu (Raycast style)
            var plRef = playlist;
            var ctxFlyout = new Flyout();
            ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
            ctxFlyout.Opening += (_, _) =>
            {
                ctxFlyout.Content = BuildPlaylistContextContent(ctxFlyout, plRef);
            };
            grid.ContextFlyout = ctxFlyout;

            TrackListView.Items.Add(grid);
        }

        TrackCountText.Text = $"{playlists.Count} playlist{(playlists.Count != 1 ? "s" : "")}";
    }

    private StackPanel BuildTrackContextContent(Flyout flyout, TrackInfo track)
    {
        var panel = new StackPanel { Spacing = 0 };

        // Queue actions (not shown in Queue view itself)
        if (_viewMode != ViewMode.Queue)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE768", "Play Next", [], () =>
            {
                flyout.Hide();
                _queue.AddToQueueNext(track);
            }));
            panel.Children.Add(ActionPanel.CreateButton("\uE710", "Add to Queue", [], () =>
            {
                flyout.Hide();
                _queue.AddToQueue(track);
            }));
            panel.Children.Add(ActionPanel.CreateSeparator());
        }

        // Add to Playlist section
        panel.Children.Add(ActionPanel.CreateSectionHeader("Add to Playlist"));
        foreach (var pl in LibraryManager.GetPlaylists())
        {
            var plId = pl.Id;
            panel.Children.Add(ActionPanel.CreateButton("\uE8FD", pl.Name, [], () =>
            {
                flyout.Hide();
                LibraryManager.AddTrackToPlaylist(plId, track.Id);
                if (_viewMode == ViewMode.PlaylistDetail && _currentPlaylist?.Id == plId)
                    ApplyFilterAndSort();
            }));
        }
        panel.Children.Add(ActionPanel.CreateButton("\uE710", "New Playlist...", [], async () =>
        {
            flyout.Hide();
            var dialog = new ContentDialog
            {
                Title = "New Playlist",
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot
            };
            var input = new TextBox { PlaceholderText = "Playlist name" };
            dialog.Content = input;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(input.Text))
            {
                var id = LibraryManager.CreatePlaylist(input.Text.Trim());
                LibraryManager.AddTrackToPlaylist(id, track.Id);
            }
        }));

        // Edit tags
        panel.Children.Add(ActionPanel.CreateSeparator());
        panel.Children.Add(ActionPanel.CreateButton("\uE70F", "Edit Tags", [], () =>
        {
            flyout.Content = BuildMetadataEditorContent(flyout, track);
        }));

        // Remove from playlist (playlist detail only)
        if (_viewMode == ViewMode.PlaylistDetail && _currentPlaylist != null)
        {
            var currentPl = _currentPlaylist;
            panel.Children.Add(ActionPanel.CreateSeparator());
            panel.Children.Add(ActionPanel.CreateButton("\uE74D", "Remove from Playlist", [], () =>
            {
                flyout.Hide();
                LibraryManager.RemoveTrackFromPlaylist(currentPl.Id, track.Id);
                ApplyFilterAndSort();
            }, isDestructive: true));
        }

        return panel;
    }

    private StackPanel BuildPlaylistContextContent(Flyout flyout, PlaylistInfo playlist)
    {
        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(ActionPanel.CreateButton("\uE768", "Play", [], async () =>
        {
            flyout.Hide();
            var tracks = LibraryManager.GetPlaylistTracks(playlist.Id);
            if (tracks.Count > 0)
            {
                _queue.SetQueue(tracks, 0);
                try { await _player.PlayTrackAsync(tracks[0]); }
                catch (Exception ex) { TrackArtist.Text = $"Error: {ex.Message}"; }
                UpdateNowPlaying(tracks[0]);
            }
        }));
        panel.Children.Add(ActionPanel.CreateButton("\uE8AC", "Rename", [], async () =>
        {
            flyout.Hide();
            var dialog = new ContentDialog
            {
                Title = "Rename Playlist",
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot
            };
            var input = new TextBox { Text = playlist.Name };
            dialog.Content = input;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(input.Text))
            {
                LibraryManager.RenamePlaylist(playlist.Id, input.Text.Trim());
                LoadPlaylistList();
            }
        }));
        panel.Children.Add(ActionPanel.CreateSeparator());
        panel.Children.Add(ActionPanel.CreateButton("\uE74D", "Delete", [], () =>
        {
            flyout.Hide();
            LibraryManager.DeletePlaylist(playlist.Id);
            LoadPlaylistList();
        }, isDestructive: true));

        return panel;
    }

    private StackPanel BuildQueueItemContextContent(Flyout flyout, TrackInfo track, int index)
    {
        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(ActionPanel.CreateButton("\uE768", "Play", [], async () =>
        {
            flyout.Hide();
            var t = _queue.PlayIndex(index);
            if (t != null)
            {
                try { await _player.PlayTrackAsync(t); }
                catch (Exception ex) { TrackArtist.Text = $"Error: {ex.Message}"; }
                UpdateNowPlaying(t);
            }
        }));

        panel.Children.Add(ActionPanel.CreateSeparator());

        if (index > 0)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE74A", "Move Up", [], () =>
            {
                flyout.Hide();
                _queue.MoveInQueue(index, index - 1);
                BuildQueueView();
            }));
        }
        if (index < _queue.Queue.Count - 1)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE74B", "Move Down", [], () =>
            {
                flyout.Hide();
                _queue.MoveInQueue(index, index + 1);
                BuildQueueView();
            }));
        }

        panel.Children.Add(ActionPanel.CreateSeparator());

        panel.Children.Add(ActionPanel.CreateButton("\uE74D", "Remove", [], () =>
        {
            flyout.Hide();
            _queue.RemoveFromQueue(index);
            BuildQueueView();
        }, isDestructive: true));

        panel.Children.Add(ActionPanel.CreateButton("\uE74D", "Clear Queue", [], () =>
        {
            flyout.Hide();
            _queue.Clear();
            BuildQueueView();
        }, isDestructive: true));

        return panel;
    }

    private StackPanel BuildMetadataEditorContent(Flyout flyout, TrackInfo track)
    {
        var panel = new StackPanel { Spacing = 6, Padding = new Thickness(4) };

        // Back button + header
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var backBtn = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 2, 4, 2),
            MinHeight = 0, MinWidth = 0,
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 12 }
        };
        backBtn.Click += (_, _) =>
        {
            flyout.Content = BuildTrackContextContent(flyout, track);
        };
        header.Children.Add(backBtn);
        header.Children.Add(new TextBlock
        {
            Text = "Edit Tags",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(header);
        panel.Children.Add(ActionPanel.CreateSeparator());

        // Artwork preview + buttons
        var artworkGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        artworkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        artworkGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var artPreview = new Image
        {
            Width = 64, Height = 64,
            Stretch = Stretch.UniformToFill
        };
        var artPlaceholder = new FontIcon
        {
            Glyph = "\uE8D6", FontSize = 24, Width = 64, Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
        };

        byte[]? pendingArtwork = null;
        bool removeArtwork = false;

        // Load current artwork
        try
        {
            using var tagFile = TagLib.File.Create(track.Path);
            if (tagFile.Tag.Pictures.Length > 0)
            {
                var pic = tagFile.Tag.Pictures[0];
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(pic.Data.Data);
                _ = writer.StoreAsync().AsTask().Result;
                stream.Seek(0);
                var bitmap = new BitmapImage();
                bitmap.SetSource(stream);
                artPreview.Source = bitmap;
                artPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                artPreview.Visibility = Visibility.Collapsed;
            }
        }
        catch
        {
            artPreview.Visibility = Visibility.Collapsed;
        }

        var artContainer = new Grid
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(4),
            Background = ThemeHelper.Brush("CardBackgroundFillColorSecondaryBrush")
        };
        artContainer.Children.Add(artPreview);
        artContainer.Children.Add(artPlaceholder);
        Grid.SetColumn(artContainer, 0);

        var artButtons = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Spacing = 4
        };

        var changeArtBtn = new Button
        {
            Content = "Change",
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            MinHeight = 0
        };
        changeArtBtn.Click += async (_, _) =>
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");

            InitializeWithWindow.Initialize(picker, _hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            pendingArtwork = await System.IO.File.ReadAllBytesAsync(file.Path);
            removeArtwork = false;

            var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
            writer.WriteBytes(pendingArtwork);
            await writer.StoreAsync();
            stream.Seek(0);
            var bitmap = new BitmapImage();
            bitmap.SetSource(stream);
            artPreview.Source = bitmap;
            artPreview.Visibility = Visibility.Visible;
            artPlaceholder.Visibility = Visibility.Collapsed;
        };
        artButtons.Children.Add(changeArtBtn);

        var removeArtBtn = new Button
        {
            Content = "Remove",
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            MinHeight = 0
        };
        removeArtBtn.Click += (_, _) =>
        {
            removeArtwork = true;
            pendingArtwork = null;
            artPreview.Source = null;
            artPreview.Visibility = Visibility.Collapsed;
            artPlaceholder.Visibility = Visibility.Visible;
        };
        artButtons.Children.Add(removeArtBtn);

        Grid.SetColumn(artButtons, 1);
        artworkGrid.Children.Add(artContainer);
        artworkGrid.Children.Add(artButtons);
        panel.Children.Add(artworkGrid);

        // Text fields
        var titleBox = new TextBox
        {
            Header = "Title",
            Text = track.Title,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5)
        };
        panel.Children.Add(titleBox);

        var artistBox = new TextBox
        {
            Header = "Artist",
            Text = track.Artist,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5)
        };
        panel.Children.Add(artistBox);

        var albumBox = new TextBox
        {
            Header = "Album",
            Text = track.Album,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5)
        };
        panel.Children.Add(albumBox);

        // Error message area
        var errorText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 99, 99)),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(errorText);

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Save / Cancel buttons
        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var saveBtn = new Button
        {
            Content = "Save",
            FontSize = 12,
            Padding = new Thickness(16, 5, 16, 5),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        saveBtn.Click += (_, _) =>
        {
            var newTitle = titleBox.Text.Trim();
            var newArtist = artistBox.Text.Trim();
            var newAlbum = albumBox.Text.Trim();

            if (string.IsNullOrEmpty(newTitle))
                newTitle = Path.GetFileNameWithoutExtension(track.Path);

            // Write tags to file
            var result = MetadataWriter.WriteTags(track.Path, newTitle, newArtist, newAlbum);
            if (!result.Success)
            {
                errorText.Text = result.Error ?? "Unknown error";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            // Write artwork if changed
            if (pendingArtwork != null || removeArtwork)
            {
                var artResult = MetadataWriter.WriteArtwork(track.Path,
                    removeArtwork ? null : pendingArtwork);
                if (!artResult.Success)
                {
                    errorText.Text = artResult.Error ?? "Unknown error";
                    errorText.Visibility = Visibility.Visible;
                    return;
                }
            }

            // Update database
            LibraryManager.UpdateTrackMetadata(track.Id, newTitle, newArtist, newAlbum);

            // Update in-memory track
            track.Title = newTitle;
            track.Artist = newArtist;
            track.Album = newAlbum;

            // Refresh UI
            LoadTracks();

            // If this is the currently playing track, update now-playing display
            if (_queue.CurrentTrack?.Id == track.Id)
            {
                _queue.CurrentTrack.Title = newTitle;
                _queue.CurrentTrack.Artist = newArtist;
                _queue.CurrentTrack.Album = newAlbum;
                TrackTitle.Text = newTitle;
                TrackArtist.Text = newArtist;
                TrackAlbum.Text = newAlbum;
                UpdateMiniPlayer(_queue.CurrentTrack);

                if (pendingArtwork != null || removeArtwork)
                    LoadAlbumArt(track.Path);
            }

            flyout.Hide();
        };
        Grid.SetColumn(saveBtn, 1);

        var cancelBtn = new Button
        {
            Content = "Cancel",
            FontSize = 12,
            Padding = new Thickness(12, 5, 12, 5)
        };
        cancelBtn.Click += (_, _) => flyout.Hide();
        Grid.SetColumn(cancelBtn, 0);

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(saveBtn);
        panel.Children.Add(buttonRow);

        return panel;
    }

    // -- Queue view -----------------------------------------------

    private void NavQueue_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Queue) return;
        _viewMode = ViewMode.Queue;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => BuildQueueView());
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        BuildQueueView();
    }

    // -- Visualizer -----------------------------------------------

    private void NavVisualizer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Visualizer) return;
        _viewMode = ViewMode.Visualizer;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => { /* visualizer draws via timer */ });
    }

    private void NavMedia_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.MediaControl) return;
        _viewMode = ViewMode.MediaControl;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        AnimateViewTransition(() => _ = InitMediaSessionsAsync());
    }

    // -- Media control ------------------------------------------------

    private async Task InitMediaSessionsAsync()
    {
        if (_mediaSessionManager == null)
        {
            try
            {
                _mediaSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mediaSessionManager.SessionsChanged += (_, _) =>
                    DispatcherQueue.TryEnqueue(RebuildMediaSessionList);
            }
            catch { return; }
        }

        RebuildMediaSessionList();
        UpdateMediaTimer();
    }

    private void RebuildMediaSessionList()
    {
        if (_mediaSessionManager == null) return;

        // Detach old panels
        foreach (var panel in _mediaSessionPanels.Values)
            panel.Detach();
        _mediaSessionPanels.Clear();
        MediaSessionList.Children.Clear();

        var sessions = _mediaSessionManager.GetSessions();

        if (sessions.Count == 0)
        {
            MediaSessionList.Children.Add(new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = 8,
                Margin = new Thickness(0, 40, 0, 0),
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE8D6", FontSize = 36,
                        Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "No media playing",
                        Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Play music or a video to control it here",
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            });
        }
        else
        {
            foreach (var session in sessions)
            {
                var id = session.SourceAppUserModelId;
                var panel = new MediaSessionPanel(session, DispatcherQueue);
                _mediaSessionPanels[id] = panel;
                MediaSessionList.Children.Add(panel.RootElement);
            }
        }

        TrackCountText.Text = $"{sessions.Count} session{(sessions.Count != 1 ? "s" : "")}";
    }

    private void UpdateMediaTimer()
    {
        bool needsTimer = _viewMode == ViewMode.MediaControl && _mediaSessionPanels.Count > 0;
        if (needsTimer && _mediaTickTimer == null)
        {
            _mediaTickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mediaTickTimer.Tick += (_, _) =>
            {
                foreach (var panel in _mediaSessionPanels.Values)
                    panel.UpdateTimeline();
            };
            _mediaTickTimer.Start();
        }
        else if (!needsTimer && _mediaTickTimer != null)
        {
            _mediaTickTimer.Stop();
            _mediaTickTimer = null;
        }
    }

    private void UpdateSpectrumTimer()
    {
        bool needsTimer = _viewMode == ViewMode.Visualizer;
        if (needsTimer && _spectrumTimer == null)
        {
            PrepareSpectrumForCurrentTrack();
            int ms = _vizFps >= 60 ? 16 : 33;
            _spectrumTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            _spectrumTimer.Tick += (_, _) =>
            {
                if (_viewMode == ViewMode.Visualizer)
                    DrawVisualization();
            };
            _spectrumTimer.Start();
        }
        else if (!needsTimer && _spectrumTimer != null)
        {
            _spectrumTimer.Stop();
            _spectrumTimer = null;
            _vizBandCount = 0;
            _vizNoTrackText = null;
        }
    }

    public void ApplyVisualizerFps(int fps)
    {
        _vizFps = fps;
        if (_spectrumTimer != null)
        {
            _spectrumTimer.Stop();
            _spectrumTimer = null;
            UpdateSpectrumTimer();
        }
    }

    private async void PrepareSpectrumForCurrentTrack()
    {
        var track = _player.CurrentTrack;
        if (track != null)
            await _spectrum.PrepareAsync(track.Path);
    }

    private void DrawVisualization()
    {
        var w = WaveformContainer.ActualWidth;
        var h = WaveformContainer.ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_player.CurrentTrack == null)
        {
            // Show "no track" only if not already shown
            if (_vizNoTrackText == null)
            {
                WaveformCanvas.Children.Clear();
                _vizBandCount = 0;
                _vizNoTrackText = new TextBlock
                {
                    Text = "No track playing", FontSize = 13,
                    Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                };
                Canvas.SetLeft(_vizNoTrackText, w / 2 - 50);
                Canvas.SetTop(_vizNoTrackText, h / 2 - 10);
                WaveformCanvas.Children.Add(_vizNoTrackText);
            }
            return;
        }
        _vizNoTrackText = null;

        double barW = 4, gap = 2, step = barW + gap;
        int bandCount = Math.Max(1, (int)(w / step));
        double ox = (w - bandCount * step) / 2;
        double centerY = h / 2, halfMax = h * 0.44;

        // Rebuild bars only when band count changes (window resize)
        if (bandCount != _vizBandCount)
        {
            WaveformCanvas.Children.Clear();
            _vizBandCount = bandCount;

            var accent = ThemeHelper.Brush("AccentFillColorDefaultBrush");
            var accentDim = ThemeHelper.Brush("AccentFillColorSecondaryBrush");

            for (int i = 0; i < bandCount; i++)
            {
                double x = ox + i * step;

                var upper = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = barW, Height = 2,
                    RadiusX = 2, RadiusY = 2,
                    Fill = accent
                };
                Canvas.SetLeft(upper, x);
                Canvas.SetTop(upper, centerY - 2);
                WaveformCanvas.Children.Add(upper);

                var lower = new Microsoft.UI.Xaml.Shapes.Rectangle
                {
                    Width = barW, Height = 2,
                    RadiusX = 2, RadiusY = 2,
                    Fill = accentDim,
                    Opacity = 0.4
                };
                Canvas.SetLeft(lower, x);
                Canvas.SetTop(lower, centerY + 2);
                WaveformCanvas.Children.Add(lower);
            }
        }

        // Update existing bar heights/positions — no allocation
        var bands = _spectrum.GetSpectrum(_player.Position, bandCount);

        for (int i = 0; i < bandCount && i < bands.Length; i++)
        {
            double bh = 2 + bands[i] * (halfMax - 2);

            var upper = (Microsoft.UI.Xaml.Shapes.Rectangle)WaveformCanvas.Children[i * 2];
            upper.Height = bh;
            Canvas.SetTop(upper, centerY - bh);

            var lower = (Microsoft.UI.Xaml.Shapes.Rectangle)WaveformCanvas.Children[i * 2 + 1];
            lower.Height = bh * 0.6;
        }
    }

    private void WaveformContainer_SizeChanged(object sender, SizeChangedEventArgs e) { }

    private void WaveformCanvas_PointerPressed(object sender, PointerRoutedEventArgs e) { }

    private void BuildQueueView()
    {
        TrackListView.CanReorderItems = false;
        TrackListView.Items.Clear();
        var queue = _queue.Queue;

        for (int i = 0; i < queue.Count; i++)
        {
            var track = queue[i];
            var isCurrent = i == _queue.CurrentIndex;

            var grid = new Grid { Padding = new Thickness(2, 4, 2, 4), ColumnSpacing = 6 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // pos
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // info
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // duration
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // reorder

            // Position number or speaker icon for current
            FrameworkElement posElement;
            if (isCurrent)
            {
                posElement = new FontIcon
                {
                    Glyph = "\uE767",
                    FontSize = 12,
                    Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 20
                };
            }
            else
            {
                posElement = new TextBlock
                {
                    Text = $"{i + 1}",
                    FontSize = 11,
                    Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 20,
                    TextAlignment = TextAlignment.Center
                };
            }
            Grid.SetColumn(posElement, 0);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = track.Title,
                FontSize = 13,
                FontWeight = isCurrent
                    ? Microsoft.UI.Text.FontWeights.SemiBold
                    : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isCurrent
                    ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                    : ThemeHelper.Brush("TextFillColorPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });

            var subtitle = new List<string>();
            if (!string.IsNullOrEmpty(track.Artist)) subtitle.Add(track.Artist);
            if (!string.IsNullOrEmpty(track.Album)) subtitle.Add(track.Album);
            if (subtitle.Count > 0)
            {
                info.Children.Add(new TextBlock
                {
                    Text = string.Join(" \u00B7 ", subtitle),
                    FontSize = 11,
                    Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1
                });
            }
            Grid.SetColumn(info, 1);

            var dur = new TextBlock
            {
                Text = track.DurationFormatted,
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dur, 2);

            // Reorder Up/Down buttons
            var reorderPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 0
            };
            if (i > 0)
            {
                var fromIdx = i;
                var upBtn = new Button
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 1, 4, 1),
                    MinHeight = 0,
                    MinWidth = 0,
                    Content = new FontIcon
                    {
                        Glyph = "\uE74A",
                        FontSize = 9,
                        Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
                    }
                };
                upBtn.Click += (_, _) =>
                {
                    _queue.MoveInQueue(fromIdx, fromIdx - 1);
                    BuildQueueView();
                };
                reorderPanel.Children.Add(upBtn);
            }
            if (i < queue.Count - 1)
            {
                var fromIdx = i;
                var downBtn = new Button
                {
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(4, 1, 4, 1),
                    MinHeight = 0,
                    MinWidth = 0,
                    Content = new FontIcon
                    {
                        Glyph = "\uE74B",
                        FontSize = 9,
                        Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
                    }
                };
                downBtn.Click += (_, _) =>
                {
                    _queue.MoveInQueue(fromIdx, fromIdx + 1);
                    BuildQueueView();
                };
                reorderPanel.Children.Add(downBtn);
            }
            Grid.SetColumn(reorderPanel, 3);

            grid.Children.Add(posElement);
            grid.Children.Add(info);
            grid.Children.Add(dur);
            grid.Children.Add(reorderPanel);
            grid.Tag = track;

            // Context menu (Raycast style)
            var capturedTrack = track;
            var capturedGrid = grid;
            var ctxFlyout = new Flyout();
            ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
            ctxFlyout.Opening += (_, _) =>
            {
                int currentIdx = TrackListView.Items.IndexOf(capturedGrid);
                ctxFlyout.Content = BuildQueueItemContextContent(ctxFlyout, capturedTrack, currentIdx);
            };
            grid.ContextFlyout = ctxFlyout;

            TrackListView.Items.Add(grid);
        }

        TrackCountText.Text = $"{queue.Count} in queue";
    }

    private void TrackListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_viewMode != ViewMode.Queue) return;

        var newOrder = new List<TrackInfo>();
        foreach (var item in TrackListView.Items)
        {
            if (item is Grid g && g.Tag is TrackInfo t)
                newOrder.Add(t);
        }

        if (newOrder.Count > 0)
            _queue.ReorderQueue(newOrder);
    }

    // -- Drag & drop from Explorer --------------------------------

    private void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to Queue";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var audioFiles = new List<string>();

        foreach (var item in items)
        {
            if (item is StorageFile file)
            {
                var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                if (LibraryManager.AudioExtensions.Contains(ext))
                    audioFiles.Add(file.Path);
            }
        }

        if (audioFiles.Count == 0) return;

        bool queueWasEmpty = _queue.Queue.Count == 0;

        foreach (var filePath in audioFiles)
        {
            var track = ReadTrackMetadata(filePath);
            if (track == null) continue;

            if (queueWasEmpty && filePath == audioFiles[0])
            {
                // Empty queue — play directly
                _queue.SetQueue([track], 0);
                try { await _player.PlayTrackAsync(track); }
                catch (Exception ex) { TrackArtist.Text = $"Error: {ex.Message}"; }
                UpdateNowPlaying(track);
                queueWasEmpty = false;
            }
            else
            {
                _queue.AddToQueue(track);
            }
        }

        if (_viewMode == ViewMode.Queue)
            BuildQueueView();
    }

    private static TrackInfo? ReadTrackMetadata(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            return new TrackInfo
            {
                Id = -1,
                Path = filePath,
                Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : tagFile.Tag.Title.Trim(),
                Artist = tagFile.Tag.FirstPerformer?.Trim() ?? "",
                Album = tagFile.Tag.Album?.Trim() ?? "",
                DurationMs = (int)tagFile.Properties.Duration.TotalMilliseconds,
                TrackNumber = (int)tagFile.Tag.Track,
                Year = (int)tagFile.Tag.Year,
                Genre = tagFile.Tag.FirstGenre ?? ""
            };
        }
        catch { return null; }
    }

    // -- Bottom bar -----------------------------------------------

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, _hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder == null) return;

        var folderId = LibraryManager.AddFolder(folder.Path);

        // Scan in background
        TrackCountText.Text = "Scanning...";
        await LibraryManager.ScanFolderAsync(folderId, folder.Path);
        LoadTracks();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFlyout(sender as FrameworkElement ?? RootGrid);
    }

    private void ShowSettingsFlyout(FrameworkElement anchor)
    {
        var currentBackdrop = SettingsManager.LoadBackdrop().Type;
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();

        var panel = new StackPanel { Spacing = 0 };

        // Header
        panel.Children.Add(ActionPanel.CreateSectionHeader("Actions"));
        panel.Children.Add(ActionPanel.CreateSeparator());

        // Library actions
        panel.Children.Add(ActionPanel.CreateButton("\uE838", "Add Folder", [], () =>
        {
            flyout.Hide();
            ChooseFolder_Click(this, new RoutedEventArgs());
        }));
        panel.Children.Add(ActionPanel.CreateButton("\uE72C", "Scan Library", [], () =>
        {
            flyout.Hide();
            ScanAllFoldersAsync();
        }));
        panel.Children.Add(ActionPanel.CreateButton("\uE74D", "Reset Library", [], () =>
        {
            flyout.Hide();
            LibraryManager.ResetLibrary();
            _allTracks.Clear();
            _displayedTracks.Clear();
            _queue.Clear();
            _viewMode = ViewMode.Library;
            _currentPlaylist = null;
            UpdateNavigation();
            ApplyFilterAndSort();
        }, isDestructive: true));

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Backdrop section
        panel.Children.Add(ActionPanel.CreateSectionHeader("Backdrop"));

        void AddBackdropOption(string type, string label)
        {
            var isActive = currentBackdrop == type;
            panel.Children.Add(ActionPanel.CreateButton(
                isActive ? "\uE73E" : "\uE8D7", label, [], () =>
            {
                var bd = new BackdropSettings(Type: type);
                SettingsManager.SaveBackdrop(bd);
                ApplyBackdrop(bd);
                flyout.Hide();
            }, isActive: isActive));
        }

        AddBackdropOption("acrylic", "Acrylic");
        AddBackdropOption("mica", "Mica");
        AddBackdropOption("mica_alt", "Mica Alt");
        AddBackdropOption("none", "None");

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Theme section
        var currentTheme = SettingsManager.LoadTheme();
        panel.Children.Add(ActionPanel.CreateSectionHeader("Theme"));

        void AddThemeOption(string theme, string label)
        {
            var isActive = currentTheme == theme;
            panel.Children.Add(ActionPanel.CreateButton(
                isActive ? "\uE73E" : "\uE8D7", label, [], () =>
            {
                SettingsManager.SaveTheme(theme);
                ApplyTheme(theme);
                flyout.Hide();
            }, isActive: isActive));
        }

        AddThemeOption("system", "System");
        AddThemeOption("light", "Light");
        AddThemeOption("dark", "Dark");

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Visualizer FPS
        panel.Children.Add(ActionPanel.CreateSectionHeader("Visualizer"));

        void AddFpsOption(int fps, string label)
        {
            var isActive = _vizFps == fps;
            panel.Children.Add(ActionPanel.CreateButton(
                isActive ? "\uE73E" : "\uE8D7", label, [], () =>
            {
                ApplyVisualizerFps(fps);
                var s = SettingsManager.Load();
                SettingsManager.Save(s with { VisualizerFps = fps });
                flyout.Hide();
            }, isActive: isActive));
        }

        AddFpsOption(30, "30 FPS");
        AddFpsOption(60, "60 FPS");

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Toggle actions
        panel.Children.Add(ActionPanel.CreateButton("\uE73F",
            _collapseState == CollapseState.Expanded ? "Compact Mode" :
            _collapseState == CollapseState.Compact ? "Mini Player" : "Expand",
            ["Ctrl", "L"], () =>
        {
            flyout.Hide();
            ToggleCollapse();
        }));

        panel.Children.Add(ActionPanel.CreateButton(
            _isPinnedOnTop ? "\uE842" : "\uE840",
            _isPinnedOnTop ? "Unpin from Top" : "Pin on Top",
            [], () =>
        {
            flyout.Hide();
            Pin_Click(this, new RoutedEventArgs());
        }));

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Quit
        panel.Children.Add(ActionPanel.CreateButton("\uE711", "Quit", [], () =>
        {
            flyout.Hide();
            _isQuitting = true;
            Close();
        }, isDestructive: true));

        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private async void ScanAllFoldersAsync()
    {
        TrackCountText.Text = "Scanning...";
        try
        {
            var added = await LibraryManager.ScanAllFoldersAsync();
            LoadTracks();
            TrackCountText.Text = $"{_allTracks.Count:N0} tracks";
        }
        catch (Exception ex)
        {
            TrackCountText.Text = $"Error: {ex.Message}";
        }
    }

    // -- Backdrop -------------------------------------------------

    private void ApplyBackdrop(BackdropSettings settings)
    {
        SystemBackdrop = settings.Type switch
        {
            "mica" => new MicaBackdrop(),
            "mica_alt" => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            "none" => null,
            _ => new DesktopAcrylicBackdrop()
        };
    }

    private void ApplyTheme(string theme)
    {
        if (Content is FrameworkElement root)
        {
            var elementTheme = theme switch
            {
                "light" => ElementTheme.Light,
                "dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
            root.RequestedTheme = elementTheme;
            ThemeHelper.CurrentTheme = elementTheme;

            // Rebuild dynamic UI so code-behind elements pick up the new theme brushes
            ApplyFilterAndSort();
            UpdateNavigation();
            UpdateRepeatIcon();
            ShuffleIcon.Foreground = _queue.Shuffle
                ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                : ThemeHelper.Brush("TextFillColorPrimaryBrush");
        }
    }

    // -- Collapse animation ----------------------------------------

    private void ToggleCollapse()
    {
        // Cycle: Expanded → Compact → Mini → Expanded
        _collapseState = _collapseState switch
        {
            CollapseState.Expanded => CollapseState.Compact,
            CollapseState.Compact => CollapseState.Mini,
            CollapseState.Mini => CollapseState.Expanded,
            _ => CollapseState.Expanded
        };

        _targetHeight = _collapseState switch
        {
            CollapseState.Mini => _miniHeight,
            CollapseState.Compact => _collapsedHeight,
            _ => _expandedHeight
        };

        _currentAnimHeight = AppWindow.Size.Height;

        // Keep bottom edge fixed
        _animStartY = AppWindow.Position.Y;
        var bottomEdge = _animStartY + AppWindow.Size.Height;
        _targetY = bottomEdge - _targetHeight;

        // Update icon and tooltip
        CollapseIcon.Glyph = _collapseState switch
        {
            CollapseState.Expanded => "\uE73F",
            CollapseState.Compact => "\uE73F",
            CollapseState.Mini => "\uE740",
            _ => "\uE73F"
        };
        ToolTipService.SetToolTip(CollapseButton, _collapseState switch
        {
            CollapseState.Expanded => "Compact (Ctrl+L)",
            CollapseState.Compact => "Mini (Ctrl+L)",
            CollapseState.Mini => "Expand (Ctrl+L)",
            _ => "Compact (Ctrl+L)"
        });

        // Show elements before expanding animation
        if (_collapseState == CollapseState.Expanded)
        {
            NowPlayingCard.Visibility = Visibility.Visible;
            MiniPlayerBar.Visibility = Visibility.Collapsed;
            VolumeRow.Visibility = Visibility.Visible;
            NavRow.Visibility = Visibility.Visible;
            SearchSortRow.Visibility = (_viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistDetail)
                ? Visibility.Visible : Visibility.Collapsed;
            TrackListView.Visibility = _viewMode != ViewMode.Visualizer && _viewMode != ViewMode.MediaControl
                ? Visibility.Visible : Visibility.Collapsed;
            WaveformContainer.Visibility = _viewMode == ViewMode.Visualizer
                ? Visibility.Visible : Visibility.Collapsed;
            MediaContainer.Visibility = _viewMode == ViewMode.MediaControl
                ? Visibility.Visible : Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Visible;
            CustomTitleBar.Visibility = Visibility.Visible;
        }
        else if (_collapseState == CollapseState.Compact)
        {
            NowPlayingCard.Visibility = Visibility.Visible;
            MiniPlayerBar.Visibility = Visibility.Collapsed;
            CustomTitleBar.Visibility = Visibility.Visible;
        }
        else if (_collapseState == CollapseState.Mini)
        {
            // Hide everything immediately before animation
            CustomTitleBar.Visibility = Visibility.Collapsed;
            NowPlayingCard.Visibility = Visibility.Collapsed;
            VolumeRow.Visibility = Visibility.Collapsed;
            NavRow.Visibility = Visibility.Collapsed;
            SearchSortRow.Visibility = Visibility.Collapsed;
            TrackListView.Visibility = Visibility.Collapsed;
            WaveformContainer.Visibility = Visibility.Collapsed;
            MediaContainer.Visibility = Visibility.Collapsed;
            BottomBar.Visibility = Visibility.Collapsed;
            MiniPlayerBar.Visibility = Visibility.Visible;
            UpdateMiniPlayer(_queue.CurrentTrack);
            MiniPlayPauseIcon.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        }

        _animTimer?.Stop();
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        _animTimer.Tick += AnimTick;
        _animTimer.Start();
    }

    private void AnimTick(object? sender, object e)
    {
        var diff = _targetHeight - _currentAnimHeight;

        if (Math.Abs(diff) <= 4)
        {
            _currentAnimHeight = _targetHeight;
            _animTimer?.Stop();
            _animTimer = null;

            // Set final visibility based on collapse state
            if (_collapseState == CollapseState.Compact)
            {
                VolumeRow.Visibility = Visibility.Collapsed;
                NavRow.Visibility = Visibility.Collapsed;
                SearchSortRow.Visibility = Visibility.Collapsed;
                TrackListView.Visibility = Visibility.Collapsed;
                WaveformContainer.Visibility = Visibility.Collapsed;
                MediaContainer.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
                MiniPlayerBar.Visibility = Visibility.Collapsed;
                NowPlayingCard.Visibility = Visibility.Visible;
            }
            else if (_collapseState == CollapseState.Mini)
            {
                CustomTitleBar.Visibility = Visibility.Collapsed;
                NowPlayingCard.Visibility = Visibility.Collapsed;
                VolumeRow.Visibility = Visibility.Collapsed;
                NavRow.Visibility = Visibility.Collapsed;
                SearchSortRow.Visibility = Visibility.Collapsed;
                TrackListView.Visibility = Visibility.Collapsed;
                WaveformContainer.Visibility = Visibility.Collapsed;
                MediaContainer.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
                MiniPlayerBar.Visibility = Visibility.Visible;
            }

            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                AppWindow.Position.X, _targetY,
                AppWindow.Size.Width, _currentAnimHeight));
        }
        else
        {
            _currentAnimHeight += (int)(diff * 0.18);
            var newY = _targetY + (_targetHeight - _currentAnimHeight);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                AppWindow.Position.X, newY,
                AppWindow.Size.Width, _currentAnimHeight));
        }
    }

    // -- Window chrome --------------------------------------------

    private void DragArea_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) return;
        _isDragging = true;
        GetCursorPos(out _dragStartCursor);
        _dragStartPos = AppWindow.Position;
        ((UIElement)sender).CapturePointer(e.Pointer);
    }

    private void DragArea_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        GetCursorPos(out var current);
        var dx = current.X - _dragStartCursor.X;
        var dy = current.Y - _dragStartCursor.Y;
        AppWindow.Move(new Windows.Graphics.PointInt32(_dragStartPos.X + dx, _dragStartPos.Y + dy));
    }

    private void DragArea_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    private void RestoreWindowPosition()
    {
        var settings = SettingsManager.Load();
        var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
            AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        if (settings.WindowX.HasValue && settings.WindowY.HasValue)
        {
            var x = settings.WindowX.Value;
            var y = settings.WindowY.Value;

            // Validate position is within screen bounds
            if (x >= workArea.X && x < workArea.X + workArea.Width - 100 &&
                y >= workArea.Y && y < workArea.Y + workArea.Height - 100)
            {
                AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
                return;
            }
        }

        // Default: bottom-right corner
        var defaultX = workArea.X + workArea.Width - AppWindow.Size.Width - 16;
        var defaultY = workArea.Y + workArea.Height - AppWindow.Size.Height - 16;
        AppWindow.Move(new Windows.Graphics.PointInt32(defaultX, defaultY));
    }

    private void CollapseToggle_Click(object sender, RoutedEventArgs e)
    {
        ToggleCollapse();
    }

    private void RootGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_collapseState == CollapseState.Mini)
        {
            ShowMiniContextMenu(sender as FrameworkElement ?? RootGrid);
        }
        else
        {
            ShowSettingsFlyout(sender as FrameworkElement ?? RootGrid);
        }
        e.Handled = true;
    }

    private void ShowMiniContextMenu(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(minWidth: 140, maxWidth: 180);

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(ActionPanel.CreateButton("\uE740", "Expand", ["Ctrl", "L"], () =>
        {
            flyout.Hide();
            ToggleCollapse();
        }));

        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        _isPinnedOnTop = !_isPinnedOnTop;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = _isPinnedOnTop;

        PinIcon.Glyph = _isPinnedOnTop ? "\uE842" : "\uE840";
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                Close_Click(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Space when !SearchBox.FocusState.HasFlag(FocusState.Keyboard):
                PlayPause_Click(sender, e);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.L when
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down):
                ToggleCollapse();
                e.Handled = true;
                break;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_isQuitting)
            Close();
        else
        {
            ShowWindow(_hwnd, 0); // Hide to tray
            _isVisible = false;
            SuspendTimers();
        }
    }

    // -- System integration ----------------------------------------

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_HOTKEY when wParam == (IntPtr)HOTKEY_ID:
                DispatcherQueue.TryEnqueue(ToggleWindow);
                return IntPtr.Zero;

            case WM_HOTKEY when wParam == (IntPtr)HOTKEY_COLLAPSE_ID:
                if (_isVisible) DispatcherQueue.TryEnqueue(ToggleCollapse);
                return IntPtr.Zero;

            case WM_TRAYICON:
                var mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg == WM_LBUTTONUP)
                    DispatcherQueue.TryEnqueue(ToggleWindow);
                else if (mouseMsg == WM_RBUTTONUP)
                    ShowTrayContextMenu();
                return IntPtr.Zero;

            case WM_COMMAND:
                var cmdId = (int)(wParam.ToInt64() & 0xFFFF);
                if (cmdId == IDM_SHOW) DispatcherQueue.TryEnqueue(ToggleWindow);
                else if (cmdId == IDM_QUIT) DispatcherQueue.TryEnqueue(() => { _isQuitting = true; Close(); });
                return IntPtr.Zero;
        }
        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ToggleWindow()
    {
        if (_isVisible)
        {
            ShowWindow(_hwnd, 0); // SW_HIDE
            _isVisible = false;
            SuspendTimers();
        }
        else
        {
            ShowWindow(_hwnd, 5); // SW_SHOW
            SetForegroundWindow(_hwnd);
            _isVisible = true;
            ResumeTimers();
        }
    }

    private void SuspendTimers()
    {
        _player.SuspendPositionTimer();
        _spectrumTimer?.Stop();
    }

    private void ResumeTimers()
    {
        _player.ResumePositionTimer();
        if (_viewMode == ViewMode.Visualizer)
            _spectrumTimer?.Start();
    }

    private void AddTrayIcon()
    {
        _trayIcon = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadImage(IntPtr.Zero,
                Path.Combine(AppContext.BaseDirectory, "app.ico"),
                IMAGE_ICON, 16, 16, LR_LOADFROMFILE),
            szTip = "Audiomatic"
        };
        Shell_NotifyIcon(NIM_ADD, ref _trayIcon);
    }

    private void RemoveTrayIcon()
    {
        Shell_NotifyIcon(NIM_DELETE, ref _trayIcon);
    }

    private void ShowTrayContextMenu()
    {
        var hMenu = CreatePopupMenu();
        AppendMenu(hMenu, 0, IDM_SHOW, "Show\tCtrl+Alt+M");
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, 0, IDM_QUIT, "Quit");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        TrackPopupMenu(hMenu, 0, pt.X, pt.Y, 0, _hwnd, IntPtr.Zero);
        DestroyMenu(hMenu);
    }

    // -- Helpers --------------------------------------------------

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}
