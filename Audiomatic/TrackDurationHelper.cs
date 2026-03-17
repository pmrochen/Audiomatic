using System.Diagnostics;
using System.Globalization;
using NAudio.Wave;

namespace Audiomatic;

internal static class TrackDurationHelper
{
    public static TimeSpan ResolveDuration(string filePath, TimeSpan? metadataDuration = null)
    {
        // ffprobe is the most accurate source (handles VBR, padding, etc.)
        var ffprobe = ReadWithFfprobe(filePath);
        if (ffprobe > TimeSpan.Zero) return ffprobe;

        // Fallback chain: MediaFoundation > NAudio > metadata
        var mf = ReadWithMediaFoundation(filePath);
        if (mf > TimeSpan.Zero) return mf;

        var naudio = ReadWithNAudio(filePath);
        if (naudio > TimeSpan.Zero) return naudio;

        return metadataDuration ?? TimeSpan.Zero;
    }

    public static int ResolveDurationMs(string filePath, TimeSpan? metadataDuration = null)
    {
        var duration = ResolveDuration(filePath, metadataDuration);
        if (duration <= TimeSpan.Zero) return 0;

        var totalMilliseconds = duration.TotalMilliseconds;
        if (totalMilliseconds >= int.MaxValue) return int.MaxValue;
        return (int)Math.Ceiling(totalMilliseconds);
    }

    public static string FormatDuration(int durationMs) =>
        FormatDuration(TimeSpan.FromMilliseconds(durationMs));

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "0:00";

        var totalSeconds = (long)Math.Ceiling(duration.TotalSeconds);
        var rounded = TimeSpan.FromSeconds(totalSeconds);
        return rounded.TotalHours >= 1
            ? $"{(int)rounded.TotalHours}:{rounded.Minutes:D2}:{rounded.Seconds:D2}"
            : $"{(int)rounded.TotalMinutes}:{rounded.Seconds:D2}";
    }

    private static TimeSpan ReadWithFfprobe(string filePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null) return TimeSpan.Zero;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            if (double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }
        catch { }
        return TimeSpan.Zero;
    }

    private static TimeSpan ReadWithMediaFoundation(string filePath)
    {
        try
        {
            using var reader = new MediaFoundationReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static TimeSpan ReadWithNAudio(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }
}
