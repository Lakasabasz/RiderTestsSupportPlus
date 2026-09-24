using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>Runs a tool that writes test names to a file, one per line, and returns them.</summary>
internal static class ListingProcess
{
  private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

  /// <param name="arguments">Command line; <c>{0}</c> is replaced by the quoted path of the list file.</param>
  public static IReadOnlyList<string> Run(string toolName, string executable, string arguments, string workingDirectory,
    CancellationToken ct)
  {
    var listFile = Path.Combine(Path.GetTempPath(), "RiderTestsSupportPlus-" + Guid.NewGuid().ToString("N") + ".txt");
    try
    {
      var output = new StringBuilder();
      var errors = new StringBuilder();
      var startInfo = new ProcessStartInfo(executable, string.Format(arguments, Quote(listFile)))
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = workingDirectory
      };

      using (var process = new Process { StartInfo = startInfo })
      {
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) =>
        {
          Append(output, e.Data);
          Append(errors, e.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + Timeout;
        while (!process.WaitForExit(200))
        {
          if (ct.IsCancellationRequested || DateTime.UtcNow > deadline)
          {
            TryKill(process);
            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"{toolName} did not finish within {Timeout.TotalMinutes} minutes.");
          }
        }

        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(listFile))
          throw new InvalidOperationException($"{toolName} failed (exit code {process.ExitCode}):\n{Tail(output)}");
      }

      var names = File.ReadAllLines(listFile).Where(l => l.Length > 0).Distinct().ToList();
      // vstest exits with 0 and writes an empty list when the test host cannot start (e.g. a missing .NET runtime);
      // the only trace is on stderr. A filter that matches nothing leaves stderr empty.
      if (names.Count == 0 && errors.ToString().Trim().Length > 0)
        throw new InvalidOperationException($"{toolName} listed no tests and reported errors:\n{Tail(errors)}");
      return names;
    }
    finally
    {
      try { File.Delete(listFile); } catch { /* best effort */ }
    }
  }

  public static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

  private static void Append(StringBuilder output, string line)
  {
    if (line == null) return;
    lock (output) output.AppendLine(line);
  }

  private static string Tail(StringBuilder output)
  {
    lock (output)
    {
      var lines = output.ToString().Split('\n');
      return string.Join("\n", lines.Skip(Math.Max(0, lines.Length - 15))).Trim();
    }
  }

  private static void TryKill(Process process)
  {
    try { process.Kill(); } catch { /* already exited */ }
  }
}
