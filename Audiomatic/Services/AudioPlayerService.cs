using Audiomatic.Models;
using Audiomatic;
using NAudio.Wave;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Audiomatic.Services;

public enum RepeatMode { None, All, One }

public sealed class AudioPlayerService : IDisposable
{
    private readonly MediaPlayer _mediaPlayer = new();
    private IWavePlayer? _waveOut;
    private AudioFileReader? _audioReader;
    private bool _useNAudio;
    private bool _isMuted;
    private double _volume = 1.0;
    private InMemoryRandomAccessStream? _albumArtStream;

    // Equalizer
    private Equalizer? _equalizer;
    private float[] _eqGains = new float[10];
    private bool _eqEnabled = true;
    private float _eqPreampDb;

    // Speed control
    private SpeedControlSampleProvider? _speedProvider;
    private float _playbackSpeed = 1.0f;

    // Gapless playback
    private GaplessSampleProvider? _gaplessProvider;
    private AudioFileReader? _nextAudioReader;
    private Equalizer? _nextEqualizer;
    private SpeedControlSampleProvider? _nextSpeedProvider;
    private TrackInfo? _nextTrack;
    private bool _gaplessTransitioning;
    private TimeSpan _nextTrueDuration;

    // Accurate duration via Media Foundation (NAudio's AudioFileReader can be off by several seconds)
    private TimeSpan _trueDuration;

