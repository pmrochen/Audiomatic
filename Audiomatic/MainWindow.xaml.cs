using System.Runtime.InteropServices;
using Audiomatic.Models;
using Audiomatic.Services;
using Audiomatic.Visualizer;
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
using WinRT;
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
    private enum ViewMode { Library, PlaylistList, PlaylistDetail, Queue, Radio, Podcast, PodcastEpisodes, Visualizer, Equalizer, MediaControl, Albums, AlbumDetail, Artists, ArtistDetail, Stats }
    private ViewMode _viewMode = ViewMode.Library;
    private PlaylistInfo? _currentPlaylist;

    // Play history tracking
    private long _playStartTrackId;
    private DateTime _playStartTime;

    // Radio
    private List<RadioStation> _radioStations = [];
    private bool _isRadioPlaying;

    // Podcast
    private List<PodcastInfo> _podcastSubscriptions = [];
    private PodcastInfo? _currentPodcast;
    private PodcastEpisode? _currentEpisode;
    private HashSet<string> _readEpisodes = [];
    private Dictionary<string, double> _episodeProgress = [];
    private readonly Dictionary<string, CancellationTokenSource> _podcastDownloads = new();

    // Visualizer
    private readonly SpectrumAnalyzer _spectrum = new();
    private DispatcherTimer? _spectrumTimer;
    private int _vizFps = 30;
    private int _vizBandCount;  // tracks current bar count for reuse
    private TextBlock? _vizNoTrackText;
    private VisualizerRenderer? _vizRenderer;

    // Equalizer
    private Slider[] _eqSliders = new Slider[10];
    private TextBlock[] _eqGainLabels = new TextBlock[10];
    private Slider? _eqPreampSlider;
    private TextBlock? _eqPreampLabel;
    private ComboBox? _eqPresetCombo;
    private ToggleSwitch? _eqToggle;
    private bool _eqUiBuilt;
    private bool _eqUpdatingFromPreset;

    // Albums & Artists
    private string? _currentAlbumName;
    private string? _currentArtistName;

    // View transition animation
    private bool _isViewTransitioning;

    // Media control
    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
    private readonly Dictionary<string, MediaSessionPanel> _mediaSessionPanels = new();
    private DispatcherTimer? _mediaTickTimer;

    // Sleep timer
    private DispatcherTimer? _sleepTimer;
    private DateTime _sleepTargetTime;

    // Backdrop controller (always use controller API to keep effect when unfocused)
    private IDisposable? _backdropController;
    private SystemBackdropConfiguration? _configSource;

    // Detached library window
    private LibraryWindow? _libraryWindow;

    // Overlay widget
    private OverlayWidget? _overlayWidget;

    // Folder watcher
    private LibraryWatcher? _libraryWatcher;

    // Nav overflow & tab order (all 10 items)
    private readonly List<int> _overflowedTabIndices = [];
    private int[] _tabOrder = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    private int _dragTabIndex = -1;
    private readonly Button[] _navButtons = new Button[11];
    private readonly TextBlock[] _navTexts = new TextBlock[11];

    private static readonly (string Icon, string LabelKey, ViewMode[] Modes)[] TabDefs =
    [
        ("\uE8F1", "Library",    [ViewMode.Library]),
        ("\uE8FD", "Playlists",  [ViewMode.PlaylistList, ViewMode.PlaylistDetail]),
        ("\uE890", "Queue",      [ViewMode.Queue]),
        ("\uEC05", "Radio",      [ViewMode.Radio]),
        ("\uE774", "Podcasts",   [ViewMode.Podcast, ViewMode.PodcastEpisodes]),
        ("\uE93F", "Albums",     [ViewMode.Albums, ViewMode.AlbumDetail]),
        ("\uE77B", "Artists",    [ViewMode.Artists, ViewMode.ArtistDetail]),
        ("\uE9D9", "Visualizer", [ViewMode.Visualizer]),
        ("\uE9E9", "Equalizer",  [ViewMode.Equalizer]),
        ("\uE93C", "Media",      [ViewMode.MediaControl]),
        ("\uE9D9", "Stats",      [ViewMode.Stats]),
    ];

    // Collapse animation
    private enum CollapseState { Expanded, Compact, Mini }
    private CollapseState _collapseState = CollapseState.Expanded;
    private readonly int _expandedHeight = 710;
    private readonly int _collapsedHeight = 220;
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

        AppWindow.Changed += MainAppWindow_Changed;

        // Restore window position (default: bottom-right)
        RestoreWindowPosition();

        // Load settings
        var settings = SettingsManager.Load();

        // Apply backdrop, theme, accent color, and localization
        ApplyBackdrop(SettingsManager.LoadBackdrop());
        ThemeHelper.ApplyAccentColor(settings.AccentColor);
        ApplyTheme(SettingsManager.LoadTheme());
        ApplyLocalization();

        // Set up audio player
        _player.SetDispatcherQueue(DispatcherQueue);
        _player.MediaOpened += OnMediaOpened;
        _player.MediaEnded += () => OnMediaEnded();
        _player.MediaFailed += OnMediaFailed;
        _player.PositionChanged += OnPositionChanged;
        _player.BufferingChanged += OnBufferingChanged;
        _player.GaplessTransitioned += OnGaplessTransitioned;
        _player.SmtcPreviousRequested += () => Prev_Click(this, new RoutedEventArgs());
        _player.SmtcNextRequested += () => Next_Click(this, new RoutedEventArgs());
        VolumeSlider.Value = settings.Volume * 100;
        _player.Volume = settings.Volume;
        _sortBy = settings.SortBy;
        _sortAscending = settings.SortAscending;
        SortAscending.IsChecked = _sortAscending;
        UpdateSortChecks();

        // Load EQ settings
        _player.EqEnabled = settings.EqEnabled;
        if (settings.EqBands is { Length: 10 })
            _player.SetEqAllBands(settings.EqBands);
        _player.SetEqPreamp(settings.EqPreamp);

        // Load radio stations and podcast subscriptions
        _radioStations = SettingsManager.LoadRadioStations();
        _podcastSubscriptions = PodcastService.LoadSubscriptions();
        _readEpisodes = PodcastService.LoadReadEpisodes();
        _episodeProgress = PodcastService.LoadProgress();

        // Load tab order and rebuild nav
        _tabOrder = SettingsManager.LoadTabOrder();
        RebuildNavTabs();

        // Initialize library and load tracks
        LibraryManager.Initialize();
        LoadTracks();
        UpdateNavigation();

        // Recheck nav overflow on window resize
        NavRow.SizeChanged += (_, _) => UpdateNavOverflow();

        // Start real-time folder watchers
        _libraryWatcher = new LibraryWatcher();
        _libraryWatcher.LibraryChanged += () =>
            DispatcherQueue.TryEnqueue(() => LoadTracks());
        _libraryWatcher.Start();

        // Restore queue state (display info only, not playing yet)
        _queue.LoadState(_allTracks);
        RestoreQueuePreview();

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
            AppWindow.Changed -= MainAppWindow_Changed;
            CloseOverlayWidget();
            CloseLibraryWindow();
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            UnregisterHotKey(_hwnd, HOTKEY_COLLAPSE_ID);
            RemoveTrayIcon();
            RecordCurrentPlay(); // Save play history before exiting
            _queue.SavedPositionSeconds = _player.Position.TotalSeconds;
            _queue.SaveState();
            SavePodcastProgressNow();
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
            _libraryWatcher?.Dispose();
            _spectrumTimer?.Stop();
            _spectrum.Dispose();
            _player.Dispose();
        };
    }

    // -- Library --------------------------------------------------

    private void LoadTracks()
    {
        _allTracks = LibraryManager.GetAllTracks();
        RefreshLibraryWindow();
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

        if (_viewMode == ViewMode.Visualizer || _viewMode == ViewMode.Albums || _viewMode == ViewMode.Artists)
            return;

        List<TrackInfo> source = _viewMode == ViewMode.PlaylistDetail && _currentPlaylist != null
            ? LibraryManager.GetPlaylistTracks(_currentPlaylist.Id)
            : _viewMode == ViewMode.AlbumDetail && _currentAlbumName != null
            ? _allTracks.Where(t => string.Equals(t.Album, _currentAlbumName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList()
            : _viewMode == ViewMode.ArtistDetail && _currentArtistName != null
            ? _allTracks.Where(t => string.Equals(t.Artist, _currentArtistName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.Album).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title).ToList()
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
                "bpm" => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.Bpm == 0 ? int.MaxValue : t.Bpm).ThenBy(t => t.Title)]
                    : [.. _displayedTracks.OrderByDescending(t => t.Bpm).ThenBy(t => t.Title)],
                _ => _sortAscending
                    ? [.. _displayedTracks.OrderBy(t => t.Title)]
                    : [.. _displayedTracks.OrderByDescending(t => t.Title)]
            };
        }

        RebuildTrackList();
        TrackCountText.Text = _viewMode == ViewMode.Library
            ? Strings.T("{0} tracks", _allTracks.Count.ToString("N0"))
            : Strings.T("{0} tracks", _displayedTracks.Count.ToString("N0"));
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

            // BPM + Duration
            var durationText = track.Bpm > 0
                ? $"{track.Bpm} BPM \u00B7 {track.DurationFormatted}"
                : track.DurationFormatted;
            var dur = new TextBlock
            {
                Text = durationText,
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
        // Radio streams show LIVE; podcast streams show normal timeline
        if (_player.IsStream && _currentEpisode == null)
        {
            _isSeeking = true;
            TimelineSlider.Maximum = 1;
            TimelineSlider.Value = 0;
            TimelineSlider.IsEnabled = false;
            DurationText.Text = Strings.T("LIVE");
            PositionText.Text = "";
            _isSeeking = false;
            return;
        }

        TimelineSlider.IsEnabled = true;
        var dur = _player.Duration;
        if (dur.TotalSeconds > 0)
        {
            _isSeeking = true;
            TimelineSlider.Maximum = dur.TotalSeconds;
            TimelineSlider.Value = 0;
            DurationText.Text = TrackDurationHelper.FormatDuration(dur);
            PositionText.Text = "0:00";
            _isSeeking = false;
        }
    }

    private bool _isHandlingMediaEnded;

    private async void OnMediaEnded()
    {
        if (_isHandlingMediaEnded) return;
        _isHandlingMediaEnded = true;
        try
        {
            await HandleMediaEndedAsync();
        }
        finally
        {
            _isHandlingMediaEnded = false;
        }
    }

    private async Task HandleMediaEndedAsync()
    {
        // Auto-mark podcast episode as read when playback ends
        if (_currentEpisode != null)
        {
            _readEpisodes.Add(_currentEpisode.AudioUrl);
            PodcastService.SaveReadEpisodes(_readEpisodes);
            // Clear saved progress — episode is finished
            _episodeProgress.Remove(_currentEpisode.AudioUrl);
            PodcastService.SaveProgress(_episodeProgress);
            _currentEpisode = null;
            RefreshEpisodeList();
        }

        var next = _queue.Next();
        if (next != null)
        {
            try
            {
                await _player.PlayTrackAsync(next);
            }
            catch (Exception ex)
            {
                TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
            }
            UpdateNowPlaying(next);
        }
        else
        {
            RecordCurrentPlay(); // Queue finished — record last track
            PlayPauseIcon.Glyph = "\uE768";
            MiniPlayPauseIcon.Glyph = "\uE768";
        }
    }

    private void OnMediaFailed(string error)
    {
        PlayPauseIcon.Glyph = "\uE768";
        TrackArtist.Text = $"Error: {error}";
    }

    private void OnBufferingChanged(bool isBuffering)
    {
        if (_player.IsStream)
            RadioStatusText.Text = isBuffering ? "Buffering..." : "Playing: " + (RadioUrlBox.Text?.Trim() ?? "");
    }

    private int _progressSaveCounter;

    private void OnPositionChanged(TimeSpan pos)
    {
        if (_isSeeking) return;
        _isSeeking = true;
        TimelineSlider.Value = pos.TotalSeconds;
        PositionText.Text = FormatTime(pos);
        _isSeeking = false;

        // Save podcast episode progress every ~5s (20 ticks × 250ms)
        if (_currentEpisode != null && pos.TotalSeconds > 1 && ++_progressSaveCounter >= 20)
        {
            _progressSaveCounter = 0;
            _episodeProgress[_currentEpisode.AudioUrl] = pos.TotalSeconds;
            PodcastService.SaveProgress(_episodeProgress);
        }

        // Gapless: pre-load next track when ~5s remain
        var remaining = _player.RemainingSeconds;
        if (remaining > 0 && remaining < 5 && _currentEpisode == null)
        {
            var nextTrack = _queue.PeekNext();
            if (nextTrack != null)
                _player.PrepareNextTrack(nextTrack);
        }
    }

    private void OnGaplessTransitioned(TrackInfo track)
    {
        // Advance the queue index to match the gapless transition
        _queue.Next();
        UpdateNowPlaying(track);
    }

    private void SavePodcastProgressNow()
    {
        if (_currentEpisode != null && _player.Position.TotalSeconds > 1)
        {
            _episodeProgress[_currentEpisode.AudioUrl] = _player.Position.TotalSeconds;
            PodcastService.SaveProgress(_episodeProgress);
        }
    }

    private void RecordCurrentPlay()
    {
        if (_playStartTrackId <= 0) return;
        var elapsed = (int)(DateTime.UtcNow - _playStartTime).TotalMilliseconds;
        if (elapsed < 10_000) return; // Ignore plays under 10 seconds
        var trackId = _playStartTrackId;
        _playStartTrackId = 0;
        Task.Run(() =>
        {
            try { LibraryManager.RecordPlay(trackId, elapsed); } catch { }
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_viewMode == ViewMode.Stats)
                    BuildStatsUI();
            });
        });
    }

    private void UpdateNowPlaying(TrackInfo track)
    {
        RecordCurrentPlay(); // Record previous track before switching
        _playStartTrackId = track.Id;
        _playStartTime = DateTime.UtcNow;

        SavePodcastProgressNow();
        _currentEpisode = null; // Clear podcast episode when playing a track
        TrackTitle.Text = track.Title;
        TrackArtist.Text = track.Artist;
        TrackAlbum.Text = track.Album;
        PlayPauseIcon.Glyph = "\uE769"; // Pause icon
        MiniPlayPauseIcon.Glyph = "\uE769";
        LoadAlbumArt(track.Path);
        UpdateMiniPlayer(track);
        UpdateTransportControls();
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
            MiniTrackText.Text = Strings.T("No track");
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
        // If a radio stream is active, just toggle play/pause
        if (_player.IsStream)
        {
            _player.TogglePlayPause();
            PlayPauseIcon.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
            MiniPlayPauseIcon.Glyph = PlayPauseIcon.Glyph;
            return;
        }

        if (_player.CurrentTrack == null)
        {
            // Resume from saved queue state if available
            if (_queue.CurrentTrack != null)
            {
                var track = _queue.CurrentTrack;
                try
                {
                    await _player.PlayTrackAsync(track);
                    // Seek to saved position
                    if (_queue.SavedPositionSeconds > 1)
                    {
                        _player.Seek(TimeSpan.FromSeconds(_queue.SavedPositionSeconds));
                        _queue.SavedPositionSeconds = 0;
                    }
                }
                catch (Exception ex)
                {
                    TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
                }
                UpdateNowPlaying(track);
            }
            else if (_displayedTracks.Count > 0)
            {
                _queue.SetQueue(_displayedTracks, 0);
                var track = _queue.CurrentTrack!;
                try
                {
                    await _player.PlayTrackAsync(track);
                }
                catch (Exception ex)
                {
                    TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
                }
                UpdateNowPlaying(track);
            }
            return;
        }

        _player.TogglePlayPause();
        PlayPauseIcon.Glyph = _player.IsPlaying ? "\uE769" : "\uE768";
        MiniPlayPauseIcon.Glyph = PlayPauseIcon.Glyph;
    }

    private void UpdateTransportControls()
    {
        bool isStream = _player.IsStream;
        ShuffleButton.IsEnabled = !isStream;
        RepeatButton.IsEnabled = !isStream;
        PrevButton.IsEnabled = !isStream;
        NextButton.IsEnabled = !isStream;
        ShuffleButton.Opacity = isStream ? 0.4 : 1;
        RepeatButton.Opacity = isStream ? 0.4 : 1;
        PrevButton.Opacity = isStream ? 0.4 : 1;
        NextButton.Opacity = isStream ? 0.4 : 1;
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
                TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
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
                TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
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

    // -- Playback Speed -------------------------------------------

    private static readonly float[] SpeedPresets = [0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f];

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(minWidth: 120, maxWidth: 160);

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(ActionPanel.CreateSectionHeader(Strings.T("Speed")));
        panel.Children.Add(ActionPanel.CreateSeparator());

        foreach (var speed in SpeedPresets)
        {
            var s = speed;
            var label = s == 1.0f ? Strings.T("Normal") : $"{s:0.##}x";
            var isActive = MathF.Abs(_player.PlaybackSpeed - s) < 0.01f;
            panel.Children.Add(ActionPanel.CreateButton(
                isActive ? "\uE73E" : "\uE8D7", label, [], () =>
                {
                    _player.PlaybackSpeed = s;
                    UpdateSpeedText();
                    flyout.Hide();
                }, isActive: isActive));
        }

        flyout.Content = panel;
        flyout.ShowAt(sender as FrameworkElement ?? SpeedButton);
    }

    private void UpdateSpeedText()
    {
        var speed = _player.PlaybackSpeed;
        SpeedText.Text = MathF.Abs(speed - 1.0f) < 0.01f ? "1x" : $"{speed:0.##}x";
        SpeedText.Foreground = MathF.Abs(speed - 1.0f) < 0.01f
            ? ThemeHelper.Brush("TextFillColorPrimaryBrush")
            : ThemeHelper.Brush("AccentTextFillColorPrimaryBrush");
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
        SortBpm.IsChecked = _sortBy == "bpm";
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
                            catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
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
            catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
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

    private void AlbumBack_Click(object sender, RoutedEventArgs e)
    {
        NavAlbums_Click(sender, e);
    }

    private async void NewPlaylist_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.T("New Playlist"),
            PrimaryButtonText = Strings.T("Create"),
            CloseButtonText = Strings.T("Cancel"),
            XamlRoot = Content.XamlRoot
        };
        var input = new TextBox { PlaceholderText = Strings.T("Playlist name") };
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
        // Toggle tab bar vs detail headers
        var isDetailView = _viewMode == ViewMode.PlaylistDetail || _viewMode == ViewMode.AlbumDetail
            || _viewMode == ViewMode.ArtistDetail;
        NavTabs.Visibility = !isDetailView ? Visibility.Visible : Visibility.Collapsed;
        PlaylistHeader.Visibility = _viewMode == ViewMode.PlaylistDetail
            ? Visibility.Visible : Visibility.Collapsed;
        AlbumHeader.Visibility = _viewMode == ViewMode.AlbumDetail
            ? Visibility.Visible : Visibility.Collapsed;
        ArtistHeader.Visibility = _viewMode == ViewMode.ArtistDetail
            ? Visibility.Visible : Visibility.Collapsed;
        NewPlaylistBtn.Visibility = _viewMode == ViewMode.PlaylistList
            ? Visibility.Visible : Visibility.Collapsed;
        ClearQueueBtn.Visibility = _viewMode == ViewMode.Queue
            ? Visibility.Visible : Visibility.Collapsed;

        // Highlight active tabs
        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (_navTexts[i] == null) continue;
            bool active = TabDefs[i].Modes.Contains(_viewMode);
            _navTexts[i].FontWeight = active
                ? Microsoft.UI.Text.FontWeights.SemiBold
                : Microsoft.UI.Text.FontWeights.Normal;
            _navTexts[i].Foreground = active
                ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                : ThemeHelper.Brush("TextFillColorPrimaryBrush");
        }
        // NavMoreText highlighting is handled in UpdateNavOverflow() to account for overflowed tabs

        // Show/hide search & sort
        SearchSortRow.Visibility = (_viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistDetail
            || _viewMode == ViewMode.AlbumDetail || _viewMode == ViewMode.ArtistDetail)
            ? Visibility.Visible : Visibility.Collapsed;

        // Show/hide content containers based on view mode
        var isPodcast = _viewMode == ViewMode.Podcast || _viewMode == ViewMode.PodcastEpisodes;
        var isTrackView = _viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistList
            || _viewMode == ViewMode.PlaylistDetail || _viewMode == ViewMode.Queue
            || _viewMode == ViewMode.AlbumDetail || _viewMode == ViewMode.ArtistDetail;
        TrackListView.Visibility = isTrackView ? Visibility.Visible : Visibility.Collapsed;
        AlbumsGridView.Visibility = (_viewMode == ViewMode.Albums || _viewMode == ViewMode.Artists)
            ? Visibility.Visible : Visibility.Collapsed;
        WaveformContainer.Visibility = _viewMode == ViewMode.Visualizer
            ? Visibility.Visible : Visibility.Collapsed;
        EqualizerContainer.Visibility = _viewMode == ViewMode.Equalizer
            ? Visibility.Visible : Visibility.Collapsed;
        RadioContainer.Visibility = _viewMode == ViewMode.Radio
            ? Visibility.Visible : Visibility.Collapsed;
        PodcastContainer.Visibility = isPodcast
            ? Visibility.Visible : Visibility.Collapsed;
        MediaContainer.Visibility = _viewMode == ViewMode.MediaControl
            ? Visibility.Visible : Visibility.Collapsed;
        StatsContainer.Visibility = _viewMode == ViewMode.Stats
            ? Visibility.Visible : Visibility.Collapsed;

        // Detail header names
        if (_currentPlaylist != null)
            PlaylistNameText.Text = _currentPlaylist.Name;
        if (_currentAlbumName != null)
            AlbumNameText.Text = _currentAlbumName;
        if (_currentArtistName != null)
            ArtistNameText.Text = _currentArtistName;

        // Recompute which tabs overflow
        if (NavTabs.Visibility == Visibility.Visible)
            UpdateNavOverflow();
    }

    private void UpdateNavOverflow()
    {
        double available = NavRow.ActualWidth - NavRow.Padding.Left - NavRow.Padding.Right;
        if (available <= 0) return;

        // Subtract right-column auto content
        if (NewPlaylistBtn.Visibility == Visibility.Visible)
        {
            NewPlaylistBtn.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            available -= NewPlaylistBtn.DesiredSize.Width;
        }
        if (ClearQueueBtn.Visibility == Visibility.Visible)
        {
            ClearQueueBtn.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            available -= ClearQueueBtn.DesiredSize.Width;
        }

        // Measure the "..." button
        double moreWidth = MeasureNavButtonWidth("...") + NavTabs.Spacing;

        // Measure each tab
        var widths = new double[TabDefs.Length];
        for (int i = 0; i < TabDefs.Length; i++)
            widths[i] = MeasureNavButtonWidth(Strings.T(TabDefs[i].LabelKey)) + NavTabs.Spacing;

        // Determine which tabs fit in display order (always reserving space for "...")
        _overflowedTabIndices.Clear();
        double used = moreWidth;

        foreach (var idx in _tabOrder)
        {
            if (_navButtons[idx] == null) continue;
            if (used + widths[idx] <= available)
            {
                _navButtons[idx].Visibility = Visibility.Visible;
                used += widths[idx];
            }
            else
            {
                _navButtons[idx].Visibility = Visibility.Collapsed;
                _overflowedTabIndices.Add(idx);
            }
        }

        // Highlight "..." if any overflowed tab's view is active
        bool moreActive = false;
        foreach (var idx in _overflowedTabIndices)
        {
            if (TabDefs[idx].Modes.Contains(_viewMode))
            { moreActive = true; break; }
        }

        NavMoreText.FontWeight = moreActive
            ? Microsoft.UI.Text.FontWeights.SemiBold
            : Microsoft.UI.Text.FontWeights.Normal;
        NavMoreText.Foreground = moreActive
            ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
            : ThemeHelper.Brush("TextFillColorPrimaryBrush");
    }

    private static double MeasureNavButtonWidth(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = 12 };
        tb.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return tb.DesiredSize.Width + 18; // Button Padding="8,4" → 16px horizontal + 2px chrome
    }

    private void NavTabClick(int tabId)
    {
        // Route click to the correct navigation handler
        var dummy = new RoutedEventArgs();
        switch (tabId)
        {
            case 0: NavLibrary_Click(this, dummy); break;
            case 1: NavPlaylists_Click(this, dummy); break;
            case 2: NavQueue_Click(this, dummy); break;
            case 3: NavRadio_Click(this, dummy); break;
            case 4: NavPodcast_Click(this, dummy); break;
            case 5: NavAlbums_Click(this, dummy); break;
            case 6: NavArtists_Click(this, dummy); break;
            case 7: NavVisualizer_Click(this, dummy); break;
            case 8: NavEqualizer_Click(this, dummy); break;
            case 9: NavMedia_Click(this, dummy); break;
            case 10: NavStats_Click(this, dummy); break;
        }
    }

    private void RebuildNavTabs()
    {
        // Create (or recreate) buttons for all tab definitions
        for (int i = 0; i < TabDefs.Length; i++)
        {
            var def = TabDefs[i];
            var textBlock = new TextBlock
            {
                Text = Strings.T(def.LabelKey),
                FontSize = 12
            };
            _navTexts[i] = textBlock;

            var btn = new Button
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 4, 8, 4),
                Content = textBlock,
                Tag = i,
                CanDrag = true,
                AllowDrop = true
            };
            btn.DragStarting += NavTab_DragStarting;
            btn.DragOver += NavTab_DragOver;
            btn.Drop += NavTab_Drop;

            var id = i;
            btn.Click += (_, _) => NavTabClick(id);

            _navButtons[i] = btn;
        }

        NavTabs.Children.Clear();
        foreach (var idx in _tabOrder)
            NavTabs.Children.Add(_navButtons[idx]);
        NavTabs.Children.Add(NavMoreBtn); // always last
    }

    private void NavTab_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is Button btn && btn.Tag is int id)
            _dragTabIndex = id;
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void NavTab_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Move;
    }

    private void NavTab_Drop(object sender, DragEventArgs e)
    {
        if (_dragTabIndex < 0) return;

        int targetTabIndex = -1;
        if (sender is Button btn && btn.Tag is int id)
            targetTabIndex = id;

        if (targetTabIndex < 0 || targetTabIndex == _dragTabIndex)
        {
            _dragTabIndex = -1;
            return;
        }

        int fromPos = Array.IndexOf(_tabOrder, _dragTabIndex);
        int toPos = Array.IndexOf(_tabOrder, targetTabIndex);

        var list = _tabOrder.ToList();
        list.RemoveAt(fromPos);
        list.Insert(toPos, _dragTabIndex);
        _tabOrder = [.. list];

        RebuildNavTabs();
        UpdateNavOverflow();
        SettingsManager.SaveTabOrder(_tabOrder);

        _dragTabIndex = -1;
    }

    private void MoveTab(int tabIndex, int direction)
    {
        int pos = Array.IndexOf(_tabOrder, tabIndex);
        int newPos = pos + direction;
        if (newPos < 0 || newPos >= _tabOrder.Length) return;

        (_tabOrder[pos], _tabOrder[newPos]) = (_tabOrder[newPos], _tabOrder[pos]);
        RebuildNavTabs();
        UpdateNavOverflow();
        SettingsManager.SaveTabOrder(_tabOrder);
    }

    private void AnimateViewTransition(Action buildNewContent, bool slideFromRight = true)
    {
        if (_isViewTransitioning) return;
        _isViewTransitioning = true;

        // Target the visible content container
        FrameworkElement target = _viewMode == ViewMode.Visualizer ? WaveformContainer
            : _viewMode == ViewMode.Equalizer ? EqualizerContainer
            : _viewMode == ViewMode.MediaControl ? MediaContainer
            : _viewMode == ViewMode.Stats ? StatsContainer
            : _viewMode == ViewMode.Radio ? RadioContainer
            : (_viewMode == ViewMode.Albums || _viewMode == ViewMode.Artists) ? AlbumsGridView
            : (_viewMode == ViewMode.Podcast || _viewMode == ViewMode.PodcastEpisodes) ? PodcastContainer
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
                : _viewMode == ViewMode.Equalizer ? EqualizerContainer
                : _viewMode == ViewMode.MediaControl ? MediaContainer
                : _viewMode == ViewMode.Stats ? StatsContainer
                : _viewMode == ViewMode.Radio ? RadioContainer
                : (_viewMode == ViewMode.Albums || _viewMode == ViewMode.Artists) ? AlbumsGridView
                : (_viewMode == ViewMode.Podcast || _viewMode == ViewMode.PodcastEpisodes) ? PodcastContainer
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

        TrackCountText.Text = playlists.Count != 1
            ? Strings.T("{0} playlists", playlists.Count)
            : Strings.T("{0} playlist", playlists.Count);
    }

    private StackPanel BuildTrackContextContent(Flyout flyout, TrackInfo track)
    {
        var panel = new StackPanel { Spacing = 0 };

        // Queue actions (not shown in Queue view itself)
        if (_viewMode != ViewMode.Queue)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE768", Strings.T("Play Next"), [], () =>
            {
                flyout.Hide();
                _queue.AddToQueueNext(track);
            }));
            panel.Children.Add(ActionPanel.CreateButton("\uE710", Strings.T("Add to Queue"), [], () =>
            {
                flyout.Hide();
                _queue.AddToQueue(track);
            }));
            panel.Children.Add(ActionPanel.CreateSeparator());
        }

        // Add to Playlist section
        panel.Children.Add(ActionPanel.CreateSectionHeader(Strings.T("Add to Playlist")));
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
        panel.Children.Add(ActionPanel.CreateButton("\uE710", Strings.T("New Playlist..."), [], async () =>
        {
            flyout.Hide();
            var dialog = new ContentDialog
            {
                Title = Strings.T("New Playlist"),
                PrimaryButtonText = Strings.T("Create"),
                CloseButtonText = Strings.T("Cancel"),
                XamlRoot = Content.XamlRoot
            };
            var input = new TextBox { PlaceholderText = Strings.T("Playlist name") };
            dialog.Content = input;
            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && !string.IsNullOrWhiteSpace(input.Text))
            {
                var id = LibraryManager.CreatePlaylist(input.Text.Trim());
                LibraryManager.AddTrackToPlaylist(id, track.Id);
            }
        }));

        // BPM detection
        panel.Children.Add(ActionPanel.CreateSeparator());
        var bpmLabel = track.Bpm > 0 ? Strings.T("{0} BPM", track.Bpm) : Strings.T("Detect BPM");
        panel.Children.Add(ActionPanel.CreateButton("\uE916", bpmLabel, [], async () =>
        {
            flyout.Hide();
            await DetectAndSaveBpmAsync(track);
        }));

        // Edit tags
        panel.Children.Add(ActionPanel.CreateButton("\uE70F", Strings.T("Edit Tags"), [], () =>
        {
            flyout.Content = BuildMetadataEditorContent(flyout, track);
        }));

        // Remove from playlist (playlist detail only)
        if (_viewMode == ViewMode.PlaylistDetail && _currentPlaylist != null)
        {
            var currentPl = _currentPlaylist;
            panel.Children.Add(ActionPanel.CreateSeparator());
            panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Remove from Playlist"), [], () =>
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

        panel.Children.Add(ActionPanel.CreateButton("\uE768", Strings.T("Play"), [], async () =>
        {
            flyout.Hide();
            var tracks = LibraryManager.GetPlaylistTracks(playlist.Id);
            if (tracks.Count > 0)
            {
                _queue.SetQueue(tracks, 0);
                try { await _player.PlayTrackAsync(tracks[0]); }
                catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
                UpdateNowPlaying(tracks[0]);
            }
        }));
        panel.Children.Add(ActionPanel.CreateButton("\uE8AC", Strings.T("Rename"), [], async () =>
        {
            flyout.Hide();
            var dialog = new ContentDialog
            {
                Title = Strings.T("Rename Playlist"),
                PrimaryButtonText = Strings.T("Rename"),
                CloseButtonText = Strings.T("Cancel"),
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
        panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Delete"), [], () =>
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

        panel.Children.Add(ActionPanel.CreateButton("\uE768", Strings.T("Play"), [], async () =>
        {
            flyout.Hide();
            var t = _queue.PlayIndex(index);
            if (t != null)
            {
                try { await _player.PlayTrackAsync(t); }
                catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
                UpdateNowPlaying(t);
            }
        }));

        panel.Children.Add(ActionPanel.CreateSeparator());

        if (index > 0)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE74A", Strings.T("Move Up"), [], () =>
            {
                flyout.Hide();
                _queue.MoveInQueue(index, index - 1);
                BuildQueueView();
            }));
        }
        if (index < _queue.Queue.Count - 1)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE74B", Strings.T("Move Down"), [], () =>
            {
                flyout.Hide();
                _queue.MoveInQueue(index, index + 1);
                BuildQueueView();
            }));
        }

        panel.Children.Add(ActionPanel.CreateSeparator());

        panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Remove"), [], () =>
        {
            flyout.Hide();
            _queue.RemoveFromQueue(index);
            BuildQueueView();
        }, isDestructive: true));

        panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Clear Queue"), [], () =>
        {
            flyout.Hide();
            _queue.Clear();
            BuildQueueView();
        }, isDestructive: true));

        return panel;
    }

    private async Task DetectAndSaveBpmAsync(TrackInfo track)
    {
        var bpm = await Task.Run(() => BpmDetector.Detect(track.Path));
        if (bpm > 0)
        {
            track.Bpm = bpm;
            LibraryManager.UpdateTrackBpm(track.Id, bpm);

            // Write BPM to file tag (best effort)
            try
            {
                await Task.Run(() =>
                {
                    using var tagFile = TagLib.File.Create(track.Path);
                    tagFile.Tag.BeatsPerMinute = (uint)bpm;
                    tagFile.Save();
                });
            }
            catch { }

            // Update in-memory track
            var mem = _allTracks.FirstOrDefault(t => t.Id == track.Id);
            if (mem != null) mem.Bpm = bpm;

            RebuildTrackList();
        }
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
            Text = Strings.T("Edit Tags"),
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
            Content = Strings.T("Change"),
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
            Content = Strings.T("Remove"),
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
            Header = Strings.T("Title"),
            Text = track.Title,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5)
        };
        panel.Children.Add(titleBox);

        var artistBox = new TextBox
        {
            Header = Strings.T("Artist"),
            Text = track.Artist,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5)
        };
        panel.Children.Add(artistBox);

        var albumBox = new TextBox
        {
            Header = Strings.T("Album"),
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
            Content = Strings.T("Save"),
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

    // -- Radio view -----------------------------------------------

    private void NavRadio_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Radio) return;
        _viewMode = ViewMode.Radio;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => UpdateRadioHistoryList());
    }

    private void RadioUrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = PlayRadioStreamAsync();
    }

    private void RadioPlay_Click(object sender, RoutedEventArgs e)
    {
        _ = PlayRadioStreamAsync();
    }

    private void RadioHistory_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Grid grid && grid.Tag is RadioStation station)
        {
            RadioUrlBox.Text = station.Url;
            _ = PlayRadioStreamAsync();
        }
    }

    private async Task PlayRadioStreamAsync()
    {
        var url = RadioUrlBox.Text?.Trim();
        if (string.IsNullOrEmpty(url)) return;

        // Basic URL validation
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            RadioStatusText.Text = Strings.T("Invalid URL. Please enter a valid http/https stream URL.");
            return;
        }

        RadioStatusText.Text = Strings.T("Connecting...");
        RadioPlayBtn.IsEnabled = false;

        try
        {
            _player.Stop();
            _currentEpisode = null;
            _isRadioPlaying = true;
            await _player.PlayStreamAsync(uri);

            RadioStatusText.Text = Strings.T("Playing: {0}", url);
            RadioUrlBox.Text = "";

            // Add to stations (most recent first, no duplicates by URL)
            var existing = _radioStations.FindIndex(s => s.Url == url);
            if (existing >= 0)
            {
                var station = _radioStations[existing];
                _radioStations.RemoveAt(existing);
                _radioStations.Insert(0, station);
            }
            else
            {
                _radioStations.Insert(0, new RadioStation(url, uri.Host));
            }
            if (_radioStations.Count > 50) _radioStations.RemoveAt(50);
            SettingsManager.SaveRadioStations(_radioStations);
            UpdateRadioHistoryList();

            // Update now-playing display
            var displayName = _radioStations[0].Name;
            TrackTitle.Text = displayName;
            TrackArtist.Text = Strings.T("Radio");
            TrackAlbum.Text = "";
            AlbumArtImage.Source = null;
            AlbumArtPlaceholder.Visibility = Visibility.Visible;

            // Update play/pause icons
            PlayPauseIcon.Glyph = "\uE769";
            MiniPlayPauseIcon.Glyph = "\uE769";
            MiniTrackText.Text = Strings.T("Radio") + " — " + displayName;

            // Hide duration/timeline for live stream
            DurationText.Text = Strings.T("LIVE");

            // Start loopback capture for visualizer
            _spectrum.StartLoopback();

            // Disable queue-related transport controls
            UpdateTransportControls();
        }
        catch (Exception ex)
        {
            RadioStatusText.Text = Strings.T("Error: {0}", ex.Message);
            _isRadioPlaying = false;
        }
        finally
        {
            RadioPlayBtn.IsEnabled = true;
        }
    }

    private void UpdateRadioHistoryList()
    {
        RadioHistoryList.Items.Clear();
        foreach (var station in _radioStations)
        {
            var grid = new Grid { Tag = station, Padding = new Thickness(4, 6, 4, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Spacing = 1 };
            info.Children.Add(new TextBlock
            {
                Text = station.Name,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
            info.Children.Add(new TextBlock
            {
                Text = station.Url,
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var playIcon = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 12,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(playIcon, 1);
            grid.Children.Add(playIcon);

            // Context flyout
            var ctxFlyout = new Flyout();
            ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
            var capturedStation = station;
            ctxFlyout.Opening += (_, _) =>
            {
                ctxFlyout.Content = BuildRadioStationContextContent(ctxFlyout, capturedStation);
            };
            grid.ContextFlyout = ctxFlyout;

            RadioHistoryList.Items.Add(grid);
        }
    }

    private StackPanel BuildRadioStationContextContent(Flyout flyout, RadioStation station)
    {
        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(ActionPanel.CreateButton("\uE768", Strings.T("Play"), [], () =>
        {
            flyout.Hide();
            RadioUrlBox.Text = station.Url;
            _ = PlayRadioStreamAsync();
        }));
        panel.Children.Add(ActionPanel.CreateButton("\uE8AC", Strings.T("Rename"), [], () =>
        {
            flyout.Hide();
            ShowRadioRenameFlyout(station);
        }));
        panel.Children.Add(ActionPanel.CreateSeparator());
        panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Delete"), [], () =>
        {
            flyout.Hide();
            _radioStations.RemoveAll(s => s.Url == station.Url);
            SettingsManager.SaveRadioStations(_radioStations);
            UpdateRadioHistoryList();
        }, isDestructive: true));

        return panel;
    }

    private void ShowRadioRenameFlyout(RadioStation station)
    {
        var renameFlyout = new Flyout();
        renameFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(ActionPanel.CreateSectionHeader(Strings.T("Rename Station")));

        var input = new TextBox
        {
            Text = station.Name,
            FontSize = 13,
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(6)
        };

        void DoRename()
        {
            if (!string.IsNullOrWhiteSpace(input.Text))
            {
                var idx = _radioStations.FindIndex(s => s.Url == station.Url);
                if (idx >= 0)
                {
                    _radioStations[idx] = station with { Name = input.Text.Trim() };
                    SettingsManager.SaveRadioStations(_radioStations);
                    UpdateRadioHistoryList();
                }
            }
            renameFlyout.Hide();
        }

        var confirmBtn = ActionPanel.CreateButton("\uE73E", Strings.T("Confirm"), [], DoRename);

        input.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                DoRename();
            }
        };

        panel.Children.Add(input);
        panel.Children.Add(confirmBtn);
        renameFlyout.Content = panel;

        // Select all text on open
        renameFlyout.Opened += (_, _) =>
        {
            input.Focus(FocusState.Programmatic);
            input.SelectAll();
        };

        renameFlyout.ShowAt(RadioContainer);
    }

    // -- Podcast view -----------------------------------------------

    private void NavPodcast_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Podcast) return;
        _viewMode = ViewMode.Podcast;
        _currentPlaylist = null;
        _currentPodcast = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => BuildPodcastSubscriptionList());
    }

    private void PodcastBack_Click(object sender, RoutedEventArgs e)
    {
        _viewMode = ViewMode.Podcast;
        _currentPodcast = null;
        PodcastBackBtn.Visibility = Visibility.Collapsed;
        PodcastSearchBox.PlaceholderText = "Search podcasts...";
        PodcastSearchBox.Text = "";
        BuildPodcastSubscriptionList();
    }

    private void PodcastSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            _ = SearchPodcastsAsync();
    }

    private void PodcastSearch_Click(object sender, RoutedEventArgs e)
    {
        _ = SearchPodcastsAsync();
    }

    private async Task SearchPodcastsAsync()
    {
        var query = PodcastSearchBox.Text?.Trim();
        if (string.IsNullOrEmpty(query)) return;

        PodcastSearchBtn.IsEnabled = false;
        PodcastListView.Items.Clear();
        PodcastListView.Items.Add(new TextBlock
        {
            Text = Strings.T("Searching..."),
            FontSize = 13,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            Margin = new Thickness(8, 12, 8, 0)
        });

        try
        {
            var results = await PodcastService.SearchAsync(query);
            PodcastListView.Items.Clear();

            if (results.Count == 0)
            {
                PodcastListView.Items.Add(new TextBlock
                {
                    Text = Strings.T("No podcasts found."),
                    FontSize = 13,
                    Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                    Margin = new Thickness(8, 12, 8, 0)
                });
                return;
            }

            foreach (var podcast in results)
                PodcastListView.Items.Add(BuildPodcastItem(podcast, isSearchResult: true));
        }
        catch (Exception ex)
        {
            PodcastListView.Items.Clear();
            PodcastListView.Items.Add(new TextBlock
            {
                Text = Strings.T("Error: {0}", ex.Message),
                FontSize = 13,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                Margin = new Thickness(8, 12, 8, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
        finally
        {
            PodcastSearchBtn.IsEnabled = true;
        }
    }

    private async Task LoadUnreadBadgeAsync(PodcastInfo podcast, Border badge)
    {
        try
        {
            var episodes = await PodcastService.FetchEpisodesAsync(podcast.FeedUrl, limit: 500);
            int unread = episodes.Count(ep => !_readEpisodes.Contains(ep.AudioUrl));
            if (unread > 0)
            {
                ((TextBlock)badge.Child).Text = unread.ToString();
                badge.Visibility = Visibility.Visible;
            }
        }
        catch { /* network error — no badge */ }
    }

    private void BuildPodcastSubscriptionList()
    {
        PodcastBackBtn.Visibility = Visibility.Collapsed;
        PodcastSearchBox.PlaceholderText = "Search podcasts...";
        PodcastListView.Items.Clear();

        if (_podcastSubscriptions.Count == 0)
        {
            PodcastListView.Items.Add(new TextBlock
            {
                Text = "No subscriptions yet. Search for podcasts to subscribe.",
                FontSize = 13,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                Margin = new Thickness(8, 12, 8, 0),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var podcast in _podcastSubscriptions)
            PodcastListView.Items.Add(BuildPodcastItem(podcast, isSearchResult: false));
    }

    private Grid BuildPodcastItem(PodcastInfo podcast, bool isSearchResult)
    {
        var grid = new Grid { Tag = podcast, Padding = new Thickness(4, 6, 4, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Artwork
        var artGrid = new Grid
        {
            Width = 44, Height = 44,
            CornerRadius = new CornerRadius(6),
            Background = ThemeHelper.Brush("CardBackgroundFillColorSecondaryBrush")
        };
        if (!string.IsNullOrEmpty(podcast.ArtworkUrl))
        {
            var img = new Image
            {
                Width = 44, Height = 44,
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            };
            img.Source = new BitmapImage(new Uri(podcast.ArtworkUrl));
            artGrid.Children.Add(img);
        }
        else
        {
            artGrid.Children.Add(new FontIcon
            {
                Glyph = "\uE8D6", FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
            });
        }
        Grid.SetColumn(artGrid, 0);
        grid.Children.Add(artGrid);

        // Info
        var info = new StackPanel { Spacing = 1, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = podcast.Name, FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
        });
        info.Children.Add(new TextBlock
        {
            Text = podcast.Author, FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
        });
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        // Right column: badge or subscribe icon
        bool isSubscribed = _podcastSubscriptions.Any(p => p.FeedUrl == podcast.FeedUrl);
        if (isSearchResult)
        {
            var subIcon = new FontIcon
            {
                Glyph = isSubscribed ? "\uE73E" : "\uE710",
                FontSize = 12,
                Foreground = isSubscribed
                    ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
                    : ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(subIcon, 2);
            grid.Children.Add(subIcon);
        }
        else
        {
            // Unread badge — loaded async
            var badge = new Border
            {
                CornerRadius = new CornerRadius(9),
                MinWidth = 18, Height = 18,
                Padding = new Thickness(5, 0, 5, 0),
                Background = ThemeHelper.Brush("AccentFillColorDefaultBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);

            _ = LoadUnreadBadgeAsync(podcast, badge);
        }

        // Context flyout
        var ctxFlyout = new Flyout();
        ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
        var captured = podcast;
        ctxFlyout.Opening += (_, _) =>
        {
            ctxFlyout.Content = BuildPodcastContextContent(ctxFlyout, captured);
        };
        grid.ContextFlyout = ctxFlyout;

        return grid;
    }

    private StackPanel BuildPodcastContextContent(Flyout flyout, PodcastInfo podcast)
    {
        var panel = new StackPanel { Spacing = 0 };
        bool isSubscribed = _podcastSubscriptions.Any(p => p.FeedUrl == podcast.FeedUrl);

        panel.Children.Add(ActionPanel.CreateButton("\uE8D6", Strings.T("Episodes"), [], () =>
        {
            flyout.Hide();
            _ = ShowPodcastEpisodesAsync(podcast);
        }));

        if (isSubscribed)
        {
            panel.Children.Add(ActionPanel.CreateSeparator());
            panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Unsubscribe"), [], () =>
            {
                flyout.Hide();
                _podcastSubscriptions.RemoveAll(p => p.FeedUrl == podcast.FeedUrl);
                PodcastService.SaveSubscriptions(_podcastSubscriptions);
                if (_viewMode == ViewMode.Podcast)
                    BuildPodcastSubscriptionList();
            }, isDestructive: true));
        }
        else
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE710", Strings.T("Subscribe"), [], () =>
            {
                flyout.Hide();
                _podcastSubscriptions.Insert(0, podcast);
                PodcastService.SaveSubscriptions(_podcastSubscriptions);
                if (_viewMode == ViewMode.Podcast)
                    BuildPodcastSubscriptionList();
            }));
        }

        return panel;
    }

    private void PodcastList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Grid grid && grid.Tag is PodcastInfo podcast)
        {
            _ = ShowPodcastEpisodesAsync(podcast);
        }
        else if (e.ClickedItem is Grid epGrid && epGrid.Tag is PodcastEpisode episode)
        {
            _ = PlayPodcastEpisodeAsync(episode);
        }
    }

    private async Task ShowPodcastEpisodesAsync(PodcastInfo podcast)
    {
        _viewMode = ViewMode.PodcastEpisodes;
        _currentPodcast = podcast;
        UpdateNavigation();

        PodcastBackBtn.Visibility = Visibility.Visible;
        PodcastSearchBox.PlaceholderText = podcast.Name;
        PodcastSearchBox.Text = "";

        PodcastListView.Items.Clear();
        PodcastListView.Items.Add(new TextBlock
        {
            Text = Strings.T("Loading episodes..."),
            FontSize = 13,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            Margin = new Thickness(8, 12, 8, 0)
        });

        try
        {
            var episodes = await PodcastService.FetchEpisodesAsync(podcast.FeedUrl, limit: 200);
            PodcastListView.Items.Clear();

            // Subscribe button at the top
            bool isSubscribed = _podcastSubscriptions.Any(p => p.FeedUrl == podcast.FeedUrl);
            var subBtn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(8, 4, 8, 8),
                CornerRadius = new CornerRadius(6),
                Background = isSubscribed
                    ? ThemeHelper.Brush("ControlFillColorDefaultBrush")
                    : ThemeHelper.Brush("AccentFillColorDefaultBrush")
            };
            var subText = new TextBlock
            {
                Text = isSubscribed ? Strings.T("Subscribed") : Strings.T("Subscribe"),
                FontSize = 12,
                Foreground = isSubscribed
                    ? ThemeHelper.Brush("TextFillColorPrimaryBrush")
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
            };
            subBtn.Content = subText;
            var capturedPodcast = podcast;
            subBtn.Click += (_, _) =>
            {
                if (_podcastSubscriptions.Any(p => p.FeedUrl == capturedPodcast.FeedUrl))
                {
                    _podcastSubscriptions.RemoveAll(p => p.FeedUrl == capturedPodcast.FeedUrl);
                    subText.Text = Strings.T("Subscribe");
                    subBtn.Background = ThemeHelper.Brush("AccentFillColorDefaultBrush");
                    subText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                }
                else
                {
                    _podcastSubscriptions.Insert(0, capturedPodcast);
                    subText.Text = Strings.T("Subscribed");
                    subBtn.Background = ThemeHelper.Brush("ControlFillColorDefaultBrush");
                    subText.Foreground = ThemeHelper.Brush("TextFillColorPrimaryBrush");
                }
                PodcastService.SaveSubscriptions(_podcastSubscriptions);
            };
            PodcastListView.Items.Add(subBtn);

            if (episodes.Count == 0)
            {
                PodcastListView.Items.Add(new TextBlock
                {
                    Text = Strings.T("No episodes found."),
                    FontSize = 13,
                    Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                    Margin = new Thickness(8, 8, 8, 0)
                });
                return;
            }

            foreach (var ep in episodes)
                PodcastListView.Items.Add(BuildEpisodeItem(ep));
        }
        catch (Exception ex)
        {
            PodcastListView.Items.Clear();
            PodcastListView.Items.Add(new TextBlock
            {
                Text = Strings.T("Error loading episodes: {0}", ex.Message),
                FontSize = 13,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                Margin = new Thickness(8, 12, 8, 0),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private Grid BuildEpisodeItem(PodcastEpisode episode)
    {
        bool isRead = _readEpisodes.Contains(episode.AudioUrl);
        bool isDownloaded = PodcastService.IsDownloaded(episode.AudioUrl);
        bool isDownloading = _podcastDownloads.ContainsKey(episode.AudioUrl);
        bool hasProgress = _episodeProgress.TryGetValue(episode.AudioUrl, out var progressSec) && progressSec > 1;

        var grid = new Grid { Tag = episode, Padding = new Thickness(4, 6, 4, 6) };
        if (isRead) grid.Opacity = 0.5;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleBrush = isRead
            ? ThemeHelper.Brush("TextFillColorTertiaryBrush")
            : ThemeHelper.Brush("TextFillColorPrimaryBrush");

        var info = new StackPanel { Spacing = 2 };
        info.Children.Add(new TextBlock
        {
            Text = episode.Title, FontSize = 13,
            Foreground = titleBrush,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2,
            TextWrapping = TextWrapping.Wrap
        });

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (!string.IsNullOrEmpty(episode.Published))
            meta.Children.Add(new TextBlock
            {
                Text = episode.Published, FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
            });
        if (!string.IsNullOrEmpty(episode.Duration))
            meta.Children.Add(new TextBlock
            {
                Text = episode.Duration, FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
            });
        if (isDownloaded)
        {
            meta.Children.Add(new FontIcon
            {
                Glyph = "\uE896", FontSize = 10,
                Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else if (isDownloading)
            meta.Children.Add(new TextBlock
            {
                Text = Strings.T("Downloading..."), FontSize = 11,
                Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush"),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        if (hasProgress && !isRead)
            meta.Children.Add(new TextBlock
            {
                Text = FormatTime(TimeSpan.FromSeconds(progressSec)),
                FontSize = 11,
                Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
            });
        if (isRead)
            meta.Children.Add(new TextBlock
            {
                Text = Strings.T("Played"), FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                FontStyle = Windows.UI.Text.FontStyle.Italic
            });
        info.Children.Add(meta);

        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var playIcon = new FontIcon
        {
            Glyph = isRead ? "\uE73E" : "\uE768", FontSize = 14,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(playIcon, 1);
        grid.Children.Add(playIcon);

        // Context flyout
        var ctxFlyout = new Flyout();
        ctxFlyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle();
        var capturedEp = episode;
        ctxFlyout.Opening += (_, _) =>
        {
            ctxFlyout.Content = BuildEpisodeContextContent(ctxFlyout, capturedEp);
        };
        grid.ContextFlyout = ctxFlyout;

        return grid;
    }

    private StackPanel BuildEpisodeContextContent(Flyout flyout, PodcastEpisode episode)
    {
        var panel = new StackPanel { Spacing = 0 };
        bool isRead = _readEpisodes.Contains(episode.AudioUrl);
        bool isDownloaded = PodcastService.IsDownloaded(episode.AudioUrl);
        bool isDownloading = _podcastDownloads.ContainsKey(episode.AudioUrl);

        panel.Children.Add(ActionPanel.CreateButton("\uE768", Strings.T("Play"), [], () =>
        {
            flyout.Hide();
            _ = PlayPodcastEpisodeAsync(episode);
        }));

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Download / Cancel / Delete download
        if (isDownloading)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE711", Strings.T("Cancel Download"), [], () =>
            {
                flyout.Hide();
                CancelPodcastDownload(episode.AudioUrl);
            }, isDestructive: true));
        }
        else if (isDownloaded)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Delete Download"), [], () =>
            {
                flyout.Hide();
                PodcastService.DeleteDownload(episode.AudioUrl);
                RefreshEpisodeList();
            }, isDestructive: true));
        }
        else
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE896", Strings.T("Download"), [], () =>
            {
                flyout.Hide();
                _ = DownloadPodcastEpisodeAsync(episode);
            }));
        }

        panel.Children.Add(ActionPanel.CreateSeparator());

        if (isRead)
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE7BA", Strings.T("Mark as unread"), [], () =>
            {
                flyout.Hide();
                _readEpisodes.Remove(episode.AudioUrl);
                PodcastService.SaveReadEpisodes(_readEpisodes);
                RefreshEpisodeList();
            }));
        }
        else
        {
            panel.Children.Add(ActionPanel.CreateButton("\uE73E", Strings.T("Mark as read"), [], () =>
            {
                flyout.Hide();
                _readEpisodes.Add(episode.AudioUrl);
                PodcastService.SaveReadEpisodes(_readEpisodes);
                RefreshEpisodeList();
            }));
        }

        return panel;
    }

    private async Task DownloadPodcastEpisodeAsync(PodcastEpisode episode)
    {
        if (_podcastDownloads.ContainsKey(episode.AudioUrl)) return;

        var cts = new CancellationTokenSource();
        _podcastDownloads[episode.AudioUrl] = cts;
        RefreshEpisodeList();

        try
        {
            await PodcastService.DownloadEpisodeAsync(episode.AudioUrl, ct: cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // Clean up partial file on error
            PodcastService.DeleteDownload(episode.AudioUrl);
            TrackArtist.Text = Strings.T("Download failed: {0}", ex.Message);
        }
        finally
        {
            _podcastDownloads.Remove(episode.AudioUrl);
            RefreshEpisodeList();
        }
    }

    private void CancelPodcastDownload(string audioUrl)
    {
        if (_podcastDownloads.TryGetValue(audioUrl, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            _podcastDownloads.Remove(audioUrl);
            PodcastService.DeleteDownload(audioUrl);
            RefreshEpisodeList();
        }
    }

    private void RefreshEpisodeList()
    {
        if (_viewMode == ViewMode.PodcastEpisodes && _currentPodcast != null)
            _ = ShowPodcastEpisodesAsync(_currentPodcast);
    }

    private async Task PlayPodcastEpisodeAsync(PodcastEpisode episode)
    {
        try
        {
            SavePodcastProgressNow();
            _player.Stop();
            _currentEpisode = episode;
            _progressSaveCounter = 0;

            // Play from local file if downloaded, otherwise stream
            if (PodcastService.IsDownloaded(episode.AudioUrl))
            {
                var localPath = PodcastService.GetDownloadPath(episode.AudioUrl);
                var track = new TrackInfo
                {
                    Id = -1,
                    Path = localPath,
                    Title = episode.Title,
                    Artist = _currentPodcast?.Name ?? "Podcast",
                    Album = episode.Published,
                    DurationMs = 0
                };
                await _player.PlayTrackAsync(track);
            }
            else
            {
                if (!Uri.TryCreate(episode.AudioUrl, UriKind.Absolute, out var uri)) return;
                await _player.PlayStreamAsync(uri);
            }

            // Resume from saved progress
            if (_episodeProgress.TryGetValue(episode.AudioUrl, out var savedPos) && savedPos > 1)
            {
                _player.Seek(TimeSpan.FromSeconds(savedPos));
            }

            // Update now-playing display
            TrackTitle.Text = episode.Title;
            TrackArtist.Text = _currentPodcast?.Name ?? "Podcast";
            TrackAlbum.Text = episode.Published;
            AlbumArtImage.Source = _currentPodcast != null && !string.IsNullOrEmpty(_currentPodcast.ArtworkUrl)
                ? new BitmapImage(new Uri(_currentPodcast.ArtworkUrl)) : null;
            AlbumArtPlaceholder.Visibility = AlbumArtImage.Source == null ? Visibility.Visible : Visibility.Collapsed;

            PlayPauseIcon.Glyph = "\uE769";
            MiniPlayPauseIcon.Glyph = "\uE769";
            MiniTrackText.Text = episode.Title;
            UpdateTransportControls();
        }
        catch (Exception ex)
        {
            TrackArtist.Text = Strings.T("Error: {0}", ex.Message);
        }
    }

    // -- More menu (Visualizer + Media) ----------------------------

    private bool _moreFlyoutKeepOpen;

    private void NavMore_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(minWidth: 200, maxWidth: 260);

        flyout.Closing += (_, args) =>
        {
            if (_moreFlyoutKeepOpen)
            {
                args.Cancel = true;
                _moreFlyoutKeepOpen = false;
            }
        };

        BuildMoreFlyoutContent(flyout, sender, e);

        flyout.ShowAt(sender as FrameworkElement ?? NavMoreBtn);
    }

    private void BuildMoreFlyoutContent(Flyout flyout, object sender, RoutedEventArgs e)
    {
        var panel = new StackPanel { Spacing = 0 };

        for (int pos = 0; pos < _tabOrder.Length; pos++)
        {
            var idx = _tabOrder[pos];
            var def = TabDefs[idx];
            var currentPos = pos;
            var capturedIdx = idx;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool isActive = def.Modes.Contains(_viewMode);
            var navBtn = ActionPanel.CreateButton(def.Icon, Strings.T(def.LabelKey), [],
                () => { flyout.Hide(); NavTabClick(capturedIdx); }, isActive: isActive);
            Grid.SetColumn(navBtn, 0);
            row.Children.Add(navBtn);

            // Move buttons
            var movePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 0,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };

            var upBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE70E", FontSize = 10 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                MinHeight = 0, MinWidth = 0,
                CornerRadius = new CornerRadius(3),
                Opacity = currentPos > 0 ? 1.0 : 0.25,
                IsEnabled = currentPos > 0
            };
            upBtn.Click += (_, _) =>
            {
                _moreFlyoutKeepOpen = true;
                MoveTab(capturedIdx, -1);
                BuildMoreFlyoutContent(flyout, sender, e);
            };
            movePanel.Children.Add(upBtn);

            var downBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE70D", FontSize = 10 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(4, 2, 4, 2),
                MinHeight = 0, MinWidth = 0,
                CornerRadius = new CornerRadius(3),
                Opacity = currentPos < _tabOrder.Length - 1 ? 1.0 : 0.25,
                IsEnabled = currentPos < _tabOrder.Length - 1
            };
            downBtn.Click += (_, _) =>
            {
                _moreFlyoutKeepOpen = true;
                MoveTab(capturedIdx, 1);
                BuildMoreFlyoutContent(flyout, sender, e);
            };
            movePanel.Children.Add(downBtn);

            Grid.SetColumn(movePanel, 1);
            row.Children.Add(movePanel);

            panel.Children.Add(row);
        }

        flyout.Content = panel;
    }

    // -- Albums ---------------------------------------------------

    private void NavAlbums_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Albums) return;
        _viewMode = ViewMode.Albums;
        _currentPlaylist = null;
        _currentAlbumName = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => BuildAlbumsGrid());
    }

    private void BuildAlbumsGrid()
    {
        AlbumsGridView.Items.Clear();

        var albumGroups = _allTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Album))
            .GroupBy(t => t.Album, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Album: g.Key,
                Artist: g.GroupBy(t => t.Artist).OrderByDescending(ag => ag.Count()).First().Key,
                TrackCount: g.Count(),
                SampleTrackPath: g.First().Path
            ))
            .OrderBy(a => a.Album)
            .ToList();

        TrackCountText.Text = Strings.T("{0} albums", albumGroups.Count.ToString("N0"));

        foreach (var album in albumGroups)
        {
            var card = new StackPanel
            {
                Width = 150,
                Spacing = 4,
                Padding = new Thickness(4),
                Tag = album.Album
            };

            // Album art container
            var artGrid = new Grid
            {
                Width = 142,
                Height = 142,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"]
            };

            var artImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Width = 142,
                Height = 142,
                Visibility = Visibility.Collapsed
            };

            var placeholder = new FontIcon
            {
                Glyph = "\uE93C",
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
            };

            artGrid.Children.Add(placeholder);
            artGrid.Children.Add(artImage);
            card.Children.Add(artGrid);

            // Album title
            card.Children.Add(new TextBlock
            {
                Text = album.Album,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Margin = new Thickness(2, 2, 0, 0)
            });

            // Artist + track count
            var artistText = string.IsNullOrWhiteSpace(album.Artist) ? "" : album.Artist;
            card.Children.Add(new TextBlock
            {
                Text = $"{artistText} \u00B7 {album.TrackCount} tracks",
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                Margin = new Thickness(2, 0, 0, 0)
            });

            AlbumsGridView.Items.Add(card);

            // Load artwork async
            var capturedImage = artImage;
            var capturedPlaceholder = placeholder;
            var capturedPath = album.SampleTrackPath;
            _ = LoadAlbumCardArtAsync(capturedImage, capturedPlaceholder, capturedPath);
        }

        if (albumGroups.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No albums found. Add music folders in Settings.",
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                FontSize = 13,
                Margin = new Thickness(8)
            };
            AlbumsGridView.Items.Add(empty);
        }
    }

    private async Task LoadAlbumCardArtAsync(Image artImage, FontIcon placeholder, string trackPath)
    {
        try
        {
            byte[]? artData = null;
            string? coverPath = null;

            await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(trackPath);
                    if (tagFile.Tag.Pictures.Length > 0)
                        artData = tagFile.Tag.Pictures[0].Data.Data;
                }
                catch { }

                if (artData == null)
                {
                    var folder = System.IO.Path.GetDirectoryName(trackPath);
                    if (folder != null)
                        coverPath = FindCoverFile(folder);
                }
            });

            if (artData != null)
            {
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(artData);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new BitmapImage { DecodePixelWidth = 142 };
                bitmap.SetSource(stream);
                artImage.Source = bitmap;
                artImage.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            }
            else if (coverPath != null)
            {
                var bitmap = new BitmapImage { DecodePixelWidth = 142, UriSource = new Uri(coverPath) };
                artImage.Source = bitmap;
                artImage.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            }
        }
        catch { }
    }

    private void AlbumGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StackPanel card || card.Tag is not string name) return;

        if (_viewMode == ViewMode.Artists)
        {
            _currentArtistName = name;
            _viewMode = ViewMode.ArtistDetail;
            _currentPlaylist = null;
            UpdateNavigation();
            AnimateViewTransition(() => ApplyFilterAndSort());
        }
        else
        {
            _currentAlbumName = name;
            _viewMode = ViewMode.AlbumDetail;
            _currentPlaylist = null;
            UpdateNavigation();
            AnimateViewTransition(() => ApplyFilterAndSort());
        }
    }

    // -- Artists ---------------------------------------------------

    private void NavArtists_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Artists) return;
        _viewMode = ViewMode.Artists;
        _currentPlaylist = null;
        _currentArtistName = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => BuildArtistsGrid());
    }

    private void BuildArtistsGrid()
    {
        AlbumsGridView.Items.Clear();

        var artistGroups = _allTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => t.Artist, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Artist: g.Key,
                AlbumCount: g.Select(t => t.Album).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TrackCount: g.Count(),
                SampleTrackPath: g.First().Path
            ))
            .OrderBy(a => a.Artist)
            .ToList();

        TrackCountText.Text = Strings.T("{0} artists", artistGroups.Count.ToString("N0"));

        foreach (var artist in artistGroups)
        {
            var card = new StackPanel
            {
                Width = 150,
                Spacing = 4,
                Padding = new Thickness(4),
                Tag = artist.Artist
            };

            // Artist art container (circular)
            var artGrid = new Grid
            {
                Width = 142,
                Height = 142,
                CornerRadius = new CornerRadius(71),
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"]
            };

            var artImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                Width = 142,
                Height = 142,
                Visibility = Visibility.Collapsed
            };

            // Clip to circle
            var clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, 142, 142)
            };
            var ellipseClip = new Microsoft.UI.Xaml.Media.EllipseGeometry
            {
                Center = new Windows.Foundation.Point(71, 71),
                RadiusX = 71,
                RadiusY = 71
            };

            var placeholder = new FontIcon
            {
                Glyph = "\uE77B",
                FontSize = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush")
            };

            artGrid.Children.Add(placeholder);
            artGrid.Children.Add(artImage);
            card.Children.Add(artGrid);

            // Artist name
            card.Children.Add(new TextBlock
            {
                Text = artist.Artist,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 2, 0, 0)
            });

            // Album + track count
            var albumText = artist.AlbumCount == 1 ? "1 album" : $"{artist.AlbumCount} albums";
            card.Children.Add(new TextBlock
            {
                Text = $"{albumText} \u00B7 {artist.TrackCount} tracks",
                FontSize = 11,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 1,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            });

            AlbumsGridView.Items.Add(card);

            // Load artwork async (reuse album art loader)
            var capturedImage = artImage;
            var capturedPlaceholder = placeholder;
            var capturedPath = artist.SampleTrackPath;
            _ = LoadAlbumCardArtAsync(capturedImage, capturedPlaceholder, capturedPath);
        }

        if (artistGroups.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No artists found. Add music folders in Settings.",
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                FontSize = 13,
                Margin = new Thickness(8)
            };
            AlbumsGridView.Items.Add(empty);
        }
    }

    private void ArtistBack_Click(object sender, RoutedEventArgs e)
    {
        NavArtists_Click(sender, e);
    }

    // -- Equalizer ------------------------------------------------

    private void NavEqualizer_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Equalizer) return;
        _viewMode = ViewMode.Equalizer;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(() => BuildEqualizerUI());
    }

    private void BuildEqualizerUI()
    {
        if (_eqUiBuilt)
        {
            // Sync UI with current player state
            SyncEqUiToPlayer();
            return;
        }
        _eqUiBuilt = true;

        EqualizerPanel.Children.Clear();

        // Header row: title + toggle
        var headerGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = "Equalizer",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 0);

        _eqToggle = new ToggleSwitch
        {
            IsOn = _player.EqEnabled,
            OnContent = "On",
            OffContent = "Off",
            MinWidth = 0
        };
        _eqToggle.Toggled += EqToggle_Toggled;
        Grid.SetColumn(_eqToggle, 1);

        headerGrid.Children.Add(titleText);
        headerGrid.Children.Add(_eqToggle);
        EqualizerPanel.Children.Add(headerGrid);

        // Preset selector
        var settings = SettingsManager.Load();
        _eqPresetCombo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 13
        };
        foreach (var preset in Equalizer.Presets.Keys)
            _eqPresetCombo.Items.Add(preset);
        _eqPresetCombo.SelectedItem = settings.EqPreset;
        if (_eqPresetCombo.SelectedItem == null) _eqPresetCombo.SelectedIndex = 0;
        _eqPresetCombo.SelectionChanged += EqPreset_Changed;
        EqualizerPanel.Children.Add(_eqPresetCombo);

        // EQ bands grid
        var bandsGrid = new Grid { MinHeight = 200 };
        // dB labels column + 10 band columns
        bandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < 10; i++)
            bandsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        bandsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // sliders
        bandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // gain values
        bandsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // freq labels

        // dB labels on the left
        var dbPanel = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        dbPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dbPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dbPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var dbTop = new TextBlock { Text = "+12", FontSize = 9,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"), VerticalAlignment = VerticalAlignment.Top };
        var dbBottom = new TextBlock { Text = "-12", FontSize = 9,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"), VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(dbTop, 0);
        Grid.SetRow(dbBottom, 2);
        dbPanel.Children.Add(dbTop);
        dbPanel.Children.Add(dbBottom);
        Grid.SetColumn(dbPanel, 0);
        Grid.SetRow(dbPanel, 0);
        bandsGrid.Children.Add(dbPanel);

        // Load current gains
        var gains = _player.GetEqGains();

        for (int i = 0; i < 10; i++)
        {
            int bandIndex = i;

            // Vertical slider
            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                StepFrequency = 0.5,
                Value = gains[i],
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = 28
            };
            slider.ValueChanged += (s, args) => EqBand_Changed(bandIndex, args.NewValue);
            Grid.SetColumn(slider, i + 1);
            Grid.SetRow(slider, 0);
            bandsGrid.Children.Add(slider);
            _eqSliders[i] = slider;

            // Gain label
            var gainLabel = new TextBlock
            {
                Text = $"{gains[i]:0.#}",
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
            };
            Grid.SetColumn(gainLabel, i + 1);
            Grid.SetRow(gainLabel, 1);
            bandsGrid.Children.Add(gainLabel);
            _eqGainLabels[i] = gainLabel;

            // Frequency label
            var freqLabel = new TextBlock
            {
                Text = Equalizer.FrequencyLabels[i],
                FontSize = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
                Margin = new Thickness(0, 2, 0, 0)
            };
            Grid.SetColumn(freqLabel, i + 1);
            Grid.SetRow(freqLabel, 2);
            bandsGrid.Children.Add(freqLabel);
        }

        EqualizerPanel.Children.Add(bandsGrid);

        // Preamp row
        var preampGrid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        preampGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        preampGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        preampGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var preampLabel = new TextBlock
        {
            Text = Strings.T("Preamp"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(preampLabel, 0);

        _eqPreampSlider = new Slider
        {
            Minimum = -12,
            Maximum = 12,
            StepFrequency = 0.5,
            Value = _player.EqPreampDb,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center
        };
        _eqPreampSlider.ValueChanged += EqPreamp_Changed;
        Grid.SetColumn(_eqPreampSlider, 1);

        _eqPreampLabel = new TextBlock
        {
            Text = $"{_player.EqPreampDb:0.#} dB",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 40
        };
        Grid.SetColumn(_eqPreampLabel, 2);

        preampGrid.Children.Add(preampLabel);
        preampGrid.Children.Add(_eqPreampSlider);
        preampGrid.Children.Add(_eqPreampLabel);
        EqualizerPanel.Children.Add(preampGrid);

        // Reset button
        var resetBtn = new Button
        {
            Content = Strings.T("Reset"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 0, 12)
        };
        resetBtn.Click += EqReset_Click;
        EqualizerPanel.Children.Add(resetBtn);
    }

    private void SyncEqUiToPlayer()
    {
        if (_eqToggle != null) _eqToggle.IsOn = _player.EqEnabled;
        var gains = _player.GetEqGains();
        _eqUpdatingFromPreset = true;
        for (int i = 0; i < 10; i++)
        {
            if (_eqSliders[i] != null)
            {
                _eqSliders[i].Value = gains[i];
                _eqGainLabels[i].Text = $"{gains[i]:0.#}";
            }
        }
        if (_eqPreampSlider != null) _eqPreampSlider.Value = _player.EqPreampDb;
        if (_eqPreampLabel != null) _eqPreampLabel.Text = $"{_player.EqPreampDb:0.#} dB";
        _eqUpdatingFromPreset = false;
    }

    private void EqToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _player.EqEnabled = _eqToggle!.IsOn;
        SaveEqSettings();
    }

    private void EqPreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_eqPresetCombo?.SelectedItem is not string presetName) return;
        if (!Equalizer.Presets.TryGetValue(presetName, out var gains)) return;

        _eqUpdatingFromPreset = true;
        _player.SetEqAllBands(gains);
        for (int i = 0; i < 10; i++)
        {
            if (_eqSliders[i] != null)
            {
                _eqSliders[i].Value = gains[i];
                _eqGainLabels[i].Text = $"{gains[i]:0.#}";
            }
        }
        _eqUpdatingFromPreset = false;
        SaveEqSettings();
    }

    private void EqBand_Changed(int bandIndex, double newValue)
    {
        float gain = (float)newValue;
        _player.SetEqBand(bandIndex, gain);
        if (_eqGainLabels[bandIndex] != null)
            _eqGainLabels[bandIndex].Text = $"{gain:0.#}";

        // Update preset to "Custom" if user manually adjusts
        if (!_eqUpdatingFromPreset && _eqPresetCombo != null)
        {
            _eqPresetCombo.SelectionChanged -= EqPreset_Changed;
            _eqPresetCombo.SelectedItem = null;
            _eqPresetCombo.SelectionChanged += EqPreset_Changed;
        }
        SaveEqSettings();
    }

    private void EqPreamp_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        float db = (float)e.NewValue;
        _player.SetEqPreamp(db);
        if (_eqPreampLabel != null) _eqPreampLabel.Text = $"{db:0.#} dB";
        SaveEqSettings();
    }

    private void EqReset_Click(object sender, RoutedEventArgs e)
    {
        _eqPresetCombo!.SelectedItem = "Flat";
        if (_eqPreampSlider != null) _eqPreampSlider.Value = 0;
        _player.SetEqPreamp(0);
    }

    private void SaveEqSettings()
    {
        var current = SettingsManager.Load();
        SettingsManager.Save(current with
        {
            EqEnabled = _player.EqEnabled,
            EqPreset = _eqPresetCombo?.SelectedItem as string ?? "Flat",
            EqBands = _player.GetEqGains(),
            EqPreamp = _player.EqPreampDb
        });
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

    private void NavStats_Click(object sender, RoutedEventArgs e)
    {
        if (_viewMode == ViewMode.Stats) return;
        _viewMode = ViewMode.Stats;
        _currentPlaylist = null;
        UpdateNavigation();
        UpdateSpectrumTimer();
        UpdateMediaTimer();
        AnimateViewTransition(BuildStatsUI);
    }

    // -- Stats --------------------------------------------------------

    private void BuildStatsUI()
    {
        StatsPanel.Children.Clear();

        try { BuildStatsContent(); }
        catch (Exception ex)
        {
            StatsPanel.Children.Add(new TextBlock
            {
                Text = $"Error: {ex.Message}\n{ex.StackTrace}",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                Margin = new Thickness(8)
            });
        }
    }

    private void BuildStatsContent()
    {
        // Total listening time
        var (totalMs, totalPlays) = LibraryManager.GetTotalListeningTime();
        var totalTime = TimeSpan.FromMilliseconds(totalMs);

        // Empty state
        if (totalPlays == 0)
        {
            StatsPanel.Children.Add(new TextBlock
            {
                Text = Strings.T("No listening history yet"),
                FontSize = 13,
                Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0)
            });
            return;
        }

        var summaryGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var playsCard = CreateStatCard(Strings.T("Total Plays"), totalPlays.ToString("N0"));
        Grid.SetColumn(playsCard, 0);
        summaryGrid.Children.Add(playsCard);

        string timeText;
        if (totalTime.TotalHours >= 1)
            timeText = Strings.T("{0}h {1}m", (int)totalTime.TotalHours, totalTime.Minutes);
        else
            timeText = Strings.T("{0} min", (int)totalTime.TotalMinutes);

        var timeCard = CreateStatCard(Strings.T("Listening Time"), timeText);
        Grid.SetColumn(timeCard, 1);
        summaryGrid.Children.Add(timeCard);

        StatsPanel.Children.Add(summaryGrid);

        // Top tracks (clickable — plays the track)
        var topTracks = LibraryManager.GetTopTracks(10);
        if (topTracks.Count > 0)
        {
            StatsPanel.Children.Add(CreateSectionLabel(Strings.T("Top Tracks")));
            for (int i = 0; i < topTracks.Count; i++)
            {
                var t = topTracks[i];
                var row = CreateStatRow(
                    $"{i + 1}",
                    t.Title,
                    t.Artist,
                    Strings.T("{0} plays", t.PlayCount));
                var trackId = t.TrackId;
                row.Tapped += (_, _) =>
                {
                    var track = _allTracks.FirstOrDefault(tr => tr.Id == trackId);
                    if (track != null)
                        _ = PlaySingleTrack(track);
                };
                row.IsHitTestVisible = true;
                StatsPanel.Children.Add(row);
            }
        }

        // Top artists (clickable — navigates to artist detail)
        var topArtists = LibraryManager.GetTopArtists(10);
        if (topArtists.Count > 0)
        {
            StatsPanel.Children.Add(CreateSectionLabel(Strings.T("Top Artists")));
            for (int i = 0; i < topArtists.Count; i++)
            {
                var a = topArtists[i];
                var dur = TimeSpan.FromMilliseconds(a.TotalMs);
                var durText = dur.TotalHours >= 1
                    ? Strings.T("{0}h {1}m", (int)dur.TotalHours, dur.Minutes)
                    : Strings.T("{0} min", (int)dur.TotalMinutes);
                var row = CreateStatRow(
                    $"{i + 1}",
                    a.Artist,
                    durText,
                    Strings.T("{0} plays", a.PlayCount));
                var artistName = a.Artist;
                row.Tapped += (_, _) =>
                {
                    _currentArtistName = artistName;
                    _viewMode = ViewMode.ArtistDetail;
                    _currentPlaylist = null;
                    UpdateNavigation();
                    AnimateViewTransition(() => ApplyFilterAndSort());
                };
                row.IsHitTestVisible = true;
                StatsPanel.Children.Add(row);
            }
        }
    }

    private async Task PlaySingleTrack(TrackInfo track)
    {
        var idx = _allTracks.FindIndex(t => t.Id == track.Id);
        if (idx >= 0)
            _queue.SetQueue(_allTracks, idx);
        try { await _player.PlayTrackAsync(track); }
        catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
        UpdateNowPlaying(track);
    }

    private static Border CreateStatCard(string label, string value)
    {
        var panel = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        return new Border
        {
            Background = ThemeHelper.Brush("CardBackgroundFillColorDefaultBrush"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(2),
            Child = panel
        };
    }

    private static TextBlock CreateSectionLabel(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        };
    }

    private static Grid CreateStatRow(string rank, string primary, string secondary, string trailing)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var rankText = new TextBlock
        {
            Text = rank,
            FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(rankText, 0);

        var infoPanel = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(new TextBlock
        {
            Text = primary,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = secondary,
            FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        });
        Grid.SetColumn(infoPanel, 1);

        var trailingText = new TextBlock
        {
            Text = trailing,
            FontSize = 11,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        Grid.SetColumn(trailingText, 2);

        grid.Children.Add(rankText);
        grid.Children.Add(infoPanel);
        grid.Children.Add(trailingText);
        return grid;
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
                        Text = Strings.T("No media playing"),
                        Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = Strings.T("Play music or a video to control it here"),
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

        TrackCountText.Text = sessions.Count != 1
            ? Strings.T("{0} sessions", sessions.Count)
            : Strings.T("{0} session", sessions.Count);
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
                    () => _player.CurrentTrack != null || _player.IsStream);
                _vizRenderer.OnModeChanged = () => UpdateSpectrumTimer();
                var selector = _vizRenderer.BuildSelector();
                VisualizerSelector.Children.Clear();
                VisualizerSelector.Children.Add(selector);
                // Place Win2D canvas in the star-sized grid row
                if (_vizRenderer.Canvas != null)
                    VisualizerCanvasHost.Children.Add(_vizRenderer.Canvas);
            }

            if (_vizRenderer.IsClassicMode)
            {
                // Classic mode: DispatcherTimer + WaveformCanvas
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
                // Win2D mode
                WaveformCanvas.Visibility = Visibility.Collapsed;
                _vizRenderer.SetCanvasVisibility(true);
                _vizRenderer.Start();

                if (_spectrumTimer != null)
                {
                    _spectrumTimer.Stop();
                    _spectrumTimer = null;
                }
            }
        }
        else
        {
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
        if (_player.IsStream)
        {
            _spectrum.StartLoopback();
            return;
        }

        _spectrum.StopLoopback();
        var track = _player.CurrentTrack;
        if (track != null)
            await _spectrum.PrepareAsync(track.Path);
    }

    private void DrawVisualization()
    {
        var w = WaveformContainer.ActualWidth;
        var h = WaveformContainer.ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (_player.CurrentTrack == null && !_player.IsStream)
        {
            // Show "no track" only if not already shown
            if (_vizNoTrackText == null)
            {
                WaveformCanvas.Children.Clear();
                _vizBandCount = 0;
                _vizNoTrackText = new TextBlock
                {
                    Text = Strings.T("No track playing"), FontSize = 13,
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

        TrackCountText.Text = Strings.T("{0} in queue", queue.Count);
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

    private async void RootGrid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;

            // Check if drop contains folders
            var deferral = e.GetDeferral();
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                bool hasFolder = items.Any(i => i is StorageFolder);
                e.DragUIOverride.Caption = hasFolder ? Strings.T("Add to Library") : Strings.T("Add to Queue");
                e.DragUIOverride.IsCaptionVisible = true;
            }
            finally { deferral.Complete(); }
        }
    }

    private async void RootGrid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        var audioFiles = new List<string>();
        var folders = new List<StorageFolder>();

        foreach (var item in items)
        {
            if (item is StorageFolder folder)
            {
                folders.Add(folder);
            }
            else if (item is StorageFile file)
            {
                var ext = Path.GetExtension(file.Path).ToLowerInvariant();
                if (LibraryManager.AudioExtensions.Contains(ext))
                    audioFiles.Add(file.Path);
            }
        }

        // Handle folder drops — add to library and scan
        if (folders.Count > 0)
        {
            TrackCountText.Text = Strings.T("Scanning...");
            foreach (var folder in folders)
            {
                var folderId = LibraryManager.AddFolder(folder.Path);
                _libraryWatcher?.WatchFolder(folderId, folder.Path);
                await LibraryManager.ScanFolderAsync(folderId, folder.Path);
            }
            LoadTracks();
            return;
        }

        // Handle file drops — add to queue
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
                catch (Exception ex) { TrackArtist.Text = Strings.T("Error: {0}", ex.Message); }
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
            var durationMs = TrackDurationHelper.ResolveDurationMs(filePath, tagFile.Properties.Duration);
            return new TrackInfo
            {
                Id = -1,
                Path = filePath,
                Title = string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                    ? Path.GetFileNameWithoutExtension(filePath)
                    : tagFile.Tag.Title.Trim(),
                Artist = tagFile.Tag.FirstPerformer?.Trim() ?? "",
                Album = tagFile.Tag.Album?.Trim() ?? "",
                DurationMs = durationMs,
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
        _libraryWatcher?.WatchFolder(folderId, folder.Path);

        // Scan in background
        TrackCountText.Text = Strings.T("Scanning...");
        await LibraryManager.ScanFolderAsync(folderId, folder.Path);
        LoadTracks();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsFlyout(sender as FrameworkElement ?? RootGrid);
    }

    private void ShowSettingsFlyout(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(260, 320);

        var panel = new StackPanel { Spacing = 0 };
        var allButtons = new List<Button>();

        static string FindLabel((string key, string label)[] items, string current)
        {
            foreach (var (k, l) in items) if (k == current) return l;
            return "";
        }

        // ── Appearance cascades ──────────────────────────────────

        // Theme
        var currentTheme = SettingsManager.LoadTheme();
        var themes = new[] { ("system", Strings.T("System")), ("light", Strings.T("Light")), ("dark", Strings.T("Dark")) };
        var themeBtn = ActionPanel.CreateCascadeButton(Strings.T("Theme"), FindLabel(themes, currentTheme),
            ActionPanel.CreateRadioSubMenu(themes, currentTheme, k => { SettingsManager.SaveTheme(k); ApplyTheme(k); flyout.Hide(); }));
        allButtons.Add(themeBtn);
        panel.Children.Add(themeBtn);

        // Backdrop
        var currentBackdrop = SettingsManager.LoadBackdrop().Type;
        var backdrops = new[] { ("acrylic", Strings.T("Acrylic")), ("acrylic_custom", Strings.T("Custom Acrylic")), ("mica", Strings.T("Mica")), ("mica_alt", Strings.T("Mica Alt")), ("none", Strings.T("None")) };
        var backdropSubMenu = CreateBackdropSubMenu(backdrops, currentBackdrop, flyout, anchor);
        var backdropBtn = ActionPanel.CreateCascadeButton(Strings.T("Backdrop"), FindLabel(backdrops, currentBackdrop), backdropSubMenu);
        backdropBtn.Tag = Strings.T("Backdrop") + " Acrylic Mica";
        allButtons.Add(backdropBtn);
        panel.Children.Add(backdropBtn);

        // Accent Color — navigate to color picker sub-panel
        var accentBtn = ActionPanel.CreateNavigateButton(Strings.T("Accent Color"), () =>
        {
            ShowAccentColorPanel(flyout, anchor);
        });
        accentBtn.Tag = Strings.T("Accent Color") + " Color Couleur";
        allButtons.Add(accentBtn);
        panel.Children.Add(accentBtn);

        // Visualizer FPS
        var fpsOptions = new[] { ("30", Strings.T("30 FPS")), ("60", Strings.T("60 FPS")) };
        var vizBtn = ActionPanel.CreateCascadeButton(Strings.T("Visualizer"), _vizFps + " FPS",
            ActionPanel.CreateRadioSubMenu(fpsOptions, _vizFps.ToString(), k =>
            {
                var fps = int.Parse(k);
                ApplyVisualizerFps(fps);
                var s = SettingsManager.Load();
                SettingsManager.Save(s with { VisualizerFps = fps });
                flyout.Hide();
            }));
        vizBtn.Tag = Strings.T("Visualizer") + " FPS";
        allButtons.Add(vizBtn);
        panel.Children.Add(vizBtn);

        panel.Children.Add(ActionPanel.CreateSeparator());

        // ── Language cascade ─────────────────────────────────────
        var currentLang = SettingsManager.LoadLanguage();
        var langs = new[] { ("en", Strings.T("English")), ("fr", Strings.T("French")) };
        var langBtn = ActionPanel.CreateCascadeButton(Strings.T("Language"), FindLabel(langs, currentLang),
            ActionPanel.CreateRadioSubMenu(langs, currentLang, k => { SettingsManager.SaveLanguage(k); flyout.Hide(); ApplyLocalization(); }));
        allButtons.Add(langBtn);
        panel.Children.Add(langBtn);

        panel.Children.Add(ActionPanel.CreateSeparator());

        // ── Actions ──────────────────────────────────────────────
        panel.Children.Add(ActionPanel.CreateButton("\uE838", Strings.T("Add Folder"), [], () =>
        {
            flyout.Hide();
            ChooseFolder_Click(this, new RoutedEventArgs());
        }));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateButton("\uE72C", Strings.T("Scan Library"), [], () =>
        {
            flyout.Hide();
            ScanAllFoldersAsync();
        }));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateButton("\uE8B5", Strings.T("Import Playlist"), [], async () =>
        {
            flyout.Hide();
            await ImportPlaylistAsync();
        }));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateButton("\uE74E", Strings.T("Export Playlist"), [], async () =>
        {
            flyout.Hide();
            await ExportPlaylistAsync();
        }));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateSeparator());

        // ── Window ───────────────────────────────────────────────
        panel.Children.Add(ActionPanel.CreateButton("\uE73F",
            _collapseState == CollapseState.Expanded ? Strings.T("Compact Mode") : Strings.T("Expand"),
            ["Ctrl", "L"], () =>
        {
            flyout.Hide();
            ToggleCollapse();
        }));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateButton(
            _isPinnedOnTop ? "\uE842" : "\uE840",
            _isPinnedOnTop ? Strings.T("Unpin from Top") : Strings.T("Pin on Top"),
            [], () =>
        {
            flyout.Hide();
            Pin_Click(this, new RoutedEventArgs());
        }));
        allButtons.Add((Button)panel.Children[^1]);

        // Overlay widget toggle
        {
            var overlayLabel = _overlayWidget != null ? Strings.T("Hide Overlay") : Strings.T("Show Overlay");
            var overlayBtn = ActionPanel.CreateButton("\uEE40", overlayLabel, [], () =>
            {
                flyout.Hide();
                ToggleOverlayWidget();
            });
            overlayBtn.Tag = Strings.T("Overlay Widget") + " Widget Overlay";
            allButtons.Add(overlayBtn);
            panel.Children.Add(overlayBtn);
        }

        panel.Children.Add(ActionPanel.CreateSeparator());

        // Sleep timer — navigate to sub-panel
        {
            var sleepLabel = IsSleepTimerActive
                ? Strings.T("Sleep ({0} min)", (int)SleepTimeRemaining.TotalMinutes)
                : Strings.T("Sleep Timer");
            var sleepBtn = ActionPanel.CreateNavigateButton(sleepLabel, () =>
            {
                ShowSleepTimerPanel(flyout, anchor);
            });
            allButtons.Add(sleepBtn);
            panel.Children.Add(sleepBtn);
        }

        AddLanguageOption("en", Strings.T("English"));
        AddLanguageOption("fr", Strings.T("French"));
		AddLanguageOption("pl", Strings.T("Polish"));

		panel.Children.Add(ActionPanel.CreateSeparator());

        // ── Destructive ──────────────────────────────────────────
        panel.Children.Add(ActionPanel.CreateButton("\uE74D", Strings.T("Reset Library"), [], () =>
        {
            flyout.Hide();
            LibraryManager.ResetLibrary();
            _libraryWatcher?.Restart();
            _allTracks.Clear();
            _displayedTracks.Clear();
            RefreshLibraryWindow();
            _queue.Clear();
            _viewMode = ViewMode.Library;
            _currentPlaylist = null;
            UpdateNavigation();
            ApplyFilterAndSort();
        }, isDestructive: true));
        allButtons.Add((Button)panel.Children[^1]);

        panel.Children.Add(ActionPanel.CreateButton("\uE711", Strings.T("Quit"), [], () =>
        {
            flyout.Hide();
            _isQuitting = true;
            Close();
        }, isDestructive: true));
        allButtons.Add((Button)panel.Children[^1]);

        // ── Search ───────────────────────────────────────────────
        panel.Children.Add(ActionPanel.CreateSeparator());
        var searchBox = new TextBox
        {
            PlaceholderText = Strings.T("Search"),
            FontSize = 12,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(2)
        };
        searchBox.TextChanged += (_, _) =>
        {
            var query = searchBox.Text;
            foreach (var btn in allButtons)
            {
                var tag = btn.Tag as string ?? "";
                btn.Visibility = string.IsNullOrEmpty(query) ||
                    tag.Contains(query, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        };
        panel.Children.Add(searchBox);

        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private Flyout CreateBackdropSubMenu(
        (string key, string label)[] options, string currentKey,
        Flyout parentFlyout, FrameworkElement anchor)
    {
        var flyout = new Flyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop,
            FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(180, 260)
        };

        var panel = new StackPanel { Spacing = 0 };
        foreach (var (key, label) in options)
        {
            var k = key;
            if (k == "acrylic_custom")
            {
                // Custom acrylic navigates to sliders panel
                panel.Children.Add(ActionPanel.CreateCheckItem(label, currentKey == k, () =>
                {
                    var bd = SettingsManager.LoadBackdrop();
                    if (bd.Type != "acrylic_custom")
                        bd = bd with { Type = "acrylic_custom" };
                    SettingsManager.SaveBackdrop(bd);
                    ApplyBackdrop(bd);
                    flyout.Hide();
                    ShowAcrylicSettingsInFlyout(parentFlyout, anchor);
                }));
            }
            else
            {
                panel.Children.Add(ActionPanel.CreateCheckItem(label, currentKey == k, () =>
                {
                    var bd = new BackdropSettings(Type: k);
                    SettingsManager.SaveBackdrop(bd);
                    ApplyBackdrop(bd);
                    parentFlyout.Hide();
                }));
            }
        }
        flyout.Content = panel;
        return flyout;
    }

    private void OpenLibraryWindow()
    {
        if (_libraryWindow != null)
        {
            RefreshLibraryWindow();
            SyncLibraryWindowBounds();
            _libraryWindow.Activate();
            return;
        }

        _libraryWindow = new LibraryWindow();
        _libraryWindow.Closed += LibraryWindow_Closed;
        RefreshLibraryWindow();
        SyncLibraryWindowBounds();
        ApplyPinToLibraryWindow();
        _libraryWindow.Activate();
    }

    private void LibraryWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_libraryWindow == null) return;
        _libraryWindow.Closed -= LibraryWindow_Closed;
        _libraryWindow = null;
    }

    private void CloseLibraryWindow()
    {
        if (_libraryWindow == null) return;
        _libraryWindow.Closed -= LibraryWindow_Closed;
        _libraryWindow.Close();
        _libraryWindow = null;
    }

    private void RefreshLibraryWindow()
    {
        if (_libraryWindow == null) return;
        _libraryWindow.SetRows(BuildLibraryRows());
    }

    private List<LibraryRow> BuildLibraryRows()
    {
        var folderPathById = LibraryManager.GetFolders()
            .ToDictionary(f => f.Id, f => f.Path);

        return _allTracks
            .Select(t => new LibraryRow(
                t.Title,
                folderPathById.TryGetValue(t.FolderId, out var path)
                    ? path
                    : Strings.T("Unknown folder ({0})", t.FolderId)))
            .ToList();
    }

    private void MainAppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_libraryWindow == null) return;
        if (args.DidPositionChange || args.DidSizeChange)
            SyncLibraryWindowBounds();
    }

    private void SyncLibraryWindowBounds()
    {
        if (_libraryWindow == null) return;

        var mainPos = AppWindow.Position;
        var mainSize = AppWindow.Size;
        var width = Math.Max(mainSize.Width + 180, 560);
        var height = mainSize.Height;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var rightX = mainPos.X + mainSize.Width;
        int targetX;
        if (rightX + width <= workArea.X + workArea.Width)
        {
            // Prefer attaching on the right.
            targetX = rightX;
        }
        else
        {
            // Fallback: attach on the left if the right side is out of bounds.
            targetX = mainPos.X - width;
            if (targetX < workArea.X)
            {
                var maxX = Math.Max(workArea.X, workArea.X + workArea.Width - width);
                targetX = Math.Clamp(rightX, workArea.X, maxX);
            }
        }

        var maxY = Math.Max(workArea.Y, workArea.Y + workArea.Height - height);
        var targetY = Math.Clamp(mainPos.Y, workArea.Y, maxY);

        _libraryWindow.AppWindow.MoveAndResize(
            new Windows.Graphics.RectInt32(targetX, targetY, width, height));
    }

    private void ApplyPinToLibraryWindow()
    {
        if (_libraryWindow?.AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = _isPinnedOnTop;
    }

    // -- Overlay widget -----------------------------------------------

    private void ToggleOverlayWidget()
    {
        if (_overlayWidget != null)
        {
            CloseOverlayWidget();
            return;
        }
        OpenOverlayWidget();
    }

    private void OpenOverlayWidget()
    {
        if (_overlayWidget != null)
        {
            _overlayWidget.Activate();
            return;
        }

        _overlayWidget = new OverlayWidget(_player, _queue,
            onPrev: () => DispatcherQueue.TryEnqueue(() => Prev_Click(this, new RoutedEventArgs())),
            onNext: () => DispatcherQueue.TryEnqueue(() => Next_Click(this, new RoutedEventArgs())),
            onPlayPause: () => DispatcherQueue.TryEnqueue(() => PlayPause_Click(this, new RoutedEventArgs())));
        _overlayWidget.Closed += OverlayWidget_Closed;
        _overlayWidget.Activate();
    }

    private void OverlayWidget_Closed(object sender, WindowEventArgs args)
    {
        if (_overlayWidget == null) return;
        _overlayWidget.Closed -= OverlayWidget_Closed;
        _overlayWidget = null;
    }

    private void CloseOverlayWidget()
    {
        if (_overlayWidget == null) return;
        _overlayWidget.Closed -= OverlayWidget_Closed;
        _overlayWidget.Close();
        _overlayWidget = null;
    }

    private async void ScanAllFoldersAsync()
    {
        TrackCountText.Text = Strings.T("Scanning...");
        try
        {
            var added = await LibraryManager.ScanAllFoldersAsync();
            LoadTracks();
            TrackCountText.Text = Strings.T("{0} tracks", _allTracks.Count.ToString("N0"));
        }
        catch (Exception ex)
        {
            TrackCountText.Text = Strings.T("Error: {0}", ex.Message);
        }
    }

    // -- Playlist import / export ---------------------------------

    private async Task ImportPlaylistAsync()
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        picker.FileTypeFilter.Add(".pls");
        InitializeWithWindow.Initialize(picker, _hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        try
        {
            var result = await Task.Run(() => PlaylistPorter.Import(file.Path));
            // Refresh playlist view if already there, otherwise navigate to it
            if (_viewMode == ViewMode.PlaylistList)
                LoadPlaylistList();
            else
            {
                _viewMode = ViewMode.PlaylistList;
                _currentPlaylist = null;
                UpdateNavigation();
                UpdateSpectrumTimer();
                UpdateMediaTimer();
                AnimateViewTransition(() => LoadPlaylistList());
            }
            ShowStatusMessage(Strings.T("Playlist imported: {0}/{1} tracks",
                result.Imported.ToString(), result.Total.ToString()));
        }
        catch (Exception ex)
        {
            ShowStatusMessage(Strings.T("Error: {0}", ex.Message));
        }
    }

    private async Task ExportPlaylistAsync()
    {
        var playlists = LibraryManager.GetPlaylists();
        if (playlists.Count == 0)
        {
            ShowStatusMessage(Strings.T("No playlist to export"));
            return;
        }

        // If already on the Playlists view and one is selected, pre-select it
        PlaylistInfo? selected = _currentPlaylist;

        // When no playlist is selected, ask the user to pick one via a simple dialog
        if (selected == null)
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = Strings.T("Select a playlist to export"),
                CloseButtonText = Strings.T("Cancel"),
                XamlRoot = Content.XamlRoot
            };
            var listView = new Microsoft.UI.Xaml.Controls.ListView
            {
                ItemsSource = playlists.Select(p => p.Name).ToList(),
                Height = 240
            };
            dialog.Content = listView;
            dialog.PrimaryButtonText = Strings.T("Export");
            dialog.IsPrimaryButtonEnabled = false;
            listView.SelectionChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = listView.SelectedIndex >= 0;

            var dlgResult = await dialog.ShowAsync();
            if (dlgResult != Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary) return;
            selected = playlists[listView.SelectedIndex];
        }

        var savePicker = new FileSavePicker();
        savePicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
        savePicker.SuggestedFileName = selected.Name;
        savePicker.FileTypeChoices.Add("M3U Playlist", [".m3u8"]);
        savePicker.FileTypeChoices.Add("PLS Playlist", [".pls"]);
        InitializeWithWindow.Initialize(savePicker, _hwnd);

        var saveFile = await savePicker.PickSaveFileAsync();
        if (saveFile == null) return;

        try
        {
            await Task.Run(() => PlaylistPorter.Export(selected.Id, saveFile.Path));
            ShowStatusMessage($"\"{selected.Name}\" → {Path.GetFileName(saveFile.Path)}");
        }
        catch (Exception ex)
        {
            ShowStatusMessage(Strings.T("Error: {0}", ex.Message));
        }
    }

    private CancellationTokenSource? _statusMsgCts;

    private void ShowStatusMessage(string message)
    {
        _statusMsgCts?.Cancel();
        _statusMsgCts = new CancellationTokenSource();
        var cts = _statusMsgCts;

        TrackCountText.Text = message;
        _ = Task.Delay(3000, cts.Token).ContinueWith(_ =>
        {
            if (!cts.IsCancellationRequested)
                DispatcherQueue.TryEnqueue(RefreshTrackCountText);
        }, TaskScheduler.Default);
    }

    private void RefreshTrackCountText()
    {
        TrackCountText.Text = _viewMode switch
        {
            ViewMode.Library => Strings.T("{0} tracks", _allTracks.Count.ToString("N0")),
            ViewMode.PlaylistList => Strings.T("{0} playlists",
                LibraryManager.GetPlaylists().Count.ToString("N0")),
            _ => TrackCountText.Text
        };
    }

    // -- Backdrop -------------------------------------------------

    private void ApplyBackdrop(BackdropSettings settings)
    {
        _backdropController?.Dispose();
        _backdropController = null;
        _configSource = null;
        SystemBackdrop = null;

        if (settings.Type == "none")
            return;

        // Always use controller API with IsInputActive = true
        // to keep the backdrop effect visible when the window loses focus
        _backdropController = settings.Type switch
        {
            "mica" when MicaController.IsSupported()
                => new MicaController(),
            "mica_alt" when MicaController.IsSupported()
                => new MicaController { Kind = MicaKind.BaseAlt },
            "acrylic_custom" when DesktopAcrylicController.IsSupported()
                => new DesktopAcrylicController
                {
                    TintOpacity = (float)settings.TintOpacity,
                    LuminosityOpacity = (float)settings.LuminosityOpacity,
                    TintColor = ParseColor(settings.TintColor),
                    FallbackColor = ParseColor(settings.FallbackColor),
                    Kind = settings.Kind == "Thin"
                        ? DesktopAcrylicKind.Thin
                        : DesktopAcrylicKind.Base,
                },
            _ when DesktopAcrylicController.IsSupported()
                => new DesktopAcrylicController(),
            _ => null
        };

        if (_backdropController == null) return;

        _configSource = new SystemBackdropConfiguration { IsInputActive = true };
        if (Content is FrameworkElement fe)
            _configSource.Theme = (SystemBackdropTheme)fe.ActualTheme;

        var target = this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>();
        switch (_backdropController)
        {
            case MicaController mica:
                mica.AddSystemBackdropTarget(target);
                mica.SetSystemBackdropConfiguration(_configSource);
                break;
            case DesktopAcrylicController acrylic:
                acrylic.AddSystemBackdropTarget(target);
                acrylic.SetSystemBackdropConfiguration(_configSource);
                break;
        }
    }

    private void ShowAcrylicSettingsInFlyout(Flyout flyout, FrameworkElement anchor)
    {
        var settings = SettingsManager.LoadBackdrop();
        var suppressChanges = true;
        var currentKind = settings.Kind;

        var panel = ActionPanel.CreateSubPanelWithHeader(Strings.T("Custom Acrylic"), () =>
        {
            flyout.Hide();
            ShowSettingsFlyout(anchor);
        });
        panel.Width = 260;

        // Tint Opacity
        var tintOpacityValue = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            Text = settings.TintOpacity.ToString("F2")
        };
        var tintOpacitySlider = new Slider
        {
            Minimum = 0, Maximum = 1, StepFrequency = 0.01,
            Value = settings.TintOpacity,
            Margin = new Thickness(0, -2, 0, 0)
        };

        var tintOpacityHeader = new Grid { Margin = new Thickness(8, 8, 8, 0) };
        tintOpacityHeader.Children.Add(new TextBlock
        {
            Text = Strings.T("Tint Opacity"), FontSize = 12,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
        });
        tintOpacityHeader.Children.Add(tintOpacityValue);
        panel.Children.Add(tintOpacityHeader);
        panel.Children.Add(new StackPanel
        {
            Margin = new Thickness(4, 0, 4, 0),
            Children = { tintOpacitySlider }
        });

        // Luminosity
        var luminosityValue = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            Text = settings.LuminosityOpacity.ToString("F2")
        };
        var luminositySlider = new Slider
        {
            Minimum = 0, Maximum = 1, StepFrequency = 0.01,
            Value = settings.LuminosityOpacity,
            Margin = new Thickness(0, -2, 0, 0)
        };

        var luminosityHeader = new Grid { Margin = new Thickness(8, 8, 8, 0) };
        luminosityHeader.Children.Add(new TextBlock
        {
            Text = Strings.T("Luminosity"), FontSize = 12,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
        });
        luminosityHeader.Children.Add(luminosityValue);
        panel.Children.Add(luminosityHeader);
        panel.Children.Add(new StackPanel
        {
            Margin = new Thickness(4, 0, 4, 0),
            Children = { luminositySlider }
        });

        // Tint Color
        var tintColorPreview = new Button
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), Padding = new Thickness(0),
            MinWidth = 0, MinHeight = 0,
            BorderBrush = ThemeHelper.Brush("ControlStrokeColorDefaultBrush"),
            Background = new SolidColorBrush(ParseColor(settings.TintColor))
        };
        ToolTipService.SetToolTip(tintColorPreview, Strings.T("Choose color"));
        var tintColorBox = new TextBox
        {
            Text = settings.TintColor, FontSize = 12, MaxLength = 7,
            MinWidth = 0
        };

        // Color picker flyout for tint
        var tintPickerFlyout = new Flyout();
        var tintPicker = new ColorPicker
        {
            Color = ParseColor(settings.TintColor),
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            IsColorSpectrumVisible = true,
            IsColorPreviewVisible = true,
            IsMoreButtonVisible = false
        };
        tintPicker.ColorChanged += (_, args) =>
        {
            var c = args.NewColor;
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            tintColorBox.Text = hex;
            tintColorPreview.Background = new SolidColorBrush(c);
        };
        tintPickerFlyout.Content = tintPicker;
        tintColorPreview.Flyout = tintPickerFlyout;

        var tintColorGrid = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(8, 8, 8, 0)
        };
        tintColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tintColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tintColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tintColorLabel = new TextBlock
        {
            Text = Strings.T("Tint"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(tintColorLabel, 0);
        Grid.SetColumn(tintColorBox, 1);
        Grid.SetColumn(tintColorPreview, 2);
        tintColorGrid.Children.Add(tintColorLabel);
        tintColorGrid.Children.Add(tintColorBox);
        tintColorGrid.Children.Add(tintColorPreview);
        panel.Children.Add(tintColorGrid);

        // Fallback Color
        var fallbackColorPreview = new Button
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), Padding = new Thickness(0),
            MinWidth = 0, MinHeight = 0,
            BorderBrush = ThemeHelper.Brush("ControlStrokeColorDefaultBrush"),
            Background = new SolidColorBrush(ParseColor(settings.FallbackColor))
        };
        ToolTipService.SetToolTip(fallbackColorPreview, Strings.T("Choose color"));
        var fallbackColorBox = new TextBox
        {
            Text = settings.FallbackColor, FontSize = 12, MaxLength = 7,
            MinWidth = 0
        };

        // Color picker flyout for fallback
        var fallbackPickerFlyout = new Flyout();
        var fallbackPicker = new ColorPicker
        {
            Color = ParseColor(settings.FallbackColor),
            IsAlphaEnabled = false,
            IsHexInputVisible = true,
            IsColorSpectrumVisible = true,
            IsColorPreviewVisible = true,
            IsMoreButtonVisible = false
        };
        fallbackPicker.ColorChanged += (_, args) =>
        {
            var c = args.NewColor;
            var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            fallbackColorBox.Text = hex;
            fallbackColorPreview.Background = new SolidColorBrush(c);
        };
        fallbackPickerFlyout.Content = fallbackPicker;
        fallbackColorPreview.Flyout = fallbackPickerFlyout;

        var fallbackColorGrid = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(8, 8, 8, 0)
        };
        fallbackColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        fallbackColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fallbackColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var fallbackColorLabel = new TextBlock
        {
            Text = Strings.T("Fallback"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(fallbackColorLabel, 0);
        Grid.SetColumn(fallbackColorBox, 1);
        Grid.SetColumn(fallbackColorPreview, 2);
        fallbackColorGrid.Children.Add(fallbackColorLabel);
        fallbackColorGrid.Children.Add(fallbackColorBox);
        fallbackColorGrid.Children.Add(fallbackColorPreview);
        panel.Children.Add(fallbackColorGrid);

        // Kind (Base / Thin)
        var baseBtn = new Button
        {
            Content = "Base", HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4), Tag = "Base"
        };
        var thinBtn = new Button
        {
            Content = "Thin", HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(4), Tag = "Thin"
        };

        void UpdateKindButtons()
        {
            var selected = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            var normal = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            baseBtn.Background = currentKind == "Base" ? selected : normal;
            thinBtn.Background = currentKind == "Thin" ? selected : normal;
        }

        UpdateKindButtons();

        var kindGrid = new Grid { ColumnSpacing = 6, Margin = new Thickness(8, 10, 8, 8) };
        kindGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        kindGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        kindGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var kindLabel = new TextBlock
        {
            Text = Strings.T("Style"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush")
        };
        Grid.SetColumn(kindLabel, 0);
        Grid.SetColumn(baseBtn, 1);
        Grid.SetColumn(thinBtn, 2);
        kindGrid.Children.Add(kindLabel);
        kindGrid.Children.Add(baseBtn);
        kindGrid.Children.Add(thinBtn);
        panel.Children.Add(kindGrid);

        // Apply changes helper
        void ApplyChanges()
        {
            if (suppressChanges) return;
            var bd = new BackdropSettings(
                Type: "acrylic_custom",
                TintOpacity: tintOpacitySlider.Value,
                LuminosityOpacity: luminositySlider.Value,
                TintColor: tintColorBox.Text,
                FallbackColor: fallbackColorBox.Text,
                Kind: currentKind);
            SettingsManager.SaveBackdrop(bd);
            ApplyBackdrop(bd);
        }

        // Wire events
        tintOpacitySlider.ValueChanged += (_, _) =>
        {
            tintOpacityValue.Text = tintOpacitySlider.Value.ToString("F2");
            ApplyChanges();
        };
        luminositySlider.ValueChanged += (_, _) =>
        {
            luminosityValue.Text = luminositySlider.Value.ToString("F2");
            ApplyChanges();
        };
        tintColorBox.TextChanged += (_, _) =>
        {
            try { tintColorPreview.Background = new SolidColorBrush(ParseColor(tintColorBox.Text)); } catch { }
            if (tintColorBox.Text.StartsWith('#') && tintColorBox.Text.Length == 7)
                ApplyChanges();
        };
        fallbackColorBox.TextChanged += (_, _) =>
        {
            try { fallbackColorPreview.Background = new SolidColorBrush(ParseColor(fallbackColorBox.Text)); } catch { }
            if (fallbackColorBox.Text.StartsWith('#') && fallbackColorBox.Text.Length == 7)
                ApplyChanges();
        };
        baseBtn.Click += (_, _) => { currentKind = "Base"; UpdateKindButtons(); ApplyChanges(); };
        thinBtn.Click += (_, _) => { currentKind = "Thin"; UpdateKindButtons(); ApplyChanges(); };

        suppressChanges = false;

        flyout.Content = panel;
    }

    private void ShowAccentColorPanel(Flyout flyout, FrameworkElement anchor)
    {
        var panel = ActionPanel.CreateSubPanelWithHeader(Strings.T("Accent Color"), () =>
        {
            flyout.Hide();
            ShowSettingsFlyout(anchor);
        });

        var currentAccent = SettingsManager.Load().AccentColor;

        // Color grid: 6 columns
        const int columns = 6;
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (int c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        int rows = (int)Math.Ceiling(ThemeHelper.AccentPresets.Length / (double)columns);
        for (int r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < ThemeHelper.AccentPresets.Length; i++)
        {
            var (name, hex) = ThemeHelper.AccentPresets[i];
            int row = i / columns;
            int col = i % columns;

            bool isSystem = string.IsNullOrEmpty(hex);
            bool isSelected = (isSystem && string.IsNullOrEmpty(currentAccent))
                           || (!isSystem && hex.Equals(currentAccent, StringComparison.OrdinalIgnoreCase));

            var swatch = new Button
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(0),
                Margin = new Thickness(3),
                BorderThickness = new Thickness(isSelected ? 2 : 0),
                BorderBrush = isSelected
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : null,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = hex
            };

            if (isSystem)
            {
                // System swatch: gradient-like icon
                swatch.Background = ThemeHelper.Brush("AccentFillColorDefaultBrush");
                swatch.Content = new FontIcon
                {
                    Glyph = "\uE770",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                };
            }
            else
            {
                swatch.Background = new SolidColorBrush(ThemeHelper.ParseHexColor(hex));
                if (isSelected)
                {
                    swatch.Content = new FontIcon
                    {
                        Glyph = "\uE73E",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White)
                    };
                }
            }

            ToolTipService.SetToolTip(swatch, name);

            swatch.Click += (s, _) =>
            {
                var selectedHex = (s as Button)?.Tag as string ?? "";
                flyout.Hide();
                DispatcherQueue.TryEnqueue(() => ApplyAccentAndSave(selectedHex));
            };

            Grid.SetRow(swatch, row);
            Grid.SetColumn(swatch, col);
            grid.Children.Add(swatch);
        }

        panel.Children.Add(grid);

        // Custom hex input
        panel.Children.Add(ActionPanel.CreateSeparator());
        var customGrid = new Grid { Margin = new Thickness(4, 0, 4, 4) };
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var customPreview = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeHelper.Brush("ControlStrokeColorDefaultBrush"),
            Background = string.IsNullOrEmpty(currentAccent)
                ? ThemeHelper.Brush("AccentFillColorDefaultBrush")
                : new SolidColorBrush(ThemeHelper.ParseHexColor(currentAccent)),
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(customPreview, 0);

        var customBox = new TextBox
        {
            PlaceholderText = "#0078D4",
            Text = currentAccent,
            FontSize = 12,
            MaxLength = 7,
            Padding = new Thickness(6, 4, 6, 4)
        };
        Grid.SetColumn(customBox, 1);

        var applyBtn = new Button
        {
            Content = "Apply",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12
        };
        Grid.SetColumn(applyBtn, 2);

        customBox.TextChanged += (_, _) =>
        {
            var text = customBox.Text;
            if (text.StartsWith('#') && text.Length == 7)
            {
                try { customPreview.Background = new SolidColorBrush(ThemeHelper.ParseHexColor(text)); } catch { }
            }
        };
        applyBtn.Click += (_, _) =>
        {
            var text = customBox.Text.Trim();
            if (text.StartsWith('#') && text.Length == 7)
            {
                flyout.Hide();
                DispatcherQueue.TryEnqueue(() => ApplyAccentAndSave(text));
            }
        };

        customGrid.Children.Add(customPreview);
        customGrid.Children.Add(customBox);
        customGrid.Children.Add(applyBtn);
        panel.Children.Add(customGrid);

        flyout.Content = panel;
    }

    private void ApplyAccentAndSave(string hexColor)
    {
        var s = SettingsManager.Load();
        SettingsManager.Save(s with { AccentColor = hexColor });

        // Update accent resources in place (no remove/add — safe at runtime)
        ThemeHelper.ApplyAccentColor(hexColor);

        // Force theme re-resolve: cycle through both explicit themes then back
        if (Content is FrameworkElement root)
        {
            var current = root.RequestedTheme;
            root.RequestedTheme = ElementTheme.Light;
            root.RequestedTheme = ElementTheme.Dark;
            root.RequestedTheme = current;
        }

        // Rebuild code-behind elements
        ApplyFilterAndSort();
        UpdateNavigation();
        UpdateRepeatIcon();
        ShuffleIcon.Foreground = _queue.Shuffle
            ? ThemeHelper.Brush("AccentTextFillColorPrimaryBrush")
            : ThemeHelper.Brush("TextFillColorPrimaryBrush");
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
            return new Windows.UI.Color { A = 255, R = 0, G = 0, B = 0 };
        return new Windows.UI.Color
        {
            A = 255,
            R = Convert.ToByte(hex[..2], 16),
            G = Convert.ToByte(hex[2..4], 16),
            B = Convert.ToByte(hex[4..6], 16)
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

    /// <summary>Re-apply all localized text to XAML-defined elements.</summary>
    private void ApplyLocalization()
    {
        // Navigation tabs (dynamic)
        for (int i = 0; i < TabDefs.Length; i++)
        {
            if (_navTexts[i] != null)
                _navTexts[i].Text = Strings.T(TabDefs[i].LabelKey);
        }

        // Sort menu
        SortTitle.Text = Strings.T("Title");
        SortArtist.Text = Strings.T("Artist");
        SortAlbum.Text = Strings.T("Album");
        SortDuration.Text = Strings.T("Duration");
        SortBpm.Text = Strings.T("BPM");
        SortAscending.Text = Strings.T("Ascending");

        // Search box
        SearchBox.PlaceholderText = Strings.T("Search");

        // Tooltips
        ToolTipService.SetToolTip(CollapseButton, Strings.T("Compact (Ctrl+L)"));
        ToolTipService.SetToolTip(PinButton, Strings.T("Pin on top"));
        ToolTipService.SetToolTip(ShuffleButton, Strings.T("Shuffle"));
        ToolTipService.SetToolTip(PrevButton, Strings.T("Previous"));
        ToolTipService.SetToolTip(NextButton, Strings.T("Next"));
        ToolTipService.SetToolTip(RepeatButton, Strings.T("Repeat"));
        ToolTipService.SetToolTip(SpeedButton, Strings.T("Playback Speed"));
        ToolTipService.SetToolTip(RadioPlayBtn, Strings.T("Play stream"));
        ToolTipService.SetToolTip(PlaylistBackBtn, Strings.T("Back to playlists"));
        ToolTipService.SetToolTip(AlbumBackBtn, Strings.T("Back to albums"));
        ToolTipService.SetToolTip(ArtistBackBtn, Strings.T("Back to artists"));
        ToolTipService.SetToolTip(NewPlaylistBtn, Strings.T("New Playlist"));
        ToolTipService.SetToolTip(ClearQueueBtn, Strings.T("Clear Queue"));
        ToolTipService.SetToolTip(SettingsBtn, Strings.T("Settings"));

        // Radio
        RadioStreamHeader.Text = Strings.T("Radio Stream");
        RadioUrlBox.PlaceholderText = Strings.T("Enter stream URL (e.g. http://...)");
        RecentStationsHeader.Text = Strings.T("Recent stations");

        // Podcast
        if (_viewMode != ViewMode.PodcastEpisodes)
            PodcastSearchBox.PlaceholderText = Strings.T("Search podcasts...");

        // Bottom bar
        ChooseFolderText.Text = Strings.T("Choose Folder");
        TrackCountText.Text = Strings.T("0 tracks");

        // Now playing defaults (only if no track is active)
        if (_player.CurrentTrack == null && !_isRadioPlaying)
        {
            TrackTitle.Text = Strings.T("No track");
            MiniTrackText.Text = Strings.T("No track");
        }

        // Rebuild current view to pick up new strings
        UpdateNavigation();
        ApplyFilterAndSort();
    }

    // -- Collapse animation ----------------------------------------

    private void ToggleCollapse()
    {
        // Cycle: Expanded ↔ Compact (Mini removed — use overlay widget instead)
        _collapseState = _collapseState == CollapseState.Expanded
            ? CollapseState.Compact
            : CollapseState.Expanded;

        _targetHeight = _collapseState == CollapseState.Compact
            ? _collapsedHeight
            : _expandedHeight;

        _currentAnimHeight = AppWindow.Size.Height;

        // Keep bottom edge fixed
        _animStartY = AppWindow.Position.Y;
        var bottomEdge = _animStartY + AppWindow.Size.Height;
        _targetY = bottomEdge - _targetHeight;

        // Update icon and tooltip
        CollapseIcon.Glyph = "\uE73F";
        ToolTipService.SetToolTip(CollapseButton,
            _collapseState == CollapseState.Expanded ? "Compact (Ctrl+L)" : "Expand (Ctrl+L)");

        // Show elements before expanding animation
        if (_collapseState == CollapseState.Expanded)
        {
            NowPlayingCard.Visibility = Visibility.Visible;
            MiniPlayerBar.Visibility = Visibility.Collapsed;
            VolumeRow.Visibility = Visibility.Visible;
            NavRow.Visibility = Visibility.Visible;
            SearchSortRow.Visibility = (_viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistDetail
                || _viewMode == ViewMode.AlbumDetail || _viewMode == ViewMode.ArtistDetail)
                ? Visibility.Visible : Visibility.Collapsed;
            var isPodcast = _viewMode == ViewMode.Podcast || _viewMode == ViewMode.PodcastEpisodes;
            var isTrackView = _viewMode == ViewMode.Library || _viewMode == ViewMode.PlaylistList
                || _viewMode == ViewMode.PlaylistDetail || _viewMode == ViewMode.Queue
                || _viewMode == ViewMode.AlbumDetail || _viewMode == ViewMode.ArtistDetail;
            TrackListView.Visibility = isTrackView ? Visibility.Visible : Visibility.Collapsed;
            AlbumsGridView.Visibility = (_viewMode == ViewMode.Albums || _viewMode == ViewMode.Artists)
                ? Visibility.Visible : Visibility.Collapsed;
            WaveformContainer.Visibility = _viewMode == ViewMode.Visualizer
                ? Visibility.Visible : Visibility.Collapsed;
            EqualizerContainer.Visibility = _viewMode == ViewMode.Equalizer
                ? Visibility.Visible : Visibility.Collapsed;
            RadioContainer.Visibility = _viewMode == ViewMode.Radio
                ? Visibility.Visible : Visibility.Collapsed;
            PodcastContainer.Visibility = isPodcast
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
            AlbumsGridView.Visibility = Visibility.Collapsed;
            VolumeRow.Visibility = Visibility.Collapsed;
            NavRow.Visibility = Visibility.Collapsed;
            SearchSortRow.Visibility = Visibility.Collapsed;
            TrackListView.Visibility = Visibility.Collapsed;
            WaveformContainer.Visibility = Visibility.Collapsed;
            EqualizerContainer.Visibility = Visibility.Collapsed;
            RadioContainer.Visibility = Visibility.Collapsed;
            PodcastContainer.Visibility = Visibility.Collapsed;
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
                AlbumsGridView.Visibility = Visibility.Collapsed;
                WaveformContainer.Visibility = Visibility.Collapsed;
                EqualizerContainer.Visibility = Visibility.Collapsed;
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
                AlbumsGridView.Visibility = Visibility.Collapsed;
                WaveformContainer.Visibility = Visibility.Collapsed;
                EqualizerContainer.Visibility = Visibility.Collapsed;
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
            var step = (int)(diff * 0.18);
            if (step == 0) step = diff > 0 ? 1 : -1;
            _currentAnimHeight += step;
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
        ShowSettingsFlyout(sender as FrameworkElement ?? RootGrid);
        e.Handled = true;
    }

    private void ShowMiniContextMenu(FrameworkElement anchor)
    {
        var flyout = new Flyout();
        flyout.FlyoutPresenterStyle = ActionPanel.CreateFlyoutPresenterStyle(minWidth: 140, maxWidth: 180);

        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(ActionPanel.CreateButton("\uE740", Strings.T("Expand"), ["Ctrl", "L"], () =>
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
        ApplyPinToLibraryWindow();

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
            CloseLibraryWindow();
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
            CloseLibraryWindow();
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
        AppendMenu(hMenu, 0, IDM_SHOW, Strings.T("Show\tCtrl+Alt+M"));
        AppendMenu(hMenu, MF_SEPARATOR, 0, null);
        AppendMenu(hMenu, 0, IDM_QUIT, Strings.T("Quit"));

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

    private void RestoreQueuePreview()
    {
        if (_queue.CurrentTrack == null) return;

        var track = _queue.CurrentTrack;
        TrackTitle.Text = track.Title;
        TrackArtist.Text = track.Artist;
        TrackAlbum.Text = track.Album;
        LoadAlbumArt(track.Path);
        UpdateMiniPlayer(track);

        if (track.DurationMs <= 0) return;

        var duration = TimeSpan.FromMilliseconds(track.DurationMs);
        var position = TimeSpan.FromSeconds(_queue.SavedPositionSeconds);
        _isSeeking = true;
        TimelineSlider.IsEnabled = true;
        TimelineSlider.Maximum = duration.TotalSeconds;
        TimelineSlider.Value = Math.Min(position.TotalSeconds, duration.TotalSeconds);
        DurationText.Text = TrackDurationHelper.FormatDuration(duration);
        PositionText.Text = FormatTime(position);
        _isSeeking = false;
    }

    // -- Sleep Timer ------------------------------------------------

    private void SetSleepTimer(int minutes)
    {
        CancelSleepTimer();

        if (minutes <= 0) return;

        _sleepTargetTime = DateTime.Now.AddMinutes(minutes);
        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sleepTimer.Tick += SleepTimer_Tick;
        _sleepTimer.Start();
    }

    private void CancelSleepTimer()
    {
        _sleepTimer?.Stop();
        _sleepTimer = null;
    }

    private bool IsSleepTimerActive => _sleepTimer != null;

    private TimeSpan SleepTimeRemaining =>
        IsSleepTimerActive ? _sleepTargetTime - DateTime.Now : TimeSpan.Zero;

    private void SleepTimer_Tick(object? sender, object e)
    {
        var remaining = _sleepTargetTime - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            CancelSleepTimer();
            _player.Stop();
            PlayPauseIcon.Glyph = "\uE768";
            MiniPlayPauseIcon.Glyph = "\uE768";
        }
    }

    private void ShowAboutPanel(Flyout flyout, FrameworkElement anchor)
    {
        var panel = ActionPanel.CreateSubPanelWithHeader(Strings.T("About"), () =>
        {
            flyout.Hide();
            ShowSettingsFlyout(anchor);
        });

        // ── App identity block ────────────────────────────────────
        var identity = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 10),
            Spacing = 2
        };

        identity.Children.Add(new TextBlock
        {
            Text = "Audiomatic",
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        identity.Children.Add(new TextBlock
        {
            Text = $"{Strings.T("Developer")} : OhMyCode",
            FontSize = 12,
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        identity.Children.Add(new TextBlock
        {
            Text = "Version 0.2.0",
            FontSize = 12,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        panel.Children.Add(identity);
        panel.Children.Add(ActionPanel.CreateSeparator());

        // ── Links ─────────────────────────────────────────────────
        const string GitHubPath =
            "M8 0C3.58 0 0 3.58 0 8c0 3.54 2.29 6.53 5.47 7.59.4.07.55-.17.55-.38" +
            " 0-.19-.01-.82-.01-1.49-2.01.37-2.53-.49-2.69-.94-.09-.23-.48-.94-.82-1.13" +
            "-.28-.15-.68-.52-.01-.53.63-.01 1.08.58 1.23.82.72 1.21 1.87.87 2.33.66" +
            ".07-.52.28-.87.51-1.07-1.78-.2-3.64-.89-3.64-3.95 0-.87.31-1.59.82-2.15" +
            "-.08-.2-.36-1.02.08-2.12 0 0 .67-.21 2.2.82.64-.18 1.32-.27 2-.27.68 0" +
            " 1.36.09 2 .27 1.53-1.04 2.2-.82 2.2-.82.44 1.1.16 1.92.08 2.12.51.56" +
            ".82 1.27.82 2.15 0 3.07-1.87 3.75-3.65 3.95.29.25.54.73.54 1.48 0 1.07" +
            "-.01 1.93-.01 2.2 0 .21.15.46.55.38A8.013 8.013 0 0016 8c0-4.42-3.58-8-8-8z";

        // Buy Me a Coffee: stylised filled mug silhouette (16×16 viewport)
        const string BmcPath =
            "M2 3h11v7c0 1.66-1.34 3-3 3H5c-1.66 0-3-1.34-3-3V3z" +
            " M13 5h1c.55 0 1 .45 1 1v2c0 .55-.45 1-1 1h-1" +
            " M4.5 0v1.5 M7.5 0v1.5 M10.5 0v1.5" +
            " M1 13h13";

        panel.Children.Add(ActionPanel.CreateLinkRow(
            ActionPanel.CreateSvgIcon(GitHubPath, 14),
            Strings.T("GitHub"),
            () => _ = Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/devohmycode"))));

        panel.Children.Add(ActionPanel.CreateLinkRow(
            ActionPanel.CreateSvgIcon(BmcPath, 14, isFilled: false),
            Strings.T("Buy Me a Coffee"),
            () => _ = Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://buymeacoffee.com/ohmycodeapp"))));

        flyout.Content = panel;
    }

    private void ShowSleepTimerPanel(Flyout flyout, FrameworkElement anchor)
    {
        var panel = ActionPanel.CreateSubPanelWithHeader(Strings.T("Sleep Timer"), () =>
        {
            flyout.Hide();
            ShowSettingsFlyout(anchor);
        });

        if (IsSleepTimerActive)
        {
            var rem = SleepTimeRemaining;
            var remText = rem.TotalMinutes >= 1
                ? Strings.T("{0} min remaining", (int)rem.TotalMinutes)
                : Strings.T("{0}s remaining", (int)rem.TotalSeconds);
            panel.Children.Add(ActionPanel.CreateSectionHeader(remText));
            panel.Children.Add(ActionPanel.CreateButton("\uE711", Strings.T("Cancel Timer"), [], () =>
            {
                CancelSleepTimer();
                flyout.Hide();
            }, isDestructive: true));
        }
        else
        {
            foreach (var mins in new[] { 15, 30, 45, 60, 90 })
            {
                var m = mins;
                var label = m >= 60
                    ? (m % 60 > 0 ? Strings.T("{0}h {1}min", m / 60, m % 60) : Strings.T("{0}h", m / 60))
                    : Strings.T("{0} min", m);
                panel.Children.Add(ActionPanel.CreateButton("\uE823", label, [], () =>
                {
                    SetSleepTimer(m);
                    flyout.Hide();
                }));
            }
        }

        flyout.Content = panel;
    }
}
