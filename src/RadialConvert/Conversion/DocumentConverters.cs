using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

/// <summary>Phase 2 document conversions: Office to PDF (LibreOffice headless) and Markdown/HTML/TXT/DOCX (Pandoc).</summary>
public static class DocumentConverters
{
    // LibreOffice cannot run two conversions on one user profile at once, so serialize them.
    private static readonly SemaphoreSlim OfficeGate = new(1);

    private sealed record PandocFormat(string Id, string Name, string Ext, string Glyph, string[] Extensions, string Reader, string Writer);

    private static readonly PandocFormat[] PandocFormats =
    {
        new("md",   "Markdown", ".md",   Glyphs.Code,     new[] { ".md", ".markdown" }, "markdown", "gfm"),
        new("html", "HTML",     ".html", Glyphs.Code,     new[] { ".html", ".htm" },    "html",     "html"),
        // Pandoc has no plain-text reader; CommonMark with hard line breaks keeps text intact in practice.
        new("txt",  "Text",     ".txt",  Glyphs.Text,     new[] { ".txt" },             "commonmark+hard_line_breaks", "plain"),
        new("docx", "Word",     ".docx", Glyphs.Document, new[] { ".docx" },            "docx",     "docx"),
    };

    public static IEnumerable<ConversionOption> Create()
    {
        yield return new ConversionOption
        {
            Id = "office.pdf",
            Group = "Office",
            TargetName = "PDF",
            TargetExtension = ".pdf",
            Glyph = Glyphs.Pdf,
            SourceExtensions = Ext.Set(".docx", ".doc", ".odt", ".rtf", ".pptx", ".ppt", ".odp", ".xlsx", ".xls", ".ods"),
            Run = OfficeToPdf
        };

        foreach (var target in PandocFormats)
        {
            var sources = Ext.Set(PandocFormats.Where(f => f != target).SelectMany(f => f.Extensions).ToArray());
            yield return new ConversionOption
            {
                Id = "doc." + target.Id,
                Group = "Document",
                TargetName = target.Name,
                TargetExtension = target.Ext,
                Glyph = target.Glyph,
                SourceExtensions = sources,
                Run = ctx => PandocConvert(ctx, target)
            };
        }
    }

    private static async Task OfficeToPdf(ConversionContext ctx)
    {
        var soffice = ToolLocator.Require(Tool.LibreOffice);
        using var temp = new TempDir();

        // A private profile means this works even while the user has LibreOffice open.
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadialConvert", "lo-profile");
        Directory.CreateDirectory(profile);

        await OfficeGate.WaitAsync(ctx.Token).ConfigureAwait(false);
        try
        {
            await ProcessRunner.RunAsync(soffice, new[]
            {
                $"-env:UserInstallation={new Uri(profile + Path.DirectorySeparatorChar).AbsoluteUri}",
                "--headless", "--norestore", "--nologo", "--nodefault", "--nolockcheck",
                "--convert-to", "pdf",
                "--outdir", temp.Path,
                ctx.Source
            }, ctx.Token, TimeSpan.FromMinutes(10)).ConfigureAwait(false);
        }
        finally
        {
            OfficeGate.Release();
        }

        // soffice exits 0 even on some failures; the missing file is the real signal.
        var produced = Directory.GetFiles(temp.Path, "*.pdf").FirstOrDefault()
            ?? throw new ConversionException("LibreOffice did not produce a PDF (the file may be corrupt or password protected).");
        File.Move(produced, ctx.Output, overwrite: true); // overwrites only our own empty placeholder
    }

    private static Task PandocConvert(ConversionContext ctx, PandocFormat target)
    {
        var pandoc = ToolLocator.Require(Tool.Pandoc);
        var sourceExt = Path.GetExtension(ctx.Source);
        var source = PandocFormats.First(f => f.Extensions.Contains(sourceExt, StringComparer.OrdinalIgnoreCase));
        var outDir = Path.GetDirectoryName(ctx.Output)!;

        var args = new List<string> { "--from", source.Reader, "--to", target.Writer, "--output", ctx.Output };
        switch (target.Id)
        {
            case "html":
                // Single self-contained file; images from DOCX are inlined.
                args.AddRange(new[] { "--standalone", "--embed-resources",
                    "--metadata", $"pagetitle={Path.GetFileNameWithoutExtension(ctx.Source)}" });
                break;
            case "md":
                // Images go to "<name>_media" beside the .md (only created when the source has images).
                args.Add($"--extract-media={Path.GetFileNameWithoutExtension(ctx.Output)}_media");
                break;
            case "txt":
                args.Add("--wrap=none");
                break;
        }
        args.Add(ctx.Source);

        // Working dir = output folder so relative media paths resolve beside the output.
        return ProcessRunner.RunAsync(pandoc, args, ctx.Token, workingDirectory: outDir);
    }
}
