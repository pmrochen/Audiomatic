using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Audiomatic.Services;

public sealed class SpectrumAnalyzer : IDisposable
{
    private float[]? _decoded;
    private int _sampleRate;
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

    // Loopback capture for live streams
    private WasapiLoopbackCapture? _loopback;
    private float[] _loopbackRing = new float[FftSize * 2];
    private int _loopbackWritePos;
    private int _loopbackSampleRate;
    private volatile bool _loopbackActive;
    private readonly object _loopbackLock = new();

    /// <summary>
    /// Pre-decodes the audio file to a mono float array in memory.
    /// Fast random access afterward — no file seeking needed.
    /// </summary>
    public async Task PrepareAsync(string filePath)
    {
        StopLoopback();

        if (filePath == _currentPath && _decoded != null) return;
        _currentPath = filePath;
        _decoded = null;
        _decoding = true;

        var (samples, rate) = await Task.Run(() =>
        {
            using var reader = new AudioFileReader(filePath);
            int sr = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            // Estimate total mono samples for pre-allocation
            long estimatedMono = reader.Length / (reader.WaveFormat.BitsPerSample / 8) / channels;
            var mono = new List<float>((int)Math.Min(estimatedMono, int.MaxValue));

            var buf = new float[8192];
            int read;
            while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            {
                for (int i = 0; i < read; i += channels)
                {
                    float s = 0;
                    for (int c = 0; c < channels && i + c < read; c++)
                        s += buf[i + c];
                    mono.Add(s / channels);
                }
            }
            return (mono.ToArray(), sr);
        });

        // Only apply if still the same track
        if (_currentPath == filePath)
        {
            _decoded = samples;
            _sampleRate = rate;
        }
        _decoding = false;
    }

    /// <summary>
    /// Starts WASAPI loopback capture for real-time spectrum of streams.
    /// </summary>
    public void StartLoopback()
    {
        if (_loopbackActive) return;

        _decoded = null;
        _currentPath = null;

        try
        {
            _loopback = new WasapiLoopbackCapture();
            _loopbackSampleRate = _loopback.WaveFormat.SampleRate;
            _loopbackRing = new float[FftSize * 2];
            _loopbackWritePos = 0;

            _loopback.DataAvailable += (_, args) =>
            {
                int channels = _loopback.WaveFormat.Channels;
                int bytesPerSample = _loopback.WaveFormat.BitsPerSample / 8;
                int sampleCount = args.BytesRecorded / bytesPerSample;

                lock (_loopbackLock)
                {
                    for (int i = 0; i < sampleCount; i += channels)
                    {
                        float sample = 0;
                        for (int c = 0; c < channels && (i + c) * bytesPerSample + bytesPerSample <= args.BytesRecorded; c++)
                        {
                            int offset = (i + c) * bytesPerSample;
                            sample += BitConverter.ToSingle(args.Buffer, offset);
                        }
                        sample /= channels;
                        _loopbackRing[_loopbackWritePos % _loopbackRing.Length] = sample;
                        _loopbackWritePos++;
                    }
                }
            };

            _loopback.StartRecording();
            _loopbackActive = true;
        }
        catch
        {
            _loopback?.Dispose();
            _loopback = null;
        }
    }

    public void StopLoopback()
    {
        if (!_loopbackActive) return;
        _loopbackActive = false;

        try
        {
            _loopback?.StopRecording();
            _loopback?.Dispose();
        }
        catch { }
        _loopback = null;
    }

    /// <summary>
    /// Returns frequency band magnitudes (0..1) at the given playback position.
    /// For file-based tracks uses pre-decoded data; for streams uses loopback capture.
    /// </summary>
    public float[] GetSpectrum(TimeSpan position, int bandCount)
    {
        if (_loopbackActive)
            return GetLoopbackSpectrum(bandCount);

        if (_decoded == null || _sampleRate == 0)
            return Decay(bandCount);

        int start = (int)(position.TotalSeconds * _sampleRate);
        if (start < 0) start = 0;
        if (start + FftSize > _decoded.Length)
            return Decay(bandCount);

        // Hamming window + FFT — reusable buffers
        for (int i = 0; i < FftSize; i++)
        {
            float window = 0.54f - 0.46f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
            _fftBuffer[i].X = _decoded[start + i] * window;
            _fftBuffer[i].Y = 0;
        }

        return ComputeBands(bandCount, _sampleRate);
    }

    private float[] GetLoopbackSpectrum(int bandCount)
    {
        lock (_loopbackLock)
        {
            if (_loopbackWritePos < FftSize)
                return Decay(bandCount);

            int ringLen = _loopbackRing.Length;
            int readStart = _loopbackWritePos - FftSize;

            for (int i = 0; i < FftSize; i++)
            {
                int idx = (readStart + i) % ringLen;
                if (idx < 0) idx += ringLen;
                float window = 0.54f - 0.46f * MathF.Cos(2f * MathF.PI * i / (FftSize - 1));
                _fftBuffer[i].X = _loopbackRing[idx] * window;
                _fftBuffer[i].Y = 0;
            }
        }

        return ComputeBands(bandCount, _loopbackSampleRate);
    }

    private float[] ComputeBands(int bandCount, int sampleRate)
    {
        FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

        // Magnitude spectrum — reusable buffer
        int half = FftSize / 2;
        for (int i = 0; i < half; i++)
            _mags[i] = MathF.Sqrt(_fftBuffer[i].X * _fftBuffer[i].X + _fftBuffer[i].Y * _fftBuffer[i].Y);

        // Logarithmic frequency bands
        float freqPerBin = (float)sampleRate / FftSize;
        const float minFreq = 30f;
        float maxFreq = MathF.Min(sampleRate / 2f, 18000f);
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

    public bool IsReady => _decoded != null || _loopbackActive;
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
        StopLoopback();
        _decoded = null;
        _currentPath = null;
        _smoothed = [];
    }

    public void Dispose()
    {
        StopLoopback();
        _decoded = null;
        _currentPath = null;
    }
}
