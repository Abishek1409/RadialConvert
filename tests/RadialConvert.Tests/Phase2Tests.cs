using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using RadialConvert.Conversion;
using RadialConvert.Settings;
using Xunit;

namespace RadialConvert.Tests;

public class Phase2RegistryTests
{
    private static string[] Targets(string file) =>
        ConversionRegistry.ForFile(file, new AppSettings()).Select(o => o.TargetName).ToArray();

    [Fact]
    public void PdfOffersWordPowerPointImagesAndText() =>
        Assert.Equal(new[] { "Word", "PowerPoint", "PNG", "JPG", "Text" }, Targets("a.pdf"));

    [Theory]
    [InlineData("a.xlsx")]
    [InlineData("a.pptx")]
    [InlineData("a.ODT")]
    public void OfficeFilesOfferPdf(string file) => Assert.Equal(new[] { "PDF" }, Targets(file));

    [Fact]
    public void DocxOffersPdfAndPandocTargets() =>
        Assert.Equal(new[] { "PDF", "Markdown", "HTML", "Text" }, Targets("a.docx"));

    [Fact]
    public void MarkdownOffersOtherPandocTargets() =>
        Assert.Equal(new[] { "HTML", "Text", "Word" }, Targets("notes.md"));

    [Fact]
    public void MenuLabelMatchesSpec() =>
        Assert.Equal("PDF to Word", ConversionRegistry.All.First(o => o.Id == "pdf.word").MenuLabel(".pdf"));
}

public sealed class OutputDirectoryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("radialconvert-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void ReservesNumberedFolders()
    {
        Assert.Equal(Path.Combine(_dir, "doc pages"), OutputPath.ReserveDirectory(_dir, "doc pages"));
        Assert.Equal(Path.Combine(_dir, "doc pages (1)"), OutputPath.ReserveDirectory(_dir, "doc pages"));
    }
}

public sealed class PptxBuilderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("radialconvert-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // 1x1 transparent PNG, and the same image with IHDR patched to 200x100 (only the header is read).
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private string WritePng(string name, int w, int h)
    {
        var bytes = (byte[])TinyPng.Clone();
        BitConverter.GetBytes(w).Reverse().ToArray().CopyTo(bytes, 16);
        BitConverter.GetBytes(h).Reverse().ToArray().CopyTo(bytes, 20);
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    [Fact]
    public void ReadsPngSize() => Assert.Equal((200, 100), PptxBuilder.PngSize(WritePng("a.png", 200, 100)));

    [Fact]
    public void BuildsValidPresentationWithOneSlidePerPage()
    {
        var pages = new[] { WritePng("p1.png", 850, 1100), WritePng("p2.png", 850, 1100), WritePng("p3.png", 1100, 850) };
        var output = Path.Combine(_dir, "out.pptx");

        PptxBuilder.Build(output, pages);

        using var doc = PresentationDocument.Open(output, false);
        var errors = new OpenXmlValidator().Validate(doc).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors));

        Assert.Equal(3, doc.PresentationPart!.SlideParts.Count());
        var size = doc.PresentationPart.Presentation.SlideSize!;
        // Portrait first page => portrait slide (height/width ~ 1100/850).
        Assert.InRange((double)size.Cy!.Value / size.Cx!.Value, 1.28, 1.30);
    }

    [Fact]
    public void EmptyPageListIsAnError() =>
        Assert.Throws<ConversionException>(() => PptxBuilder.Build(Path.Combine(_dir, "x.pptx"), Array.Empty<string>()));
}
