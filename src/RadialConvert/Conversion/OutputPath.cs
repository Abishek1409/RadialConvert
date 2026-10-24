using System.IO;

namespace RadialConvert.Conversion;

public static class OutputPath
{
    /// <summary>
    /// Picks "name.ext", then "name (1).ext", "name (2).ext", ... beside the source and atomically reserves it
    /// by creating an empty file (FileMode.CreateNew), so existing files are never overwritten, even when
    /// several conversions run at once.
    /// </summary>
    public static string Reserve(string source, string newExtension)
    {
        var dir = Path.GetDirectoryName(source)!;
        var name = Path.GetFileNameWithoutExtension(source);

        for (int i = 0; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, i == 0 ? name + newExtension : $"{name} ({i}){newExtension}");
            if (Directory.Exists(candidate)) continue;
            try
            {
                using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken: try the next number.
            }
        }
        throw new ConversionException("Could not find a free output file name.");
    }

    /// <summary>Creates a new, previously non-existent folder "baseName", "baseName (1)", ... in <paramref name="parent"/>.</summary>
    public static string ReserveDirectory(string parent, string baseName)
    {
        for (int i = 0; i < 10_000; i++)
        {
            var candidate = Path.Combine(parent, i == 0 ? baseName : $"{baseName} ({i})");
            if (File.Exists(candidate) || Directory.Exists(candidate)) continue;
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        throw new ConversionException("Could not find a free output folder name.");
    }
}
