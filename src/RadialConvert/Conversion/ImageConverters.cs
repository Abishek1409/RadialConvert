using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

/// <summary>Phase 1 conversions: raster images via ImageMagick, SVG via resvg (Inkscape/ImageMagick fallback).</summary>
public static class ImageConverters
{
    private static readonly string[] RasterExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff" };

    private sealed record Target(string Id, string Name, string Ext, string MagickFormat, string[] OwnExtensions,
        bool Multiframe, bool Opaque);

    private static readonly Target[] RasterTargets =
    {
        new("png",  "PNG",  ".png",  "PNG",  new[] { ".png" },           false, false),
        new("jpg",  "JPG",  ".jpg",  "JPEG", new[] { ".jpg", ".jpeg" },  false, true),
        new("webp", "WebP", ".webp", "WEBP", new[] { ".webp" },          true,  false),
        new("bmp",  "BMP",  ".bmp",  "BMP",  new[] { ".bmp" },           false, true),
        new("gif",  "GIF",  ".gif",  "GIF",  new[] { ".gif" },           true,  false),
        new("tiff", "TIFF", ".tiff", "TIFF", new[] { ".tif", ".tiff" },  true,  false),
        new("pdf",  "PDF",  ".pdf",  "PDF",  new[] { ".pdf" },           true,  false),
    };

    public static IEnumerable<ConversionOption> Create()
    {
        foreach (var t in RasterTargets)
        {
            var sources = new HashSet<string>(RasterExtensions, StringComparer.OrdinalIgnoreCase);
            sources.ExceptWith(t.OwnExtensions);

            yield return new ConversionOption
            {
                Id = "image." + t.Id,
                Group = "Image",
                TargetName = t.Name,
                TargetExtension = t.Ext,
                Glyph = t.Id == "pdf" ? Glyphs.Pdf : Glyphs.Image,
                SourceExtensions = sources,
                Run = ctx => MagickConvert(ctx, t)
            };
        }

        foreach (var t in new[] { RasterTargets[0], RasterTargets[1], RasterTargets[6] }) // PNG, JPG, PDF
        {
            yield return new ConversionOption
            {
                Id = "svg." + t.Id,
                Group = "SVG",
                TargetName = t.Name,
                TargetExtension = t.Ext,
                Glyph = t.Id == "pdf" ? Glyphs.Pdf : Glyphs.Image,
                SourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".svg" },
                Run = ctx => SvgConvert(ctx, t)
            };
        }
    }

    private static Task MagickConvert(ConversionContext ctx, Target t, string? sourceOverride = null)
    {
        var magick = ToolLocator.Require(Tool.ImageMagick);
        var input = sourceOverride ?? ctx.Source;

        var args = new List<string>
        {
            // Single-frame targets take only the first frame/page, otherwise ImageMagick writes name-0, name-1, ...
            t.Multiframe ? input : input + "[0]",
            "-auto-orient"
        };
        if (t.Opaque) args.AddRange(new[] { "-background", "white", "-alpha", "remove", "-alpha", "off" });
        if (t.Id == "jpg") args.AddRange(new[] { "-quality", "92" });
        // Explicit format prefix: output is written in this format regardless of name.
        args.Add($"{t.MagickFormat}:{ctx.Output}");

        return ProcessRunner.RunAsync(magick, args, ctx.Token);
    }

    private static async Task SvgConvert(ConversionContext ctx, Target t)
    {
        if (t.Id == "png")
        {
            await RenderSvgToPng(ctx, ctx.Source, ctx.Output).ConfigureAwait(false);
            return;
        }

        // JPG/PDF: render to a temp PNG first, then let ImageMagick finish the job.
        var tempPng = Path.Combine(Path.GetTempPath(), $"radialconvert-{Guid.NewGuid():N}.png");
        try
        {
            await RenderSvgToPng(ctx, ctx.Source, tempPng).ConfigureAwait(false);
            await MagickConvert(ctx, t, tempPng).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(tempPng); } catch { /* best effort */ }
        }
    }

    private static Task RenderSvgToPng(ConversionContext ctx, string svg, string png)
    {
        var resvg = ToolLocator.Find(Tool.Resvg);
        if (resvg != null)
            return ProcessRunner.RunAsync(resvg, new[] { svg, png }, ctx.Token);

        var inkscape = ToolLocator.Find(Tool.Inkscape);
        if (inkscape != null)
            return ProcessRunner.RunAsync(inkscape, new[] { svg, "--export-type=png", $"--export-filename={png}" }, ctx.Token);

        var magick = ToolLocator.Find(Tool.ImageMagick);
        if (magick != null)
            return ProcessRunner.RunAsync(magick, new[] { "-background", "none", svg, $"PNG:{png}" }, ctx.Token);

        throw ToolLocator.MissingError(Tool.Resvg);
    }
}
