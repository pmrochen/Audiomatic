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
using Microsoft.UI.Xaml.Media.Imaging;
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

    // Collapse animation
    private bool _isCollapsed;
    private readonly int _expandedHeight = 680;
    private readonly int _collapsedHeight = 220;
    private DispatcherTimer? _animTimer;
    private int _targetHeight;
    private int _currentAnimHeight;

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

        // Apply backdrop
        ApplyBackdrop(SettingsManager.LoadBackdrop());

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

        // Restore queue state
        _queue.LoadState(_allTracks);
        if (_queue.CurrentTrack != null)
        {
            UpdateNowPlaying(_queue.CurrentTrack);
        }

        // Restore shuffle/repeat
        _queue.Shuffle = settings.ShuffleEnabled;
        if (settings.ShuffleEnabled)
            ShuffleIcon.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];

        if (Enum.TryParse<RepeatMode>(settings.RepeatMode, true, out var rm))
        {
            _queue.Repeat = rm;
            UpdateRepeatIcon();
        }

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
                WindowX = pos.X,
                WindowY = pos.Y
            });
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
        var query = SearchBox.Text?.Trim() ?? "";
        _displayedTracks = string.IsNullOrEmpty(query)
            ? [.. _allTracks]
            : _allTracks.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        // Sort
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

        RebuildTrackList();
        TrackCountText.Text = $"{_allTracks.Count:N0} tracks";
    }

    private void RebuildTrackList()
    {
        // Save scroll position
        var scrollViewer = FindScrollViewer(TrackListView);
        var scrollOffset = scrollViewer?.VerticalOffset ?? 0;

        TrackListView.Items.Clear();
        foreach (var track in _displayedTracks)
        {
            var isPlaying = _queue.CurrentTrack?.Id == track.Id;

            var grid = new Grid { Padding = new Thickness(2, 4, 2, 4), ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Speaker icon or music note
            var icon = new FontIcon
            {
                Glyph = isPlaying ? "\uE767" : "\uE8D6",
                FontSize = 12,
                Foreground = isPlaying
                    ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
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
                    ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                    : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"],
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
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
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
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dur, 2);

            grid.Children.Add(icon);
            grid.Children.Add(info);
            grid.Children.Add(dur);
            grid.Tag = track;

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
        LoadAlbumArt(track.Path);
        RebuildTrackList(); // Re-highlight current track
    }

    private async void LoadAlbumArt(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            if (tagFile.Tag.Pictures.Length > 0)
            {
                var pic = tagFile.Tag.Pictures[0];
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0));
                writer.WriteBytes(pic.Data.Data);
                await writer.StoreAsync();
                stream.Seek(0);

                var bitmap = new BitmapImage();
                bitmap.SetSource(stream);
                AlbumArtImage.Source = bitmap;
                AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                AlbumArtImage.Visibility = Visibility.Visible;
                return;
            }
        }
        catch { }

        AlbumArtImage.Source = null;
        AlbumArtImage.Visibility = Visibility.Collapsed;
        AlbumArtPlaceholder.Visibility = Visibility.Visible;
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
            ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
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
            ? (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
            : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
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
        if (e.ClickedItem is Grid grid && grid.Tag is TrackInfo track)
        {
            // Find index in displayed tracks
            var idx = _displayedTracks.FindIndex(t => t.Id == track.Id);
            if (idx < 0) return;

            // Set the displayed list as the queue
            _queue.SetQueue(_displayedTracks, idx);
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

        // Toggle actions
        panel.Children.Add(ActionPanel.CreateButton("\uE73F",
            _isCollapsed ? "Expand" : "Compact Mode",
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

    // -- Collapse animation ----------------------------------------

    private void ToggleCollapse()
    {
        _isCollapsed = !_isCollapsed;
        _targetHeight = _isCollapsed ? _collapsedHeight : _expandedHeight;
        _currentAnimHeight = AppWindow.Size.Height;

        // Update collapse/expand icon
        CollapseIcon.Glyph = _isCollapsed ? "\uE740" : "\uE73F";
        ToolTipService.SetToolTip(CollapseButton, _isCollapsed ? "Expand (Ctrl+L)" : "Compact (Ctrl+L)");

        // Show elements before expanding animation
        if (!_isCollapsed)
        {
            VolumeRow.Visibility = Visibility.Visible;
            SearchSortRow.Visibility = Visibility.Visible;
            TrackListView.Visibility = Visibility.Visible;
            BottomBar.Visibility = Visibility.Visible;
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

            // Hide elements after collapsing animation
            if (_isCollapsed)
            {
                VolumeRow.Visibility = Visibility.Collapsed;
                SearchSortRow.Visibility = Visibility.Collapsed;
                TrackListView.Visibility = Visibility.Collapsed;
                BottomBar.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            // Ease-out: move a fraction of remaining distance each frame
            _currentAnimHeight += (int)(diff * 0.18);
        }

        AppWindow.Resize(new Windows.Graphics.SizeInt32(AppWindow.Size.Width, _currentAnimHeight));
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
        }
        else
        {
            ShowWindow(_hwnd, 5); // SW_SHOW
            SetForegroundWindow(_hwnd);
            _isVisible = true;
        }
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
