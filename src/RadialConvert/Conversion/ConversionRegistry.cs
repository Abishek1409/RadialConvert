using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RadialConvert.Settings;

namespace RadialConvert.Conversion;

/// <summary>All known conversions. Phase 3 adds media conversions here.</summary>
public static class ConversionRegistry
{
    public static IReadOnlyList<ConversionOption> All { get; } = ImageConverters.Create()
        .Concat(PdfConverters.Create())
        .Concat(DocumentConverters.Create())
        .Concat(MediaConverters.Create())
        .ToList();

    public static IReadOnlyList<ConversionOption> ForFile(string path, AppSettings settings)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return Array.Empty<ConversionOption>();
        return All.Where(o => o.SourceExtensions.Contains(ext) && settings.IsEnabled(o.Id)).ToList();
    }
}
