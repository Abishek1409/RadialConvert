using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RadialConvert.Conversion;
using RadialConvert.Settings;
using Xunit;
using Xunit.Abstractions;

namespace RadialConvert.Tests;

public class MediaRegistryTests
{
    private static string[] Targets(string file) =>
        ConversionRegistry.ForFile(file, new AppSettings()).Select(o => o.TargetName).ToArray();

    [Fact]
    public void Mp3OffersOtherAudioFormats() => Assert.Equal(new[] { "WAV", "FLAC", "M4A", "OGG", "Opus" }, Targets("song.mp3"));

    [Fact]
    public void ParsesFfmpegDuration() =>
        Assert.Equal(TimeSpan.Parse("00:01:23.45"), MediaConverters.ParseDuration("  Duration: 00:01:23.45, start: 0.000000, bitrate: 128 kb/s"));

    [Fact]
    public void UnknownDurationIsNull() => Assert.Null(MediaConverters.ParseDuration("Duration: N/A, bitrate: N/A"));

    [Fact]
    public void ReportsProgressFraction()
    {
        double? last = null;
        var progress = new SyncProgress(v => last = v);
        MediaConverters.ReportProgress("out_time_us=5000000", TimeSpan.FromSeconds(10), progress);
        Assert.Equal(0.5, last);
        MediaConverters.ReportProgress("frame=12", TimeSpan.FromSeconds(10), progress); // ignored
        Assert.Equal(0.5, last);
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [Fact]
    public void Mp4OffersVideoAudioAndGif() =>
        Assert.Equal(new[] { "WebM", "MKV", "MP3 audio", "GIF" }, Targets("clip.MP4"));

    [Fact]
    public void AacDoesNotOfferM4a() => Assert.DoesNotContain("M4A", Targets("a.aac"));
}

/// <summary>Real FFmpeg conversions on tiny generated clips; skipped when FFmpeg is missing.</summary>
public sealed class MediaIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _log;
    private readonly string _dir = Directory.CreateTempSubdirectory("radialconvert-media-").FullName;

    public MediaIntegrationTests(ITestOutputHelper log) => _log = log;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string? Ffmpeg()
    {
        var p = ToolLocator.Find(Tool.FFmpeg);
        if (p == null) _log.WriteLine("SKIPPED: FFmpeg not installed.");
        return p;
    }

    private static Task<string> Convert(string source, string id) =>
        ConversionService.RunAsync(source, ConversionRegistry.All.First(o => o.Id == id), CancellationToken.None);

    private static async Task Generate(string ffmpeg, string output, params string[] input) =>
        await ProcessRunner.RunAsync(ffmpeg,
            new[] { "-hide_banner", "-loglevel", "error" }.Concat(input).Concat(new[] { "-y", output }), CancellationToken.None);

    [Theory]
    [InlineData("audio.mp3", ".mp3")]
    [InlineData("audio.flac", ".flac")]
    [InlineData("audio.ogg", ".ogg")]
    [InlineData("audio.m4a", ".m4a")]
    public async Task WavToAudioFormats(string id, string ext)
    {
        var ffmpeg = Ffmpeg();
        if (ffmpeg == null) return;
        var wav = Path.Combine(_dir, "tone.wav");
        await Generate(ffmpeg, wav, "-f", "lavfi", "-i", "sine=frequency=440:duration=1");

        var result = await Convert(wav, id);
        Assert.Equal(Path.Combine(_dir, "tone" + ext), result);
        Assert.True(new FileInfo(result).Length > 100);
    }

    [Theory]
    [InlineData("video.mp4", ".mp4")]
    [InlineData("video.webm", ".webm")]
    [InlineData("video.mp3", ".mp3")]
    [InlineData("video.gif", ".gif")]
    public async Task MkvToVideoTargets(string id, string ext)
    {
        var ffmpeg = Ffmpeg();
        if (ffmpeg == null) return;
        var mkv = Path.Combine(_dir, "clip.mkv");
        await Generate(ffmpeg, mkv, "-f", "lavfi", "-i", "testsrc=size=160x120:rate=10:duration=1",
            "-f", "lavfi", "-i", "sine=duration=1", "-c:v", "libx264", "-c:a", "aac", "-shortest");

        var result = await Convert(mkv, id);
        Assert.Equal(Path.Combine(_dir, "clip" + ext), result);
        Assert.True(new FileInfo(result).Length > 100);
    }

    [Fact]
    public async Task CorruptInputReportsFfmpegErrorAndCleansUp()
    {
        if (Ffmpeg() == null) return;
        var bad = Path.Combine(_dir, "broken.mp4");
        await File.WriteAllTextAsync(bad, "not a video");

        var ex = await Assert.ThrowsAsync<ConversionException>(() => Convert(bad, "video.webm"));
        Assert.Contains("ffmpeg failed", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "broken.webm"))); // placeholder removed
    }
}

public sealed class MediaProgressTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("radialconvert-progress-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Recorder : IProgress<double>
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<double> Values = new();
        public void Report(double value) => Values.Enqueue(value);
    }

    [Fact]
    public async Task ReportsIncreasingProgressEndingAtOne()
    {
        var ffmpeg = ToolLocator.Find(Tool.FFmpeg);
        if (ffmpeg == null) return;
        var wav = Path.Combine(_dir, "long.wav");
        await ProcessRunner.RunAsync(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "sine=duration=120", "-y", wav }, CancellationToken.None);

        var rec = new Recorder();
        await ConversionService.RunAsync(wav, ConversionRegistry.All.First(o => o.Id == "audio.mp3"), CancellationToken.None, rec);

        var values = rec.Values.ToArray();
        Assert.True(values.Length >= 2, $"only {values.Length} progress reports");
        Assert.Equal(1, values[^1]);
        Assert.Contains(values, v => v > 0 && v < 1); // at least one intermediate value
    }

    [Fact]
    public async Task CancellationStopsFfmpegAndRemovesOutput()
    {
        var ffmpeg = ToolLocator.Find(Tool.FFmpeg);
        if (ffmpeg == null) return;
        var mkv = Path.Combine(_dir, "big.mkv");
        await ProcessRunner.RunAsync(ffmpeg, new[] { "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
            "testsrc=size=1280x720:rate=30:duration=60", "-c:v", "libx264", "-preset", "ultrafast", "-y", mkv }, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ConversionService.RunAsync(mkv, ConversionRegistry.All.First(o => o.Id == "video.webm"), cts.Token));
        Assert.False(File.Exists(Path.Combine(_dir, "big.webm")));
    }
}
