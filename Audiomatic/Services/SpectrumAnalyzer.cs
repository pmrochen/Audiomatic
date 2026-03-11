using NAudio.Dsp;
using NAudio.Wave;

namespace Audiomatic.Services;

public sealed class SpectrumAnalyzer : IDisposable
{
    private AudioFileReader? _reader;
    private int _sampleRate;
    private int _channels;
    private string? _currentPath;
    private float[] _smoothed = [];
    private volatile bool _decoding;

    private const int FftSize = 4096;
    private static readonly int FftLog2 = (int)Math.Log2(FftSize);
    private const float MinDb = -55f;
    private const float SmoothDown = 0.25f;
    private const float SmoothUp = 0.85f;

    // Pre-allocated buffers — avoid per-frame allocations
    private readonly Complex[] _fftBuffer = new Complex[FftSize];
    private readonly float[] _mags = new float[FftSize / 2];
    private float[] _bands = [];

    // Sliding window cache — ~10 s of mono samples kept in memory
    // avoids per-frame disk seeks on compressed formats
    private const int CacheSeconds = 10;
    private float[]? _cache;
    private long _cacheStartSample; // first mono sample index in cache
    private int _cacheSampleCount;  // valid mono samples in cache

    /// <summary>
    /// Opens an AudioFileReader for on-demand decoding.
    /// Only a sliding window (~10 s) is kept in memory.
    /// </summary>
    public async Task PrepareAsync(string filePath)
    {
        if (filePath == _currentPath && _reader != null) return;
        _currentPath = filePath;
        _reader?.Dispose();
        _reader = null;
        _cache = null;
        _cacheSampleCount = 0;
        _decoding = true;

        var reader = await Task.Run(() => new AudioFileReader(filePath));

        // Only apply if still the same track
        if (_currentPath == filePath)
        {
            _reader = reader;
            _sampleRate = reader.WaveFormat.SampleRate;
            _channels = reader.WaveFormat.Channels;
            _cache = new float[_sampleRate * CacheSeconds];
            _cacheStartSample = -1;
            _cacheSampleCount = 0;
        }
        else
        {
            reader.Dispose();
        }
        _decoding = false;
    }

    /// <summary>
    /// Fills the sliding cache so it covers [sampleOffset .. sampleOffset + FftSize).
    /// Only reads from disk when the requested window falls outside the cache.
    /// </summary>
    private bool EnsureCached(long sampleOffset)
    {
        if (_reader == null || _cache == null) return false;

        long cacheEnd = _cacheStartSample + _cacheSampleCount;
        bool hit = _cacheStartSample >= 0
                   && sampleOffset >= _cacheStartSample
                   && sampleOffset + FftSize <= cacheEnd;
        if (hit) return true;

        // Cache miss — read a full window centered on sampleOffset
        long totalMonoSamples = _reader.Length / (_channels * sizeof(float));
        long start = Math.Max(0, sampleOffset - _sampleRate); // 1 s before
        long end = Math.Min(totalMonoSamples, start + _cache.Length);
        if (end - start < FftSize) return false;

        long bytePos = start * _channels * sizeof(float);
        try
        {
            _reader.Position = bytePos;
        }
        catch { return false; }

        int monoToRead = (int)(end - start);
        int interleavedToRead = monoToRead * _channels;

        // Temporary interleaved read buffer (stack-friendly size check)
        var interleaved = new float[interleavedToRead];
        int read = _reader.Read(interleaved, 0, interleavedToRead);
        int monoRead = read / _channels;

        // Down-mix to mono into cache
        for (int i = 0; i < monoRead; i++)
        {
            float s = 0;
            int b = i * _channels;
            for (int c = 0; c < _channels; c++)
                s += interleaved[b + c];
            _cache[i] = s / _channels;
        }

        _cacheStartSample = start;
        _cacheSampleCount = monoRead;
        return sampleOffset >= start && sampleOffset + FftSize <= start + monoRead;
    }

    /// <summary>
    /// Returns frequency band magnitudes (0..1) at the given playback position.
    /// </summary>
    public float[] GetSpectrum(TimeSpan position, int bandCount)
    {
        if (_reader == null || _sampleRate == 0 || _cache == null)
            return Decay(bandCount);

        long sampleOffset = (long)(position.TotalSeconds * _sampleRate);
        if (sampleOffset < 0) sampleOffset = 0;

        if (!EnsureCached(sampleOffset))
            return Decay(bandCount);

        // Index into cache
        int cacheIdx = (int)(sampleOffset - _cacheStartSample);

        // Hamming window + FFT (reusable buffer)
        for (int i = 0; i < FftSize; i++)
        {
            float window = 0.54f - 0.46f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
            _fftBuffer[i].X = _cache[cacheIdx + i] * window;
            _fftBuffer[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

        // Magnitude spectrum — reusable buffer
        int half = FftSize / 2;
        for (int i = 0; i < half; i++)
            _mags[i] = MathF.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);

        // Logarithmic frequency bands
        float freqPerBin = (float)_sampleRate / FftSize;
        const float minFreq = 30f;
        float maxFreq = MathF.Min(_sampleRate / 2f, 18000f);
        float logMin = MathF.Log2(minFreq);
        float logMax = MathF.Log2(maxFreq);

        if (_bands.Length != bandCount)
            _bands = new float[bandCount];

        for (int b = 0; b < bandCount; b++)
        {
            float lo = MathF.Pow(2, logMin + (logMax - logMin) * b / bandCount);
            float hi = MathF.Pow(2, logMin + (logMax - logMin) * (b + 1) / bandCount);
            int loBin = Math.Max(1, (int)(lo / freqPerBin));
            int hiBin = Math.Min(half - 1, (int)(hi / freqPerBin));

            float sum = 0;
            int cnt = 0;
            for (int i = loBin; i <= hiBin; i++) { sum += _mags[i]; cnt++; }
            float avg = cnt > 0 ? sum / cnt : 0;

            float db = avg > 0 ? 20f * MathF.Log10(avg) : MinDb;
            _bands[b] = Math.Clamp((db - MinDb) / -MinDb, 0f, 1f);
        }

        // Smooth
        if (_smoothed.Length != bandCount)
            _smoothed = new float[bandCount];
        for (int i = 0; i < bandCount; i++)
        {
            float factor = _bands[i] > _smoothed[i] ? SmoothUp : SmoothDown;
            _smoothed[i] += (_bands[i] - _smoothed[i]) * factor;
        }
        return _smoothed;
    }

    public bool IsReady => _reader != null;
    public bool IsDecoding => _decoding;

    private float[] Decay(int bandCount)
    {
        if (_smoothed.Length != bandCount)
            _smoothed = new float[bandCount];
        for (int i = 0; i < _smoothed.Length; i++)
            _smoothed[i] *= 0.85f;
        return _smoothed;
    }

    public void Reset()
    {
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
        _cache = null;
        _cacheSampleCount = 0;
        _smoothed = [];
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
        _cache = null;
    }
}
