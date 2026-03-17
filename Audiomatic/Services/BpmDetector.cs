using NAudio.Wave;

namespace Audiomatic.Services;

/// <summary>
/// Detects BPM from audio files using multi-band spectral flux onset detection
/// combined with comb-filter tempo estimation.
/// </summary>
public static class BpmDetector
{
    private const int AnalysisSeconds = 30;
    private const int MinBpm = 60;
    private const int MaxBpm = 200;

    /// <summary>
    /// Analyze the audio file and return estimated BPM (0 if detection fails).
    /// </summary>
    public static int Detect(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels = reader.WaveFormat.Channels;

            // Skip first 20% to avoid intros, analyze up to 30s
            var totalSeconds = reader.TotalTime.TotalSeconds;
            var startSec = totalSeconds * 0.2;
            var analysisSec = Math.Min(AnalysisSeconds, totalSeconds - startSec);
            if (analysisSec < 4) return 0;

            // Seek to start
            reader.CurrentTime = TimeSpan.FromSeconds(startSec);

            var samplesToRead = (int)(analysisSec * sampleRate) * channels;
            var mono = ReadMono(reader, samplesToRead, channels);
            if (mono.Length < sampleRate * 4) return 0;

            // Low-pass filter to isolate bass/kick (< 200Hz) — most reliable for tempo
            var bassSignal = LowPassFilter(mono, sampleRate, 200f);

            // Also keep a mid-range signal (200-5000Hz) for snare/hi-hat detection
            var midSignal = BandPassFilter(mono, sampleRate, 200f, 5000f);

            // Compute onset strength envelopes (~86 Hz resolution = ~11.6ms windows)
            var hopSize = sampleRate / 86;
            var bassOnsets = ComputeOnsetEnvelope(bassSignal, hopSize);
            var midOnsets = ComputeOnsetEnvelope(midSignal, hopSize);

            // Combine: bass weighted 2x more than mid
            var onsetRate = (float)sampleRate / hopSize;
            var combined = new float[Math.Min(bassOnsets.Length, midOnsets.Length)];
            for (int i = 0; i < combined.Length; i++)
                combined[i] = bassOnsets[i] * 2f + midOnsets[i];

            // Adaptive threshold — suppress low-energy onsets
            AdaptiveThreshold(combined, (int)(onsetRate * 0.3f));

            // Comb filter BPM estimation
            var bestBpm = CombFilterEstimate(combined, onsetRate, MinBpm, MaxBpm);

            // Verify with autocorrelation
            var autoBpm = AutocorrelationEstimate(combined, onsetRate, MinBpm, MaxBpm);

            // If both methods agree within 8%, use the average; otherwise prefer comb filter
            if (bestBpm > 0 && autoBpm > 0)
            {
                var ratio = (float)Math.Max(bestBpm, autoBpm) / Math.Min(bestBpm, autoBpm);
                if (ratio < 1.08f)
                    bestBpm = (int)Math.Round((bestBpm + autoBpm) / 2.0);
                // Check if one is a harmonic of the other
                else if (Math.Abs(bestBpm - autoBpm * 2) < 8)
                    bestBpm = autoBpm * 2 > MaxBpm ? autoBpm : autoBpm * 2;
                else if (Math.Abs(autoBpm - bestBpm * 2) < 8)
                    bestBpm = bestBpm * 2 > MaxBpm ? bestBpm : bestBpm * 2;
            }

            // Normalize to common range
            while (bestBpm > MaxBpm) bestBpm /= 2;
            while (bestBpm > 0 && bestBpm < MinBpm) bestBpm *= 2;

            return bestBpm;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Comb filter tempo estimation — peaks at the correct BPM.</summary>
    private static int CombFilterEstimate(float[] onsets, float onsetRate, int minBpm, int maxBpm)
    {
        var bestBpm = 0;
        var bestEnergy = 0.0;

        // Test each BPM candidate (0.5 BPM resolution)
        for (int bpmX2 = minBpm * 2; bpmX2 <= maxBpm * 2; bpmX2++)
        {
            var bpm = bpmX2 / 2.0;
            var lagSamples = onsetRate * 60.0 / bpm;

            // Sum onset energy at multiples of this lag (comb filter)
            double energy = 0;
            int pulses = 0;
            for (int harmonic = 1; harmonic <= 4; harmonic++)
            {
                var step = lagSamples * harmonic;
                if (step >= onsets.Length) break;

                for (double pos = 0; pos < onsets.Length; pos += step)
                {
                    var idx = (int)pos;
                    if (idx < onsets.Length)
                    {
                        // Check a small window around the expected position
                        var window = Math.Max(1, (int)(lagSamples * 0.06));
                        var lo = Math.Max(0, idx - window);
                        var hi = Math.Min(onsets.Length - 1, idx + window);
                        float peak = 0;
                        for (int j = lo; j <= hi; j++)
                            peak = Math.Max(peak, onsets[j]);
                        energy += peak;
                        pulses++;
                    }
                }
            }

            if (pulses > 0) energy /= pulses;

            if (energy > bestEnergy)
            {
                bestEnergy = energy;
                bestBpm = (int)Math.Round(bpm);
            }
        }

        return bestBpm;
    }

    /// <summary>Autocorrelation-based BPM estimation.</summary>
    private static int AutocorrelationEstimate(float[] onsets, float onsetRate, int minBpm, int maxBpm)
    {
        var minLag = (int)(onsetRate * 60.0 / maxBpm);
        var maxLag = (int)(onsetRate * 60.0 / minBpm);
        maxLag = Math.Min(maxLag, onsets.Length / 2);
        if (minLag >= maxLag) return 0;

        // Compute autocorrelation
        var corr = new double[maxLag + 1];
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            double sum = 0;
            int count = onsets.Length - lag;
            for (int i = 0; i < count; i++)
                sum += onsets[i] * onsets[i + lag];
            corr[lag] = sum / count;
        }

        // Find peaks in autocorrelation (not just the max — look for the most prominent)
        var bestLag = minLag;
        var bestVal = 0.0;

        for (int lag = minLag + 1; lag < maxLag; lag++)
        {
            // Must be a local peak
            if (corr[lag] > corr[lag - 1] && corr[lag] > corr[lag + 1] && corr[lag] > bestVal)
            {
                bestVal = corr[lag];
                bestLag = lag;
            }
        }

        return (int)Math.Round(onsetRate * 60.0 / bestLag);
    }

