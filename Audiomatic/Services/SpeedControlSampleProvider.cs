using NAudio.Wave;

namespace Audiomatic.Services;

/// <summary>
/// Sample provider that changes playback speed via linear interpolation.
/// Speed > 1 = faster (consumes more source samples per output frame).
/// Speed &lt; 1 = slower (consumes fewer source samples per output frame).
/// Note: this changes both speed and pitch (like a vinyl speed change).
/// </summary>
public sealed class SpeedControlSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private float _speed = 1.0f;
    private float[] _sourceBuffer = new float[8192];
    private int _sourceBufferCount;
    private double _sourcePosition;

    public SpeedControlSampleProvider(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
    }

    public float Speed
    {
        get => _speed;
        set => _speed = Math.Clamp(value, 0.25f, 4.0f);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Clear internal buffer — call after seeking the source.</summary>
    public void Reset()
    {
        _sourceBufferCount = 0;
        _sourcePosition = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (_speed == 1.0f)
            return _source.Read(buffer, offset, count);

        int outputFrames = count / _channels;
        int written = 0;

        for (int i = 0; i < outputFrames; i++)
        {
            int neededSample = ((int)_sourcePosition + 1) * _channels;
            while (neededSample >= _sourceBufferCount)
            {
                EnsureCapacity(_sourceBufferCount + 4096);
                int read = _source.Read(_sourceBuffer, _sourceBufferCount, 4096);
                if (read == 0)
                    return written;
                _sourceBufferCount += read;
            }

            int srcFrame = (int)_sourcePosition;
            float frac = (float)(_sourcePosition - srcFrame);

            for (int ch = 0; ch < _channels; ch++)
            {
                float s0 = _sourceBuffer[srcFrame * _channels + ch];
                float s1 = _sourceBuffer[(srcFrame + 1) * _channels + ch];
                buffer[offset + written++] = s0 + (s1 - s0) * frac;
            }

            _sourcePosition += _speed;
        }

        // Compact: discard consumed frames
        int consumedFrames = (int)_sourcePosition;
        if (consumedFrames > 0)
        {
            int consumedSamples = consumedFrames * _channels;
            int remaining = _sourceBufferCount - consumedSamples;
            if (remaining > 0)
                Array.Copy(_sourceBuffer, consumedSamples, _sourceBuffer, 0, remaining);
            _sourceBufferCount = remaining;
            _sourcePosition -= consumedFrames;
        }

        return written;
    }

    private void EnsureCapacity(int needed)
    {
        if (_sourceBuffer.Length >= needed) return;
        var newBuf = new float[Math.Max(needed, _sourceBuffer.Length * 2)];
        Array.Copy(_sourceBuffer, newBuf, _sourceBufferCount);
        _sourceBuffer = newBuf;
    }
}
