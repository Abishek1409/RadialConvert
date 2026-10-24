using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RadialConvert.UI;

namespace RadialConvert.Conversion;

/// <summary>Runs a conversion on a background thread and reports progress through a toast (UI thread).</summary>
public static class ConversionService
{
    // Limit parallel tool processes so a burst of conversions does not swamp the machine.
    private static readonly SemaphoreSlim Gate = new(Math.Max(1, Environment.ProcessorCount / 2));

    public static async void Start(string source, ConversionOption option)
    {
        var name = Path.GetFileName(source);
        var toast = ToastWindow.ShowProgress($"Converting {name}", $"{option.MenuLabel(Path.GetExtension(source))}...");
        using var cts = new CancellationTokenSource();
        toast.AddCancel(cts.Cancel);
        // Created on the UI thread, so reports are marshalled back to it automatically.
        var progress = new Progress<double>(toast.SetProgress);

        try
        {
            var result = await Task.Run(() => RunAsync(source, option, cts.Token, progress));
            toast.SetSuccess("Conversion complete", Path.GetFileName(result), result);
        }
        catch (OperationCanceledException)
        {
            toast.SetInfo("Conversion cancelled", name);
        }
        catch (ConversionException ex)
        {
            toast.SetError($"Could not convert {name}", ex.Message);
        }
        catch (Exception ex)
        {
            toast.SetError($"Could not convert {name}", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>Converts and returns the file (or folder, for multi-page output) that was created.</summary>
    public static async Task<string> RunAsync(string source, ConversionOption option, CancellationToken token,
        IProgress<double>? progress = null)
    {
        await Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(source)) throw new ConversionException("The source file no longer exists.");
            var outPath = OutputPath.Reserve(source, option.TargetExtension);
            var ctx = new ConversionContext(source, outPath, token, progress);
            try
            {
                await option.Run(ctx).ConfigureAwait(false);

                if (Directory.Exists(ctx.ResultPath)) return ctx.ResultPath;
                var info = new FileInfo(ctx.ResultPath);
                if (!info.Exists || info.Length == 0)
                    throw new ConversionException("The tool finished but produced no output.");
                return ctx.ResultPath;
            }
            catch
            {
                // Remove our placeholder / partial output; never touches pre-existing files.
                if (ctx.ResultPath == outPath) TryDelete(outPath);
                throw;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* ignore */ }
    }
}
