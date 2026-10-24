using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

/// <summary>Phase 3 audio/video conversions with FFmpeg, with real progress parsed from "-progress pipe:1".</summary>
public static class MediaConverters
{
    private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".mov", ".avi", ".webm", ".wmv", ".flv", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts" };
    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".aif" };

    private sealed record Target(string Id, string Name, string Ext, string Glyph, string[] OwnExtensions, string[] Args);

    // Sensible quality defaults; FFmpeg picks sane values for everything else.
    private static readonly Target[] VideoTargets =
    {
        new("mp4",  "MP4",  ".mp4",  Glyphs.Video, new[] { ".mp4", ".m4v" },
            new[] { "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart" }),
        new("webm", "WebM", ".webm", Glyphs.Video, new[] { ".webm" },
            new[] { "-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-deadline", "good", "-cpu-used", "4", "-c:a", "libopus", "-b:a", "128k" }),
        // Copy subtitles only when they are text-based would need probing; drop them for reliability.
        new("mkv",  "MKV",  ".mkv",  Glyphs.Video, new[] { ".mkv" },
            new[] { "-map", "0:v?", "-map", "0:a?", "-c:v", "libx264", "-preset", "medium", "-crf", "21", "-c:a", "aac", "-b:a", "192k" }),
        // Extract the soundtrack.
        new("mp3",  "MP3 audio", ".mp3", Glyphs.Audio, Array.Empty<string>(),
            new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" }),
        new("gif",  "GIF",  ".gif",  Glyphs.Image, Array.Empty<string>(),
            // Two-pass palette in one filter graph: far better colors than the default GIF palette.
            new[] { "-an", "-vf", "fps=12,scale='min(640,iw)':-2:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4", "-loop", "0" }),
    };

    private static readonly Target[] AudioTargets =
    {
        new("mp3",  "MP3",  ".mp3",  Glyphs.Audio, new[] { ".mp3" },  new[] { "-vn", "-c:a", "libmp3lame", "-q:a", "2" }),
        new("wav",  "WAV",  ".wav",  Glyphs.Audio, new[] { ".wav" },  new[] { "-vn", "-c:a", "pcm_s16le" }),
        new("flac", "FLAC", ".flac", Glyphs.Audio, new[] { ".flac" }, new[] { "-vn", "-c:a", "flac" }),
        new("m4a",  "M4A",  ".m4a",  Glyphs.Audio, new[] { ".m4a", ".aac" }, new[] { "-vn", "-c:a", "aac", "-b:a", "192k" }),
        new("ogg",  "OGG",  ".ogg",  Glyphs.Audio, new[] { ".ogg" },  new[] { "-vn", "-c:a", "libvorbis", "-q:a", "5" }),
        new("opus", "Opus", ".opus", Glyphs.Audio, new[] { ".opus" }, new[] { "-vn", "-c:a", "libopus", "-b:a", "128k" }),
    };

    public static IEnumerable<ConversionOption> Create()
    {
        foreach (var t in VideoTargets)
            yield return Make("video", "Video", t, VideoExtensions);

        foreach (var t in AudioTargets)
            yield return Make("audio", "Audio", t, AudioExtensions);
    }

    private static ConversionOption Make(string group, string groupName, Target t, string[] sourceExtensions)
    {
        var sources = Ext.Set(sourceExtensions);
        sources.ExceptWith(t.OwnExtensions);
        return new ConversionOption
        {
            Id = $"{group}.{t.Id}",
            Group = groupName,
            TargetName = t.Name,
            TargetExtension = t.Ext,
            Glyph = t.Glyph,
            SourceExtensions = sources,
            Run = ctx => Run(ctx, t)
        };
    }

    private static async Task Run(ConversionContext ctx, Target t)
    {
        var ffmpeg = ToolLocator.Require(Tool.FFmpeg);
        var duration = await ProbeDurationAsync(ffmpeg, ctx).ConfigureAwait(false);

        var args = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-i", ctx.Source };
        args.AddRange(t.Args);
        // Machine-readable progress on stdout ("out_time_us=..."), errors stay on stderr.
        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", ctx.Output });

        await ProcessRunner.RunAsync(ffmpeg, args, ctx.Token, TimeSpan.FromHours(6),
            onStdout: line => ReportProgress(line, duration, ctx.Progress)).ConfigureAwait(false);
        ctx.Progress?.Report(1);
    }

    internal static void ReportProgress(string line, TimeSpan? duration, IProgress<double>? progress)
    {
        if (progress == null || duration is not { TotalMicroseconds: > 0 } d) return;
        if (!line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
        if (!long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us) || us < 0) return;
        progress.Report(Math.Clamp(us / d.TotalMicroseconds, 0, 1));
    }

    /// <summary>Reads the input duration from FFmpeg's own banner (no ffprobe needed). Null if unknown.</summary>
    private static async Task<TimeSpan?> ProbeDurationAsync(string ffmpeg, ConversionContext ctx)
    {
        string? found = null;
        try
        {
            // "ffmpeg -i file" exits non-zero (no output given) but prints "Duration: 00:01:23.45" on stderr first.
            await ProcessRunner.RunAsync(ffmpeg, new[] { "-hide_banner", "-nostdin", "-i", ctx.Source }, ctx.Token,
                TimeSpan.FromSeconds(30),
                onStderr: line => { if (found == null && line.Contains("Duration: ", StringComparison.Ordinal)) found = line; })
                .ConfigureAwait(false);
        }
        catch (ConversionException)
        {
            // Expected: no output file was given. A broken input fails again in the real run with a clear message.
        }
        return ParseDuration(found);
    }

    internal static TimeSpan? ParseDuration(string? ffmpegOutput)
    {
        if (ffmpegOutput == null) return null;
        int i = ffmpegOutput.IndexOf("Duration: ", StringComparison.Ordinal);
        if (i < 0) return null;
        var s = ffmpegOutput.AsSpan(i + 10);
        int comma = s.IndexOf(',');
        if (comma > 0) s = s[..comma];
        return TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out var ts) && ts > TimeSpan.Zero ? ts : null;
    }
}
