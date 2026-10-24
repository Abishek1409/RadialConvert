using System;
using System.IO;
using System.Linq;
using RadialConvert.Conversion;
using RadialConvert.Settings;
using Xunit;

namespace RadialConvert.Tests;

public sealed class OutputPathTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("radialconvert-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void UsesPlainNameWhenFree()
    {
        var src = Path.Combine(_dir, "photo.png");
        Assert.Equal(Path.Combine(_dir, "photo.jpg"), OutputPath.Reserve(src, ".jpg"));
    }

    [Fact]
    public void NeverOverwritesAndNumbersSequentially()
    {
        var src = Path.Combine(_dir, "photo.png");
        File.WriteAllText(Path.Combine(_dir, "photo.jpg"), "existing");

        var first = OutputPath.Reserve(src, ".jpg");
        var second = OutputPath.Reserve(src, ".jpg");

        Assert.Equal(Path.Combine(_dir, "photo (1).jpg"), first);
        Assert.Equal(Path.Combine(_dir, "photo (2).jpg"), second);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_dir, "photo.jpg")));
    }
}

public class RegistryTests
{
    [Theory]
    [InlineData("a.png", new[] { "JPG", "WebP", "BMP", "GIF", "TIFF", "PDF" })]
    [InlineData("a.JPEG", new[] { "PNG", "WebP", "BMP", "GIF", "TIFF", "PDF" })]
    [InlineData("a.tif", new[] { "PNG", "JPG", "WebP", "BMP", "GIF", "PDF" })]
    [InlineData("a.svg", new[] { "PNG", "JPG", "PDF" })]
    public void OffersOnlyMatchingTargets(string file, string[] expected)
    {
        var names = ConversionRegistry.ForFile(file, new AppSettings()).Select(o => o.TargetName).ToArray();
        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData("a.exe")]
    [InlineData("noextension")]
    public void UnsupportedFilesGetNoOptions(string file) =>
        Assert.Empty(ConversionRegistry.ForFile(file, new AppSettings()));

    [Fact]
    public void DisabledConversionsAreHidden()
    {
        var settings = new AppSettings();
        settings.DisabledConversions.Add("image.jpg");
        Assert.DoesNotContain(ConversionRegistry.ForFile("a.png", settings), o => o.Id == "image.jpg");
    }

    [Fact]
    public void IdsAreUnique() =>
        Assert.Equal(ConversionRegistry.All.Count, ConversionRegistry.All.Select(o => o.Id).Distinct().Count());

    [Fact]
    public void MenuLabelReadsNaturally() =>
        Assert.Equal("PNG to JPG", ConversionRegistry.All.First(o => o.Id == "image.jpg").MenuLabel(".png"));
}
