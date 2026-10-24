using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using P = DocumentFormat.OpenXml.Presentation;

namespace RadialConvert.Conversion;

/// <summary>
/// Builds a minimal, valid .pptx with one full-bleed picture per slide, using the Open XML SDK (MIT).
/// Slide size follows the aspect ratio of the first image; every image is fitted and centered.
/// </summary>
public static class PptxBuilder
{
    private const string NsDecl =
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";

    private const string EmptyTree =
        "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr/>";

    private const long SlideWidth = 12192000; // 13.333 in (16:9 width), in EMU
    private const long MinEmu = 914400, MaxEmu = 51206400; // PowerPoint's allowed slide size range

    public static void Build(string outputPath, IReadOnlyList<string> pngImages)
    {
        if (pngImages.Count == 0) throw new ConversionException("The PDF has no pages.");

        var (w0, h0) = PngSize(pngImages[0]);
        long slideW = SlideWidth;
        long slideH = Math.Clamp((long)Math.Round(slideW * (double)h0 / w0), MinEmu, MaxEmu);

        using var doc = PresentationDocument.Create(outputPath, PresentationDocumentType.Presentation);
        var presPart = doc.AddPresentationPart();

        var masterPart = presPart.AddNewPart<SlideMasterPart>("rIdMaster");
        var layoutPart = masterPart.AddNewPart<SlideLayoutPart>("rIdLayout");
        var themePart = masterPart.AddNewPart<ThemePart>("rIdTheme");
        layoutPart.AddPart(masterPart);
        presPart.AddPart(themePart, "rIdTheme");

        themePart.Theme = new DocumentFormat.OpenXml.Drawing.Theme(ThemeXml);
        layoutPart.SlideLayout = new SlideLayout(
            $"<p:sldLayout {NsDecl} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\"><p:spTree>{EmptyTree}</p:spTree></p:cSld>" +
            "<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        masterPart.SlideMaster = new SlideMaster(
            $"<p:sldMaster {NsDecl}><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>" +
            $"<p:spTree>{EmptyTree}</p:spTree></p:cSld>" +
            "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" " +
            "accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>" +
            "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rIdLayout\"/></p:sldLayoutIdLst>" +
            "<p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>");

        var slideIds = new SlideIdList();
        uint slideId = 256;
        for (int i = 0; i < pngImages.Count; i++)
        {
            var slidePart = presPart.AddNewPart<SlidePart>($"rIdSlide{i + 1}");
            slidePart.AddPart(layoutPart);
            var imagePart = slidePart.AddImagePart(ImagePartType.Png, "rIdImg");
            using (var fs = File.OpenRead(pngImages[i])) imagePart.FeedData(fs);

            // Fit the page inside the slide, preserving aspect ratio.
            var (w, h) = PngSize(pngImages[i]);
            double scale = Math.Min((double)slideW / w, (double)slideH / h);
            long cx = (long)(w * scale), cy = (long)(h * scale);
            long x = (slideW - cx) / 2, y = (slideH - cy) / 2;

            slidePart.Slide = new Slide(
                $"<p:sld {NsDecl}><p:cSld><p:spTree>{EmptyTree}" +
                $"<p:pic><p:nvPicPr><p:cNvPr id=\"2\" name=\"Page {i + 1}\" descr=\"Page {i + 1}\"/>" +
                "<p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                "<p:blipFill><a:blip r:embed=\"rIdImg\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill>" +
                $"<p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>" +
                "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>" +
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");

            slideIds.Append(new SlideId { Id = slideId++, RelationshipId = $"rIdSlide{i + 1}" });
        }

        presPart.Presentation = new Presentation(
            new SlideMasterIdList(new SlideMasterId { Id = 2147483648U, RelationshipId = "rIdMaster" }),
            slideIds,
            new SlideSize { Cx = (int)slideW, Cy = (int)slideH },
            new NotesSize { Cx = 6858000, Cy = 9144000 })
        { SaveSubsetFonts = true };
    }

    /// <summary>Reads width/height from the PNG IHDR chunk (bytes 16..23, big-endian).</summary>
    public static (int Width, int Height) PngSize(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using (var fs = File.OpenRead(path))
        {
            if (fs.Read(header) != 24) throw new ConversionException("Rendered page image is invalid.");
        }
        int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        if (w <= 0 || h <= 0) throw new ConversionException("Rendered page image is invalid.");
        return (w, h);
    }

    private static readonly string ThemeXml =
        "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"RadialConvert\"><a:themeElements>" +
        "<a:clrScheme name=\"Office\">" +
        "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        "<a:dk2><a:srgbClr val=\"44546A\"/></a:dk2><a:lt2><a:srgbClr val=\"E7E6E6\"/></a:lt2>" +
        "<a:accent1><a:srgbClr val=\"4472C4\"/></a:accent1><a:accent2><a:srgbClr val=\"ED7D31\"/></a:accent2>" +
        "<a:accent3><a:srgbClr val=\"A5A5A5\"/></a:accent3><a:accent4><a:srgbClr val=\"FFC000\"/></a:accent4>" +
        "<a:accent5><a:srgbClr val=\"5B9BD5\"/></a:accent5><a:accent6><a:srgbClr val=\"70AD47\"/></a:accent6>" +
        "<a:hlink><a:srgbClr val=\"0563C1\"/></a:hlink><a:folHlink><a:srgbClr val=\"954F72\"/></a:folHlink></a:clrScheme>" +
        "<a:fontScheme name=\"Office\">" +
        "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>" +
        "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont></a:fontScheme>" +
        "<a:fmtScheme name=\"Office\">" +
        "<a:fillStyleLst>" + Repeat3("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>") + "</a:fillStyleLst>" +
        "<a:lnStyleLst>" + Repeat3("<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>") + "</a:lnStyleLst>" +
        "<a:effectStyleLst>" + Repeat3("<a:effectStyle><a:effectLst/></a:effectStyle>") + "</a:effectStyleLst>" +
        "<a:bgFillStyleLst>" + Repeat3("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>") + "</a:bgFillStyleLst>" +
        "</a:fmtScheme></a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";

    private static string Repeat3(string s) => s + s + s;
}
