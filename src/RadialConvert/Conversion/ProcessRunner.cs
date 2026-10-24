using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RadialConvert.Conversion;

/// <summary>Runs a command-line tool hidden, with arguments passed via ArgumentList (no shell, no string quoting).</summary>
public static class ProcessRunner
{
    /// <param name="onStdout">If set, stdout lines go to this callback (e.g. FFmpeg progress) instead of the error log.</param>
    /// <param name="onStderr">If set, also receives every stderr line (the error log is still kept).</param>
    public static async Task RunAsync(string exe, IEnumerable<string> args, CancellationToken token,
        TimeSpan? timeout = null, string? workingDirectory = null, Action<string>? onStdout = null,
        Action<string>? onStderr = null)
    {
        var toolName = Path.GetFileNameWithoutExtension(exe);
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            // Portable tools (ImageMagick, Poppler) look for their DLLs/config next to the exe.
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Python tools (pdf2docx) otherwise print with the console code page and can crash on non-ASCII names.
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        // No stdin: tools that would prompt (e.g. FFmpeg "overwrite?") get EOF instead of hanging.
        psi.RedirectStandardInput = true;

        // A bundled ImageMagick has no registry keys on the target machine; point it at its own folder.
        var exeDir = Path.GetDirectoryName(exe)!;
        if (string.Equals(Path.GetFileName(exe), "magick.exe", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(exeDir, "modules")))
        {
            psi.Environment["MAGICK_HOME"] = exeDir;
            psi.Environment["MAGICK_CONFIGURE_PATH"] = exeDir;
            psi.Environment["MAGICK_CODER_MODULE_PATH"] = Path.Combine(exeDir, "modules", "coders");
            psi.Environment["MAGICK_FILTER_MODULE_PATH"] = Path.Combine(exeDir, "modules", "filters");
        }

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();
        void Collect(object _, DataReceivedEventArgs e) { if (e.Data != null) lock (output) output.AppendLine(e.Data); }
        process.ErrorDataReceived += Collect;
        if (onStderr != null)
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) onStderr(e.Data); };
        if (onStdout != null)
            process.OutputDataReceived += (_, e) => { if (e.Data != null) onStdout(e.Data); };
        else
            process.OutputDataReceived += Collect;

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ConversionException($"Could not start {toolName}: {ex.Message}");
        }
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(timeout ?? TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            if (token.IsCancellationRequested) throw;
            throw new ConversionException($"{toolName} timed out.");
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (output) detail = LastLines(output.ToString(), 4);
            throw new ConversionException(
                $"{toolName} failed (exit code {process.ExitCode})." + (detail.Length > 0 ? $"\n{detail}" : ""));
        }
    }

    private static string LastLines(string text, int count) =>
        string.Join("\n", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));
}
