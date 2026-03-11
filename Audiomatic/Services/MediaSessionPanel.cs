using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;

namespace Audiomatic.Services;

/// <summary>
/// UI panel for a single system media session.
/// </summary>
internal sealed class MediaSessionPanel
{
    private readonly GlobalSystemMediaTransportControlsSession _session;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    private readonly Image _thumbnail;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _artistBlock;
    private readonly TextBlock _appNameBlock;
    private readonly Button _prevButton;
    private readonly Button _playPauseButton;
    private readonly FontIcon _playPauseIcon;
    private readonly Button _nextButton;
    private readonly Slider _timelineSlider;
    private readonly TextBlock _positionText;
    private readonly TextBlock _durationText;
    private bool _isSeeking;
    private DateTimeOffset _blockUntil;

    public Grid RootElement { get; }

    public MediaSessionPanel(GlobalSystemMediaTransportControlsSession session,
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;

        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        // Root card
        RootElement = new Grid
        {
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(6, 4, 6, 4),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
        };
        RootElement.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RootElement.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RootElement.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Row 0: Thumbnail + info
        var infoGrid = new Grid { ColumnSpacing = 12 };
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(infoGrid, 0);

        _thumbnail = new Image
        {
            Width = 48,
            Height = 48,
            Stretch = Stretch.UniformToFill,
        };
        var thumbnailBorder = new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = _thumbnail,
            Width = 48,
            Height = 48,
            Background = new SolidColorBrush(Colors.DimGray)
        };
        Grid.SetColumn(thumbnailBorder, 0);
        infoGrid.Children.Add(thumbnailBorder);