    /// <summary>Compute onset strength envelope using half-wave rectified spectral flux.</summary>
    private static float[] ComputeOnsetEnvelope(float[] samples, int hopSize)
    {
        var count = samples.Length / hopSize;
        if (count < 2) return [];

        var envelope = new float[count];

        // Compute RMS energy per window
        for (int i = 0; i < count; i++)
        {
            float energy = 0;
            var offset = i * hopSize;
            for (int j = 0; j < hopSize && offset + j < samples.Length; j++)
            {
                var s = samples[offset + j];
                energy += s * s;
            }
            envelope[i] = MathF.Sqrt(energy / hopSize);
        }

        // Spectral flux: positive first-order difference
        var flux = new float[count];
        for (int i = 1; i < count; i++)
        {
            var diff = envelope[i] - envelope[i - 1];
            flux[i] = diff > 0 ? diff : 0;
        }

        return flux;
    }

    /// <summary>Adaptive threshold: subtract local mean to suppress noise.</summary>
    private static void AdaptiveThreshold(float[] signal, int windowHalf)
    {
        if (windowHalf < 1) windowHalf = 1;
        var mean = new float[signal.Length];

        // Running mean
        double sum = 0;
        int count = 0;
        for (int i = 0; i < signal.Length; i++)
        {
            sum += signal[i];
            count++;
            if (i >= windowHalf * 2)
            {
                sum -= signal[i - windowHalf * 2];
                count--;
            }
            mean[i] = (float)(sum / count);
        }

        for (int i = 0; i < signal.Length; i++)
        {
            signal[i] = signal[i] > mean[i] * 1.5f ? signal[i] - mean[i] : 0;
        }
    }

    /// <summary>Simple first-order IIR low-pass filter.</summary>
    private static float[] LowPassFilter(float[] samples, int sampleRate, float cutoffHz)
    {
        var rc = 1.0f / (2.0f * MathF.PI * cutoffHz);
        var dt = 1.0f / sampleRate;
        var alpha = dt / (rc + dt);

        var output = new float[samples.Length];
        output[0] = samples[0];
        for (int i = 1; i < samples.Length; i++)
            output[i] = output[i - 1] + alpha * (samples[i] - output[i - 1]);
        return output;
    }

    /// <summary>Band-pass: high-pass then low-pass.</summary>
    private static float[] BandPassFilter(float[] samples, int sampleRate, float lowHz, float highHz)
    {
        // High-pass
        var rcHigh = 1.0f / (2.0f * MathF.PI * lowHz);
        var dt = 1.0f / sampleRate;
        var alphaHigh = rcHigh / (rcHigh + dt);

        var hp = new float[samples.Length];
        hp[0] = samples[0];
        for (int i = 1; i < samples.Length; i++)
            hp[i] = alphaHigh * (hp[i - 1] + samples[i] - samples[i - 1]);

        // Low-pass
        return LowPassFilter(hp, sampleRate, highHz);
    }

    private static float[] ReadMono(AudioFileReader reader, int sampleCount, int channels)
    {
        var buffer = new float[Math.Min(sampleCount, 1024 * 1024)];
        var mono = new List<float>(buffer.Length / channels);
        int totalRead = 0;

        while (totalRead < sampleCount)
        {
            var toRead = Math.Min(buffer.Length, sampleCount - totalRead);
            var read = reader.Read(buffer, 0, toRead);
            if (read == 0) break;
            totalRead += read;

            for (int i = 0; i < read; i += channels)
            {
                float sum = 0;
                for (int ch = 0; ch < channels && i + ch < read; ch++)
                    sum += buffer[i + ch];
                mono.Add(sum / channels);
            }
        }

        return mono.ToArray();
    }
}
