using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.ProjectModel;
using JetBrains.Util.Dotnet.TargetFrameworkIds;

namespace ReSharperPlugin.RiderTestsSupportPlus.Discovery;

public enum BuildOutputState
{
  Fresh,

  /// <summary>A source of the project or of a project it references is newer than the assembly.</summary>
  Stale,

  Missing
}

/// <summary>
/// Whether the assembly of a project is built and newer than what it is built from. <c>dotnet vstest</c> and metadata
/// exploration read the assembly, so a missing or stale one silently yields no tests or outdated ones.
/// Staleness compares file times only, so it is a hint, not a verdict: a build that leaves the assembly untouched
/// (nothing to compile) keeps it "stale".
/// </summary>
public sealed class BuildOutput
{
  // File systems and copies round times, a newer source within this margin is not a change
  private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

  private static readonly HashSet<string> SourceExtensions =
    new(StringComparer.OrdinalIgnoreCase) { ".cs", ".vb", ".fs", ".fsi", ".resx", ".xaml" };

  private BuildOutput(string projectName, TargetFrameworkId targetFrameworkId, string assemblyPath, BuildOutputState state,
    DateTime? builtAt, string newestInput, DateTime? newestInputAt)
  {
    ProjectName = projectName;
    TargetFrameworkId = targetFrameworkId;
    AssemblyPath = assemblyPath;
    State = state;
    BuiltAt = builtAt;
    NewestInput = newestInput;
    NewestInputAt = newestInputAt;
  }

  public string ProjectName { get; }
  public TargetFrameworkId TargetFrameworkId { get; }

  /// <summary>Output of the active configuration; null when the project model does not know it.</summary>
  [CanBeNull] public string AssemblyPath { get; }

  public BuildOutputState State { get; }

  /// <summary>Local time.</summary>
  public DateTime? BuiltAt { get; }

  /// <summary>The newest source of the project or of a project it references, set when <see cref="State"/> is Stale.</summary>
  [CanBeNull] public string NewestInput { get; }

  public DateTime? NewestInputAt { get; }

  public string Target => $"{ProjectName} ({TargetFrameworkId.PresentableString})";

  public string Describe() => State switch
  {
    BuildOutputState.Missing => $"{Target}: no build output" + (AssemblyPath != null ? $" ({AssemblyPath} does not exist)" : ""),
    BuildOutputState.Stale => $"{Target}: {AssemblyPath} was built {BuiltAt:yyyy-MM-dd HH:mm:ss}, " +
                              $"but {NewestInput} changed later ({NewestInputAt:yyyy-MM-dd HH:mm:ss})",
    _ => $"{Target}: {AssemblyPath}, built {BuiltAt:yyyy-MM-dd HH:mm:ss}"
  };

  /// <summary>Checks build outputs, reading the sources of each project once.</summary>
  public sealed class Checker
  {
    private readonly Dictionary<(IProject, TargetFrameworkId), (string Path, DateTime Time)?> myNewestSources = new();

    public BuildOutput Check(IProject project, TargetFrameworkId targetFrameworkId)
    {
      var output = project.GetOutputFilePath(targetFrameworkId);
      var assemblyPath = output == null || output.IsEmpty ? null : output.FullPath;
      if (assemblyPath == null || !File.Exists(assemblyPath))
        return new BuildOutput(project.Name, targetFrameworkId, assemblyPath, BuildOutputState.Missing, null, null, null);

      var builtAt = File.GetLastWriteTime(assemblyPath);
      var newest = NewestSource(project, targetFrameworkId, new HashSet<IProject>());
      return newest != null && newest.Value.Time > builtAt + Tolerance
        ? new BuildOutput(project.Name, targetFrameworkId, assemblyPath, BuildOutputState.Stale, builtAt, newest.Value.Path, newest.Value.Time)
        : new BuildOutput(project.Name, targetFrameworkId, assemblyPath, BuildOutputState.Fresh, builtAt, null, null);
    }

    /// <summary>The newest source file or project file of the project and of the projects it references, transitively.</summary>
    private (string Path, DateTime Time)? NewestSource(IProject project, TargetFrameworkId targetFrameworkId, HashSet<IProject> visited)
    {
      if (!visited.Add(project))
        return null;
      if (myNewestSources.TryGetValue((project, targetFrameworkId), out var cached))
        return cached;

      var files = project.GetAllProjectFiles(f => SourceExtensions.Contains(f.Location.ExtensionWithDot))
        .Select(f => f.Location.FullPath)
        .Append(project.ProjectFileLocation?.FullPath);

      (string Path, DateTime Time)? newest = null;
      foreach (var path in files)
      {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
          continue;
        var time = File.GetLastWriteTime(path);
        if (newest == null || time > newest.Value.Time)
          newest = (path, time);
      }

      foreach (var referenced in project.GetReferencedProjects(targetFrameworkId))
      {
        // A referenced project builds for its own nearest framework, any of them stands for its sources
        var referencedNewest = NewestSource(referenced, referenced.TargetFrameworkIds.FirstOrDefault() ?? targetFrameworkId, visited);
        if (referencedNewest != null && (newest == null || referencedNewest.Value.Time > newest.Value.Time))
          newest = referencedNewest;
      }

      myNewestSources[(project, targetFrameworkId)] = newest;
      return newest;
    }
  }
}