        var textPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };

        _appNameBlock = new TextBlock
        {
            Text = GetAppName(),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeHelper.Brush("AccentTextFillColorPrimaryBrush"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        textPanel.Children.Add(_appNameBlock);

        _titleBlock = new TextBlock
        {
            Text = "...",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        textPanel.Children.Add(_titleBlock);

        _artistBlock = new TextBlock
        {
            Text = "",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeHelper.Brush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        textPanel.Children.Add(_artistBlock);

        Grid.SetColumn(textPanel, 1);
        infoGrid.Children.Add(textPanel);
        RootElement.Children.Add(infoGrid);

        // Row 1: Timeline
        var timelineGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        timelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        timelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timelineGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(timelineGrid, 1);

        _positionText = new TextBlock
        {
            Text = "0:00",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            MinWidth = 32
        };
        Grid.SetColumn(_positionText, 0);
        timelineGrid.Children.Add(_positionText);

        _timelineSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 24,
            Margin = new Thickness(8, 0, 8, 0),
        };
        _timelineSlider.ValueChanged += async (_, e) =>
        {
            if (_isSeeking) return;
            _isSeeking = true;
            try
            {
                var pos = TimeSpan.FromSeconds(e.NewValue);
                await _session.TryChangePlaybackPositionAsync((long)(pos.TotalMilliseconds * 10000));
                _positionText.Text = FormatTime(pos);
            }
            catch { }
            finally { _isSeeking = false; }
        };
        Grid.SetColumn(_timelineSlider, 1);
        timelineGrid.Children.Add(_timelineSlider);

        _durationText = new TextBlock
        {
            Text = "0:00",
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = ThemeHelper.Brush("TextFillColorTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Right,
            FontSize = 11,
            MinWidth = 32
        };
        Grid.SetColumn(_durationText, 2);
        timelineGrid.Children.Add(_durationText);

        RootElement.Children.Add(timelineGrid);

        // Row 2: Controls
        var controlPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 4,
            Margin = new Thickness(0, 2, 0, 0)
        };
        Grid.SetRow(controlPanel, 2);

        _prevButton = CreateControlButton("\uE892");
        _prevButton.Click += async (_, _) =>
        {
            BlockAndReset();
            try { await _session.TrySkipPreviousAsync(); } catch { }
        };
        controlPanel.Children.Add(_prevButton);

        _playPauseIcon = new FontIcon { Glyph = "\uE768", FontSize = 18, Foreground = new SolidColorBrush(Colors.White) };
        _playPauseButton = new Button
        {
            Content = _playPauseIcon,
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(0),
            Background = ThemeHelper.Brush("AccentFillColorDefaultBrush"),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _playPauseButton.Click += async (_, _) =>
        {
            try { await _session.TryTogglePlayPauseAsync(); } catch { }
        };
        controlPanel.Children.Add(_playPauseButton);

        _nextButton = CreateControlButton("\uE893");
        _nextButton.Click += async (_, _) =>
        {
            BlockAndReset();
            try { await _session.TrySkipNextAsync(); } catch { }
        };
        controlPanel.Children.Add(_nextButton);

        RootElement.Children.Add(controlPanel);

        // Initial populate
        Update();
        UpdateTimeline();
    }

    public void Detach()
    {
        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession session, MediaPropertiesChangedEventArgs? args)
    {
        _dispatcher.TryEnqueue(() =>
        {
            _blockUntil = DateTimeOffset.Now.AddSeconds(3);
            ResetTimelineUi();
            Update();
        });
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession session, PlaybackInfoChangedEventArgs? args)
    {
        _dispatcher.TryEnqueue(UpdatePlaybackInfo);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession session, TimelinePropertiesChangedEventArgs? args)
    {
        _dispatcher.TryEnqueue(UpdateTimeline);
    }

    private string GetAppName()
    {
        try
        {
            var id = _session.SourceAppUserModelId;
            var parts = id.Split('.');
            var name = parts.Length > 0 ? parts[^1] : id;
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            if (name.Length > 0)
                name = char.ToUpper(name[0]) + name[1..];
            return name;
        }
        catch
        {
            return _session.SourceAppUserModelId;
        }
    }

    public async void Update()
    {
        try
        {
            var mediaProps = await _session.TryGetMediaPropertiesAsync();
            if (mediaProps != null)
            {
                _titleBlock.Text = string.IsNullOrEmpty(mediaProps.Title)
                    ? "Unknown" : mediaProps.Title;

                var parts = new List<string>();
                if (!string.IsNullOrEmpty(mediaProps.Artist))
                    parts.Add(mediaProps.Artist);
                if (!string.IsNullOrEmpty(mediaProps.AlbumTitle))
                    parts.Add(mediaProps.AlbumTitle);
                _artistBlock.Text = parts.Count > 0 ? string.Join(" \u00B7 ", parts) : "";

                if (mediaProps.Thumbnail != null)
                {
                    try
                    {
                        var stream = await mediaProps.Thumbnail.OpenReadAsync();
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(stream);
                        _thumbnail.Source = bmp;
                    }
                    catch { }
                }
            }
        }
        catch { }

        UpdatePlaybackInfo();
    }

    private void UpdatePlaybackInfo()
    {
        try
        {
            var playbackInfo = _session.GetPlaybackInfo();
            if (playbackInfo != null)
            {
                var isPlaying = playbackInfo.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                _playPauseIcon.Glyph = isPlaying ? "\uE769" : "\uE768";
                _prevButton.IsEnabled = playbackInfo.Controls.IsPreviousEnabled;
                _nextButton.IsEnabled = playbackInfo.Controls.IsNextEnabled;
            }
        }
        catch { }
    }

    public void UpdateTimeline()
    {
        if (DateTimeOffset.Now < _blockUntil) return;

        try
        {
            var timeline = _session.GetTimelineProperties();
            if (timeline == null) return;

            var duration = timeline.EndTime - timeline.StartTime;
            var position = timeline.Position - timeline.StartTime;

            var playback = _session.GetPlaybackInfo();
            if (playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
                var rate = playback.PlaybackRate ?? 1.0;
                position += TimeSpan.FromSeconds(elapsed.TotalSeconds * rate);
            }

            if (position > duration) position = duration;
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;

            _isSeeking = true;
            if (duration.TotalSeconds > 0)
            {
                _timelineSlider.Maximum = duration.TotalSeconds;
                _timelineSlider.Value = position.TotalSeconds;
                _positionText.Text = FormatTime(position);
                _durationText.Text = FormatTime(duration);
            }
            else
            {
                _timelineSlider.Value = 0;
                _positionText.Text = "0:00";
                _durationText.Text = "0:00";
            }
            _isSeeking = false;
        }
        catch { }
    }

    private void BlockAndReset()
    {
        _blockUntil = DateTimeOffset.Now.AddSeconds(3);
        ResetTimelineUi();
    }

    private void ResetTimelineUi()
    {
        _isSeeking = true;
        _timelineSlider.Value = 0;
        _positionText.Text = "0:00";
        _durationText.Text = "0:00";
        _isSeeking = false;
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private static Button CreateControlButton(string glyph)
    {
        return new Button
        {
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(10, 8, 10, 8),
            Content = new FontIcon { Glyph = glyph, FontSize = 16 }
        };
    }
}
