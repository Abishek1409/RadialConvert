using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

/// <summary>Phase 2 PDF conversions: Word (pdf2docx), PowerPoint (Poppler + Open XML SDK), images and text (Poppler).</summary>
public static class PdfConverters
{
    private static readonly IReadOnlySet<string> Pdf = Ext.Set(".pdf");

    public static IEnumerable<ConversionOption> Create()
    {
        yield return Option("word", "Word", ".docx", Glyphs.Document, ToWord);
        yield return Option("pptx", "PowerPoint", ".pptx", Glyphs.Slides, ToPowerPoint);
        yield return Option("png", "PNG", ".png", Glyphs.Image, ctx => ToImages(ctx, "png"));
        yield return Option("jpg", "JPG", ".jpg", Glyphs.Image, ctx => ToImages(ctx, "jpeg"));
        yield return Option("txt", "Text", ".txt", Glyphs.Text, ToText);
    }

    private static ConversionOption Option(string id, string name, string ext, string glyph, Func<ConversionContext, Task> run) => new()
    {
        Id = "pdf." + id,
        Group = "PDF",
        TargetName = name,
        TargetExtension = ext,
        Glyph = glyph,
        SourceExtensions = Pdf,
        Run = run
    };

    private static Task ToWord(ConversionContext ctx)
    {
        var exe = ToolLocator.Require(Tool.Pdf2Docx);
        // Large PDFs can take a while with pdf2docx.
        return ProcessRunner.RunAsync(exe, new[] { ctx.Source, ctx.Output }, ctx.Token, TimeSpan.FromMinutes(30));
    }

    private static Task ToText(ConversionContext ctx)
    {
        var exe = ToolLocator.RequireSibling(Tool.Poppler, "pdftotext.exe");
        return ProcessRunner.RunAsync(exe, new[] { "-layout", "-enc", "UTF-8", ctx.Source, ctx.Output }, ctx.Token);
    }

    private static async Task ToPowerPoint(ConversionContext ctx)
    {
        using var temp = new TempDir();
        var pages = await RenderPages(ctx, temp.Path, "png", dpi: 150).ConfigureAwait(false);
        File.Delete(ctx.Output); // PresentationDocument.Create wants to create the file itself
        PptxBuilder.Build(ctx.Output, pages);
    }

    /// <summary>One page: a single image beside the PDF. Several pages: a new folder with one image per page.</summary>
    private static async Task ToImages(ConversionContext ctx, string format)
    {
        using var temp = new TempDir();
        var pages = await RenderPages(ctx, temp.Path, format, dpi: 150).ConfigureAwait(false);

        if (pages.Count == 1)
        {
            File.Move(pages[0], ctx.Output, overwrite: true); // overwrites only our own empty placeholder
            return;
        }

        File.Delete(ctx.Output);
        var name = Path.GetFileNameWithoutExtension(ctx.Source);
        var folder = OutputPath.ReserveDirectory(Path.GetDirectoryName(ctx.Source)!, $"{name} pages");
        var ext = format == "jpeg" ? ".jpg" : ".png";
        int digits = pages.Count.ToString().Length;
        for (int i = 0; i < pages.Count; i++)
            File.Move(pages[i], Path.Combine(folder, $"{name} - page {(i + 1).ToString().PadLeft(digits, '0')}{ext}"));
        ctx.ResultPath = folder;
    }

    /// <summary>Renders every page with pdftoppm and returns the files in page order.</summary>
    private static async Task<IReadOnlyList<string>> RenderPages(ConversionContext ctx, string dir, string format, int dpi)
    {
        var exe = ToolLocator.Require(Tool.Poppler);
        var args = new List<string> { "-" + format, "-r", dpi.ToString() };
        if (format == "jpeg") args.AddRange(new[] { "-jpegopt", "quality=90" });
        args.Add(ctx.Source);
        args.Add(Path.Combine(dir, "page"));
        await ProcessRunner.RunAsync(exe, args, ctx.Token, TimeSpan.FromMinutes(20)).ConfigureAwait(false);

        // pdftoppm names files page-1.png or page-01.png depending on page count; sort numerically.
        var pages = Directory.GetFiles(dir, "page-*")
            .Select(f => (File: f, N: int.TryParse(Path.GetFileNameWithoutExtension(f).AsSpan(5), out var n) ? n : int.MaxValue))
            .OrderBy(p => p.N)
            .Select(p => p.File)
            .ToList();
        if (pages.Count == 0) throw new ConversionException("Poppler rendered no pages (is the PDF password protected?).");
        return pages;
    }
}
