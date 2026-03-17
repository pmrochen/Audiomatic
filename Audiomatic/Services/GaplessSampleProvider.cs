using NAudio.Wave;

namespace Audiomatic.Services;

/// <summary>
/// Sample provider that enables gapless playback by seamlessly switching
/// from the current source to a pre-queued next source when the current one ends.
/// </summary>
public sealed class GaplessSampleProvider : ISampleProvider
{
    private ISampleProvider? _current;
    private ISampleProvider? _next;
    private readonly object _lock = new();
    private bool _currentEnded;

    public WaveFormat WaveFormat { get; }

    /// <summary>Fired on the audio thread when the current source ends and a next source takes over.</summary>
    public event Action? SourceTransitioned;

    /// <summary>Fired on the audio thread when the current source ends and no next source is queued.</summary>
    public event Action? PlaybackEnded;

    public GaplessSampleProvider(ISampleProvider initial)
    {
        _current = initial;
        WaveFormat = initial.WaveFormat;
    }

    /// <summary>
    /// Queue the next source for gapless transition.
    /// Must have the same WaveFormat as the current source.
    /// Returns false if formats don't match.
    /// </summary>
    public bool QueueNext(ISampleProvider next)
    {
        if (next.WaveFormat.SampleRate != WaveFormat.SampleRate ||
            next.WaveFormat.Channels != WaveFormat.Channels)
            return false;

        lock (_lock)
        {
            _next = next;
        }
        return true;
    }

    /// <summary>Check if a next source is already queued.</summary>
    public bool HasNext
    {
        get { lock (_lock) { return _next != null; } }
    }

    /// <summary>Clear the queued next source (e.g., when user skips manually).</summary>
    public void ClearNext()
    {
        lock (_lock) { _next = null; }
    }

    /// <summary>Replace the current source entirely (for manual track changes).</summary>
    public void SetCurrent(ISampleProvider source)
    {
        lock (_lock)
        {
            _current = source;
            _next = null;
            _currentEnded = false;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            if (_current == null) return 0;

            var read = _current.Read(buffer, offset, count);

            if (read < count)
            {
                // Current source ended — try to switch to next
                if (_next != null)
                {
                    _current = _next;
                    _next = null;
                    _currentEnded = false;

                    // Fill remaining buffer from the new source
                    var remaining = count - read;
                    var nextRead = _current.Read(buffer, offset + read, remaining);
                    read += nextRead;

                    // Fire transition event (on audio thread — keep it fast)
                    SourceTransitioned?.Invoke();
                }
                else if (!_currentEnded && read == 0)
                {
                    _currentEnded = true;
                    PlaybackEnded?.Invoke();
                }
            }

            return read;
        }
    }
}
