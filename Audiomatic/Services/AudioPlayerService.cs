using Audiomatic.Models;
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
    private InMemoryRandomAccessStream? _albumArtStream;

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
            if (_useNAudio && _audioReader != null)
                return _audioReader.TotalTime;
            return _mediaPlayer.NaturalDuration;
        }
    }
    public double Volume
    {
        get => _mediaPlayer.Volume;
        set
        {
            _mediaPlayer.Volume = Math.Clamp(value, 0, 1);
            if (_waveOut != null)
                _waveOut.Volume = (float)Math.Clamp(value, 0, 1);
        }
    }
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            _mediaPlayer.IsMuted = value;
            if (_waveOut != null)
                _waveOut.Volume = value ? 0f : (float)Math.Clamp(_mediaPlayer.Volume, 0, 1);
        }
    }

    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcherQueue;
    private System.Threading.Timer? _positionTimer;

    public AudioPlayerService()
    {
        _mediaPlayer.MediaEnded += (_, _) =>
        {
            IsPlaying = false;
            _dispatcherQueue?.TryEnqueue(() => MediaEnded?.Invoke());
        };
        _mediaPlayer.MediaOpened += (_, _) =>
        {
            _dispatcherQueue?.TryEnqueue(() => MediaOpened?.Invoke());
        };
        _mediaPlayer.MediaFailed += (_, args) =>
        {
            IsPlaying = false;
            _dispatcherQueue?.TryEnqueue(() => MediaFailed?.Invoke(args.ErrorMessage));
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

        var ext = Path.GetExtension(track.Path).ToLowerInvariant();

        if (NAudioOnlyExtensions.Contains(ext))
        {
            // Use NAudio for formats MediaPlayer can't handle
            _useNAudio = true;
            try
            {
                _audioReader = new AudioFileReader(track.Path);
                _waveOut = new WasapiOut();
                _waveOut.Init(_audioReader);
                _waveOut.Volume = _isMuted ? 0f : (float)Math.Clamp(_mediaPlayer.Volume, 0, 1);
                _waveOut.PlaybackStopped += (_, _) =>
                {
                    if (_audioReader != null && _audioReader.CurrentTime >= _audioReader.TotalTime - TimeSpan.FromMilliseconds(500))
                    {
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
        else
        {
            // Use MediaPlayer (handles mp3, flac, wav, ogg, aac, wma, m4a, opus)
            _useNAudio = false;
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(track.Path);
                _mediaPlayer.Source = MediaSource.CreateFromStorageFile(file);
                _mediaPlayer.Volume = Volume;
                _mediaPlayer.Play();
                IsPlaying = true;
                UpdateSmtc(track);
                _dispatcherQueue?.TryEnqueue(() => PlaybackStarted?.Invoke());
            }
            catch (Exception ex)
            {
                _dispatcherQueue?.TryEnqueue(() => MediaFailed?.Invoke(ex.Message));
            }
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
        if (_useNAudio)
        {
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
        PlaybackStopped?.Invoke();
    }

    public void Seek(TimeSpan position)
    {
        if (_useNAudio && _audioReader != null)
        {
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

            // Try to set album art from embedded tag
            try
            {
                using var tagFile = TagLib.File.Create(track.Path);
                if (tagFile.Tag.Pictures.Length > 0)
                {
                    var pic = tagFile.Tag.Pictures[0];
                    var newStream = new InMemoryRandomAccessStream();
                    var writer = new DataWriter(newStream.GetOutputStreamAt(0));
                    writer.WriteBytes(pic.Data.Data);
                    writer.StoreAsync().GetResults();
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromStream(newStream);

                    _albumArtStream?.Dispose();
                    _albumArtStream = newStream;
                }
            }
            catch { }

            updater.Update();
        }
        catch { }
    }

    public void Dispose()
    {
        _positionTimer?.Dispose();
        _positionTimer = null;
        Stop();
        _albumArtStream?.Dispose();
        _albumArtStream = null;
        _mediaPlayer.Dispose();
    }
}
