using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

public sealed class ConversionContext
{
    public ConversionContext(string source, string output, CancellationToken token, IProgress<double>? progress = null)
    {
        Source = source;
        Output = output;
        Token = token;
        Progress = progress;
        ResultPath = output;
    }

    /// <summary>Optional 0..1 progress for converters that can measure it (FFmpeg). Others leave the bar indeterminate.</summary>
    public IProgress<double>? Progress { get; }

    public string Source { get; }

    /// <summary>Reserved (empty) output file beside the source. Converters write here.</summary>
    public string Output { get; }

    public CancellationToken Token { get; }

    /// <summary>
    /// What the user should be shown. Defaults to <see cref="Output"/>; a converter that produces several
    /// files (e.g. one image per PDF page) deletes the placeholder and points this at a folder instead.
    /// </summary>
    public string ResultPath { get; set; }
}

/// <summary>One slice of the radial menu: a source-extension set mapped to a single target format.</summary>
public sealed class ConversionOption
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string TargetName { get; init; }
    /// <summary>Output extension including the dot, e.g. ".png".</summary>
    public required string TargetExtension { get; init; }
    public required string Glyph { get; init; }
    public required IReadOnlySet<string> SourceExtensions { get; init; }
    public required Func<ConversionContext, Task> Run { get; init; }

    /// <summary>Name shown in the settings window.</summary>
    public string DisplayName => $"{Group} to {TargetName}";

    /// <summary>Short slice label, e.g. "PNG to JPG", "PDF to Word".</summary>
    public string MenuLabel(string sourceExtension) =>
        $"{sourceExtension.TrimStart('.').ToUpperInvariant()} to {TargetName}";
}

/// <summary>Icon glyphs from Segoe Fluent Icons / Segoe MDL2 Assets (ship with Windows 10/11).</summary>
public static class Glyphs
{
    public const string FontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";
    public const string Image = "\uE91B";
    public const string Pdf = "\uEA90";
    public const string Document = "\uE8A5";
    public const string Slides = "\uE786";
    public const string Text = "\uE8D2";
    public const string Code = "\uE943";
    public const string Audio = "\uE8D6";
    public const string Video = "\uE714";
    public const string Cancel = "\uE711";
    public const string Check = "\uE73E";
    public const string Error = "\uE783";
    public const string Info = "\uE946";
    public const string Sync = "\uE895";
    public const string Folder = "\uE838";
}

/// <summary>A message that is safe to show directly to the user.</summary>
public class ConversionException : Exception
{
    public ConversionException(string message) : base(message) { }
}

/// <summary>Scratch folder under %TEMP%\RadialConvert, deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RadialConvert", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

internal static class Ext
{
    public static HashSet<string> Set(params string[] extensions) => new(extensions, StringComparer.OrdinalIgnoreCase);
}