    // NAudio-supported but not natively by MediaPlayer
    private static readonly HashSet<string> NAudioOnlyExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".ape", ".aiff" };

    public event Action? PlaybackStarted;
    public event Action? PlaybackPaused;
    public event Action? PlaybackStopped;
    public event Action? MediaEnded;
    public event Action? MediaOpened;
    public event Action<string>? MediaFailed;
    public event Action<TimeSpan>? PositionChanged;
    public event Action<bool>? BufferingChanged;
    /// <summary>Fired when gapless transition occurs — the next track started seamlessly.</summary>
    public event Action<TrackInfo>? GaplessTransitioned;

    public TrackInfo? CurrentTrack { get; private set; }
    public bool IsPlaying { get; private set; }
    public TimeSpan Position
    {
        get
        {
            if (_useNAudio && _audioReader != null)
                return _audioReader.CurrentTime;
            return _mediaPlayer.PlaybackSession.Position;
        }
    }
    public TimeSpan Duration
    {
        get
        {
            if (_useNAudio)
                return _trueDuration > TimeSpan.Zero ? _trueDuration : (_audioReader?.TotalTime ?? TimeSpan.Zero);
            return _mediaPlayer.NaturalDuration;
        }
    }
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            _mediaPlayer.Volume = _volume;
            if (_audioReader != null)
                _audioReader.Volume = _isMuted ? 0f : (float)_volume;
        }
    }
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            _mediaPlayer.IsMuted = value;
            if (_audioReader != null)
                _audioReader.Volume = value ? 0f : (float)_volume;
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private System.Threading.Timer? _positionTimer;
    private bool _mediaEndedFired;

    public AudioPlayerService()
    {
        // Disable automatic command handling so other apps (YouTube, etc.)
        // cannot pause our playback through system media transport commands.
        _mediaPlayer.CommandManager.IsEnabled = false;

        _mediaPlayer.MediaEnded += (_, _) =>
        {
            if (!_useNAudio)
            {
                IsPlaying = false;
                _dispatcherQueue?.TryEnqueue(() => MediaEnded?.Invoke());
            }
        };
        _mediaPlayer.MediaOpened += (_, _) =>
        {
            if (!_useNAudio)
                _dispatcherQueue?.TryEnqueue(() => MediaOpened?.Invoke());
        };
        _mediaPlayer.MediaFailed += (_, args) =>
        {
            if (!_useNAudio)
            {
                IsPlaying = false;
                _dispatcherQueue?.TryEnqueue(() => MediaFailed?.Invoke(args.ErrorMessage));
            }
        };

        _positionTimer = new System.Threading.Timer(_ =>
        {
            if (IsPlaying)
                _dispatcherQueue?.TryEnqueue(() => PositionChanged?.Invoke(Position));
        }, null, 0, 250);

        var smtc = _mediaPlayer.SystemMediaTransportControls;
        smtc.ButtonPressed += (_, args) =>
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        Play();
                        smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        Pause();
                        smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                        break;
                }
            });
        };
    }

    public void SetDispatcherQueue(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        _dispatcherQueue = queue;
    }

    public async Task PlayTrackAsync(TrackInfo track)
    {
        Stop();
        CurrentTrack = track;
        _mediaEndedFired = false;

        // Always use NAudio for file playback (enables equalizer)
        _useNAudio = true;
        try
        {
            _audioReader = new AudioFileReader(track.Path);
            _trueDuration = TrackDurationHelper.ResolveDuration(track.Path, _audioReader.TotalTime);

            _equalizer = new Equalizer(_audioReader);
            _equalizer.Enabled = _eqEnabled;
            _equalizer.SetAllBands(_eqGains);
            _equalizer.Preamp = DbToLinear(_eqPreampDb);

            _audioReader.Volume = _isMuted ? 0f : (float)_volume;
            _speedProvider = new SpeedControlSampleProvider(_equalizer) { Speed = _playbackSpeed };

            // Wrap in gapless provider
            _gaplessProvider = new GaplessSampleProvider(_speedProvider);
            _gaplessProvider.SourceTransitioned += OnGaplessTransition;
            _gaplessProvider.PlaybackEnded += () =>
            {
                if (_mediaEndedFired) return;
                _mediaEndedFired = true;
                IsPlaying = false;
                _dispatcherQueue?.TryEnqueue(() => MediaEnded?.Invoke());
            };

            _waveOut = new WasapiOut();
            _waveOut.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(_gaplessProvider));
            _waveOut.PlaybackStopped += (_, _) =>
            {
                if (_mediaEndedFired || _gaplessTransitioning) return;
                if (_audioReader != null
                    && _audioReader.CurrentTime >= _audioReader.TotalTime - TimeSpan.FromMilliseconds(500))
                {
                    _mediaEndedFired = true;
                    IsPlaying = false;
                    _dispatcherQueue?.TryEnqueue(() => MediaEnded?.Invoke());
                }
            };
            _waveOut.Play();
            IsPlaying = true;
            UpdateSmtc(track);
            _dispatcherQueue?.TryEnqueue(() =>
            {
                MediaOpened?.Invoke();
                PlaybackStarted?.Invoke();
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue?.TryEnqueue(() => MediaFailed?.Invoke(ex.Message));
        }
    }

    /// <summary>
    /// Pre-build the audio chain for the next track so it can transition gaplessly.
    /// Call this when the current track is nearing its end.
    /// </summary>
    public void PrepareNextTrack(TrackInfo track)
    {
        if (_gaplessProvider == null || !_useNAudio) return;
        if (_gaplessProvider.HasNext) return; // already prepared

        try
        {
            DisposeNextChain();
            _nextAudioReader = new AudioFileReader(track.Path);

            // Check format compatibility
            if (_audioReader != null &&
                (_nextAudioReader.WaveFormat.SampleRate != _audioReader.WaveFormat.SampleRate ||
                 _nextAudioReader.WaveFormat.Channels != _audioReader.WaveFormat.Channels))
            {
                // Incompatible formats — can't do gapless, will fall back to normal transition
                DisposeNextChain();
                return;
            }

            _nextEqualizer = new Equalizer(_nextAudioReader);
            _nextEqualizer.Enabled = _eqEnabled;
            _nextEqualizer.SetAllBands(_eqGains);
            _nextEqualizer.Preamp = DbToLinear(_eqPreampDb);

            _nextAudioReader.Volume = _isMuted ? 0f : (float)_volume;
            _nextSpeedProvider = new SpeedControlSampleProvider(_nextEqualizer) { Speed = _playbackSpeed };

            _nextTrueDuration = TrackDurationHelper.ResolveDuration(track.Path, _nextAudioReader.TotalTime);

            _nextTrack = track;
            _gaplessProvider.QueueNext(_nextSpeedProvider);
        }
        catch
        {
            DisposeNextChain();
        }
    }

    private void OnGaplessTransition()
    {
        _gaplessTransitioning = true;

        // Dispose old chain
        var oldReader = _audioReader;
        var oldEqualizer = _equalizer;

        // Promote next chain to current
        _audioReader = _nextAudioReader;
        _equalizer = _nextEqualizer;
        _speedProvider = _nextSpeedProvider;
        _trueDuration = _nextTrueDuration;
        _nextAudioReader = null;
        _nextEqualizer = null;
        _nextSpeedProvider = null;
        _nextTrueDuration = TimeSpan.Zero;

        var transitionedTrack = _nextTrack;
        _nextTrack = null;
        CurrentTrack = transitionedTrack;

        // Dispose old reader on background thread
        Task.Run(() =>
        {
            try { oldReader?.Dispose(); } catch { }
        });

        if (transitionedTrack != null)
        {
            UpdateSmtc(transitionedTrack);
            _dispatcherQueue?.TryEnqueue(() =>
            {
                GaplessTransitioned?.Invoke(transitionedTrack);
                MediaOpened?.Invoke();
            });
        }

        _gaplessTransitioning = false;
    }

    private void DisposeNextChain()
    {
        try { _nextAudioReader?.Dispose(); } catch { }
        _nextAudioReader = null;
        _nextEqualizer = null;
        _nextSpeedProvider = null;
        _nextTrack = null;
        _nextTrueDuration = TimeSpan.Zero;
    }

    /// <summary>
    /// Returns how many seconds remain in the current track.
    /// Returns -1 if not applicable (stream, no track).
    /// </summary>
    public double RemainingSeconds
    {
        get
        {
            if (!_useNAudio || _audioReader == null) return -1;
            var total = _trueDuration > TimeSpan.Zero ? _trueDuration : _audioReader.TotalTime;
            return (total - _audioReader.CurrentTime).TotalSeconds / _playbackSpeed;
        }
    }

    public bool IsStream { get; private set; }

    public async Task PlayStreamAsync(Uri streamUri)
    {
        Stop();
        IsStream = true;
        _useNAudio = false;
        CurrentTrack = null;

        try
        {
            var source = MediaSource.CreateFromUri(streamUri);
            var item = new MediaPlaybackItem(source);

            _mediaPlayer.Source = item;

            // Wait for the source to open before playing to let MediaPlayer buffer
            var tcs = new TaskCompletionSource<bool>();
            void onOpened(MediaPlayer mp, object args)
            {
                _mediaPlayer.MediaOpened -= onOpened;
                tcs.TrySetResult(true);
            }
            void onFailed(MediaPlayer mp, MediaPlayerFailedEventArgs args)
            {
                _mediaPlayer.MediaFailed -= onFailed;
                tcs.TrySetException(new Exception(args.ErrorMessage));
            }
            _mediaPlayer.MediaOpened += onOpened;
            _mediaPlayer.MediaFailed += onFailed;

            // Subscribe to buffering events
            var session = _mediaPlayer.PlaybackSession;
            session.BufferingStarted += (_, _) =>
                _dispatcherQueue?.TryEnqueue(() => BufferingChanged?.Invoke(true));
            session.BufferingEnded += (_, _) =>
                _dispatcherQueue?.TryEnqueue(() => BufferingChanged?.Invoke(false));

            _mediaPlayer.Volume = _volume;
            _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
            _mediaPlayer.Play();

            // Wait up to 15s for the stream to open
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            cts.Token.Register(() => tcs.TrySetException(new TimeoutException("Stream connection timed out")));
            await tcs.Task;

            IsPlaying = true;

            var smtc = _mediaPlayer.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

            var updater = smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = "Radio Stream";
            updater.MusicProperties.Artist = streamUri.Host;
            updater.Update();

            _dispatcherQueue?.TryEnqueue(() =>
            {
                PlaybackStarted?.Invoke();
            });
        }
        catch (Exception ex)
        {
            IsStream = false;
            _dispatcherQueue?.TryEnqueue(() => MediaFailed?.Invoke(ex.Message));
            throw;
        }
    }

    public void Play()
    {
        if (_useNAudio)
        {
            _waveOut?.Play();
        }
        else
        {
            _mediaPlayer.Play();
        }
        IsPlaying = true;
        PlaybackStarted?.Invoke();
    }

    public void Pause()
    {
        if (_useNAudio)
        {
            _waveOut?.Pause();
        }
        else
        {
            _mediaPlayer.Pause();
        }
        IsPlaying = false;
        PlaybackPaused?.Invoke();
    }

    public void Stop()
    {
        _mediaEndedFired = true; // Prevent spurious MediaEnded during teardown
        if (_useNAudio)
        {
            _gaplessProvider?.ClearNext();
            _gaplessProvider = null;
            DisposeNextChain();
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _audioReader?.Dispose();
            _audioReader = null;
        }
        else
        {
            _mediaPlayer.Pause();
            _mediaPlayer.Source = null;
        }
        IsPlaying = false;
        IsStream = false;
        _gaplessTransitioning = false;
        _trueDuration = TimeSpan.Zero;
        PlaybackStopped?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_useNAudio && _audioReader != null)
        {
            _speedProvider?.Reset();
            _audioReader.CurrentTime = position;
        }
        else
        {
            _mediaPlayer.PlaybackSession.Position = position;
        }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    private static readonly string[] CoverFileNames = { "cover", "folder", "album", "front", "artwork" };
    private static readonly string[] CoverExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

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

    private void UpdateSmtc(TrackInfo track)
    {
        try
        {
            var smtc = _mediaPlayer.SystemMediaTransportControls;
            smtc.IsEnabled = true;
            smtc.IsPlayEnabled = true;
            smtc.IsPauseEnabled = true;
            smtc.IsNextEnabled = true;
            smtc.IsPreviousEnabled = true;
            smtc.PlaybackStatus = MediaPlaybackStatus.Playing;

            var updater = smtc.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.Artist = track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album;
            updater.Update();
        }
        catch { }
    }

    /// <summary>
    /// Sets SMTC thumbnail from artwork data already read by the caller.
    /// Avoids a duplicate TagLib read.
    /// </summary>
    public void UpdateSmtcArtwork(byte[]? embeddedArtData, string? coverFilePath)
    {
        try
        {
            var updater = _mediaPlayer.SystemMediaTransportControls.DisplayUpdater;

            if (embeddedArtData != null)
            {
                var newStream = new InMemoryRandomAccessStream();
                var writer = new DataWriter(newStream.GetOutputStreamAt(0));
                writer.WriteBytes(embeddedArtData);
                writer.StoreAsync().GetResults();
                updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(newStream);

                _albumArtStream?.Dispose();
                _albumArtStream = newStream;
            }
            else if (coverFilePath != null)
            {
                var uri = new Uri(coverFilePath);
                updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(uri);
            }

            updater.Update();
        }
        catch { }
    }

    // -- Equalizer control --

    public bool EqEnabled
    {
        get => _eqEnabled;
        set
        {
            _eqEnabled = value;
            if (_equalizer != null) _equalizer.Enabled = value;
        }
    }

    public void SetEqBand(int index, float gainDb)
    {
        if (index >= 0 && index < _eqGains.Length)
            _eqGains[index] = Math.Clamp(gainDb, -12f, 12f);
        _equalizer?.SetBand(index, gainDb);
    }

    public void SetEqAllBands(float[] gains)
    {
        for (int i = 0; i < Math.Min(gains.Length, _eqGains.Length); i++)
            _eqGains[i] = Math.Clamp(gains[i], -12f, 12f);
        _equalizer?.SetAllBands(_eqGains);
    }

    public float[] GetEqGains() => (float[])_eqGains.Clone();

    public void SetEqPreamp(float db)
    {
        _eqPreampDb = db;
        if (_equalizer != null) _equalizer.Preamp = DbToLinear(db);
    }

    public float EqPreampDb => _eqPreampDb;

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

    // -- Speed control --

    public float PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = Math.Clamp(value, 0.25f, 4.0f);
            if (_speedProvider != null)
                _speedProvider.Speed = _playbackSpeed;
            if (!_useNAudio)
                _mediaPlayer.PlaybackSession.PlaybackRate = _playbackSpeed;
        }
    }

    public void SuspendPositionTimer()
    {
        _positionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void ResumePositionTimer()
    {
        _positionTimer?.Change(0, 250);
    }

    public void Dispose()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
        Stop();
        DisposeNextChain();
        _albumArtStream?.Dispose();
        _albumArtStream = null;
        _mediaPlayer.Dispose();
    }
}
