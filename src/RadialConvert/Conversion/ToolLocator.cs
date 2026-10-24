using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RadialConvert.Conversion;

public enum Tool { ImageMagick, Resvg, Inkscape, Poppler, Pdf2Docx, LibreOffice, Pandoc, FFmpeg }

/// <param name="BundledPaths">Paths relative to the tools folder.</param>
/// <param name="PathExeNames">Executable names looked up on PATH.</param>
/// <param name="InstalledPaths">Standard installer locations; "*" in a folder name matches any version (newest wins).</param>
public sealed record ToolInfo(Tool Tool, string Name, string[] BundledPaths, string[] PathExeNames,
    string[] InstalledPaths, string DownloadUrl);

/// <summary>Finds external tools: bundled "tools" folder next to the exe, then PATH, then standard install folders.</summary>
public static class ToolLocator
{
    public static string ToolsDir => Path.Combine(AppContext.BaseDirectory, "tools");

    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public static IReadOnlyList<ToolInfo> All { get; } = new[]
    {
        new ToolInfo(Tool.ImageMagick, "ImageMagick",
            new[] { @"imagemagick\magick.exe" }, new[] { "magick.exe" },
            new[] { Path.Combine(ProgramFiles, @"ImageMagick-*\magick.exe"), Path.Combine(ProgramFilesX86, @"ImageMagick-*\magick.exe") },
            "https://imagemagick.org/script/download.php#windows"),
        new ToolInfo(Tool.Resvg, "resvg",
            new[] { @"resvg\resvg.exe" }, new[] { "resvg.exe" }, Array.Empty<string>(),
            "https://github.com/linebender/resvg/releases"),
        new ToolInfo(Tool.Inkscape, "Inkscape (optional, SVG fallback)",
            new[] { @"inkscape\bin\inkscape.com", @"inkscape\bin\inkscape.exe" }, new[] { "inkscape.com", "inkscape.exe" },
            new[] { Path.Combine(ProgramFiles, @"Inkscape\bin\inkscape.com") },
            "https://inkscape.org/release/"),
        // Poppler ships several exes in one folder; we locate pdftoppm and use its siblings.
        new ToolInfo(Tool.Poppler, "Poppler",
            new[] { @"poppler\Library\bin\pdftoppm.exe", @"poppler\bin\pdftoppm.exe", @"poppler\*\Library\bin\pdftoppm.exe" },
            new[] { "pdftoppm.exe" }, Array.Empty<string>(),
            "https://github.com/oschwartz10612/poppler-windows/releases"),
        new ToolInfo(Tool.Pdf2Docx, "pdf2docx (built with helpers\\pdf2docx\\build.ps1)",
            new[] { @"pdf2docx\pdf2docx.exe" }, new[] { "pdf2docx.exe" }, Array.Empty<string>(),
            "https://github.com/ArtifexSoftware/pdf2docx"),
        new ToolInfo(Tool.LibreOffice, "LibreOffice",
            new[] { @"libreoffice\program\soffice.com", @"libreoffice\App\libreoffice\program\soffice.com" },
            new[] { "soffice.com" },
            new[] { Path.Combine(ProgramFiles, @"LibreOffice\program\soffice.com"), Path.Combine(ProgramFilesX86, @"LibreOffice\program\soffice.com") },
            "https://www.libreoffice.org/download/download-libreoffice/"),
        new ToolInfo(Tool.Pandoc, "Pandoc",
            new[] { @"pandoc\pandoc.exe", @"pandoc\*\pandoc.exe" }, new[] { "pandoc.exe" },
            new[] { Path.Combine(LocalAppData, @"Pandoc\pandoc.exe"), Path.Combine(ProgramFiles, @"Pandoc\pandoc.exe") },
            "https://github.com/jgm/pandoc/releases"),
        new ToolInfo(Tool.FFmpeg, "FFmpeg",
            new[] { @"ffmpeg\bin\ffmpeg.exe", @"ffmpeg\*\bin\ffmpeg.exe" }, new[] { "ffmpeg.exe" }, Array.Empty<string>(),
            "https://www.gyan.dev/ffmpeg/builds/"),
    };

    public static ToolInfo Info(Tool tool) => All.First(t => t.Tool == tool);

    /// <summary>Full path of the tool, or null. Not cached so tools dropped in while running are picked up.</summary>
    public static string? Find(Tool tool)
    {
        var info = Info(tool);

        foreach (var rel in info.BundledPaths)
        {
            var p = Resolve(Path.Combine(ToolsDir, rel));
            if (p != null) return p;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var exe in info.PathExeNames)
            {
                try
                {
                    var p = Path.Combine(dir, exe);
                    if (File.Exists(p)) return p;
                }
                catch (ArgumentException) { /* malformed PATH entry */ }
            }
        }

        foreach (var installed in info.InstalledPaths)
        {
            var p = Resolve(installed);
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>Finds a sibling exe next to a located tool, e.g. pdftotext next to pdftoppm.</summary>
    public static string RequireSibling(Tool tool, string exeName)
    {
        var p = Path.Combine(Path.GetDirectoryName(Require(tool))!, exeName);
        return File.Exists(p) ? p : throw new ConversionException($"{exeName} is missing from the {Info(tool).Name} folder.");
    }

    public static string Require(Tool tool) => Find(tool) ?? throw MissingError(tool);

    public static ConversionException MissingError(Tool tool)
    {
        var info = Info(tool);
        return new ConversionException(
            $"{info.Name} was not found. Place it at \"{Path.Combine(ToolsDir, info.BundledPaths[0])}\" " +
            $"or add it to PATH. Free download: {info.DownloadUrl}");
    }

    /// <summary>Resolves a path where one folder segment may contain "*" (matched against real folders, newest first).</summary>
    private static string? Resolve(string pattern)
    {
        int star = pattern.IndexOf('*');
        if (star < 0) return File.Exists(pattern) ? pattern : null;

        int segStart = pattern.LastIndexOf('\\', star) + 1;
        int segEnd = pattern.IndexOf('\\', star);
        if (segEnd < 0) return null;

        var parent = pattern[..Math.Max(0, segStart - 1)];
        var segment = pattern[segStart..segEnd];
        var rest = pattern[(segEnd + 1)..];
        if (!Directory.Exists(parent)) return null;

        try
        {
            foreach (var dir in Directory.GetDirectories(parent, segment).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(dir, rest);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return null;
    }
}
