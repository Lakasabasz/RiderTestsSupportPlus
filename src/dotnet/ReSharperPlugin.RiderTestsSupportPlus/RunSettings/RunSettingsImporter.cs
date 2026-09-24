using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.BuildTools;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Persistence;
using JetBrains.Application.Threading;
using JetBrains.Util;
using JetBrains.Util.Dotnet.TargetFrameworkIds;
using ReSharperPlugin.RiderTestsSupportPlus.Resolution;
using ReSharperPlugin.RiderTestsSupportPlus.Sessions;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>
/// Builds a session from the tests a <c>.runsettings</c> file selects: every test project's build output is listed by
/// <c>dotnet vstest</c> with the file's filter, and the fully qualified names are resolved against Rider's test model.
/// </summary>
public static class RunSettingsImporter
{
  private sealed class Target
  {
    public string ProjectId;
    public string ProjectName;
    public TargetFrameworkId TargetFrameworkId;
    public string AssemblyPath;
    /// <summary>
    /// NUnit3TestAdapter sits next to the assembly. The adapter, not Rider's test provider, decides how
    /// <c>Where</c> and <c>Category</c> are interpreted when <c>dotnet vstest</c> runs the tests.
    /// </summary>
    public bool IsNUnit;
  }

  /// <summary>Completes on the main thread, after the session was opened (or with notes explaining why none was).</summary>
  public static Task<SessionOpenResult> ImportAsync(ISolution solution, FileSystemPath runSettingsPath)
  {
    solution.Locks.AssertReadAccessAllowed();

    var dotnet = SolutionToolsetEx.GetDotNetCoreToolset(solution.GetComponent<ISolutionToolset>())?.Cli?.ExePath;
    if (dotnet == null)
    {
      return Task.FromResult(Failed("The .NET CLI is not configured (Settings | Build, Execution, Deployment | Toolset and Build)."));
    }

    var notes = new List<string>();
    var details = new List<string>();
    var targets = CollectTargets(solution, notes);
    if (targets.Count == 0)
    {
      notes.Insert(0, "No built test projects were found. Build the solution and try again.");
      return Task.FromResult(Failed(notes.ToArray()));
    }

    var lifetime = solution.GetSolutionLifetimes().UntilSolutionCloseLifetime;
    var dotnetPath = dotnet.FullPath;
    var settingsPath = runSettingsPath.FullPath;
    var sessionName = runSettingsPath.NameWithoutExtension;

    var completion = new TaskCompletionSource<SessionOpenResult>();
    var progress = BackgroundTask.Start(solution, "Importing tests from " + runSettingsPath.Name);
    Task.Run(() =>
    {
      var requests = new List<TestRequest>();
      var errors = new List<string>();
      string nunitSettingsPath = null;
      RunSettingsFile settings = null;
      try
      {
        settings = RunSettingsFile.Load(settingsPath);
        if (targets.Any(t => t.IsNUnit) && !settings.UsesNUnitWhere)
          nunitSettingsPath = NUnitFilterAliases.CreateAdjustedCopy(settingsPath);
      }
      catch (Exception e)
      {
        // Load Session and this action sit next to each other, a saved session is the likely wrong pick
        errors.Add(SessionFile.LooksLikeSessionFile(settingsPath)
          ? $"{runSettingsPath.Name} is a saved test session, not run settings: open it with Load Session (Tests Support Plus)."
          : "Could not read the run settings: " + e.Message);
        targets.Clear();
      }

      for (var i = 0; i < targets.Count && !progress.Token.IsCancellationRequested; i++)
      {
        var target = targets[i];
        progress.Report((double)i / targets.Count,
          $"Listing tests in {target.ProjectName} ({target.TargetFrameworkId.PresentableString}), {i + 1} of {targets.Count}");
        try
        {
          // Other adapters ignore <NUnit><Where>, so for them the file selects every test, as in `dotnet vstest`.
          var names = target.IsNUnit && settings.UsesNUnitWhere
            ? NUnitWhereListing.ListFullyQualifiedTests(dotnetPath, target.AssemblyPath, settingsPath, progress.Token)
            : VsTestListing.ListFullyQualifiedTests(dotnetPath, target.AssemblyPath,
              target.IsNUnit ? nunitSettingsPath ?? settingsPath : settingsPath, progress.Token);
          // A stale build silently yields stale tests, so say which build was listed.
          details.Add($"{target.ProjectName} ({target.TargetFrameworkId.PresentableString}): {names.Count} tests listed from " +
                    $"{target.AssemblyPath}, built {File.GetLastWriteTime(target.AssemblyPath):yyyy-MM-dd HH:mm}");
          foreach (var fqn in names)
          {
            requests.Add(new TestRequest(fqn)
            {
              ProjectId = target.ProjectId,
              ProjectName = target.ProjectName,
              TargetFrameworkId = target.TargetFrameworkId.UniqueString,
              AncestorIds = TestIdPath.AncestorCandidates(fqn)
            });
          }
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception e)
        {
          errors.Add($"{target.ProjectName} ({target.TargetFrameworkId.PresentableString}): {e.Message}");
        }
      }

      if (nunitSettingsPath != null)
      {
        try { File.Delete(nunitSettingsPath); } catch { /* best effort */ }
      }

      if (progress.Token.IsCancellationRequested)
      {
        progress.Dispose();
        completion.SetCanceled();
        return;
      }

      progress.Report(1, "Resolving tests");
      solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "RiderTestsSupportPlus.ImportRunSettings", () =>
      {
        // The rescan, if needed, shows its own progress.
        progress.Dispose();
        var allNotes = notes.Concat(errors).ToList();
        if (requests.Count == 0)
        {
          // Unreadable run settings select nothing, the error says why
          if (settings != null)
            allNotes.Insert(0, "The run settings select no tests.");
          completion.SetResult(Failed(allNotes.Concat(details).ToArray()));
          return;
        }

        SessionOpener.ResolveAndOpenAsync(solution, sessionName, requests, allNotes, details).ContinueWith(t =>
        {
          if (t.IsFaulted) completion.SetException(t.Exception!.InnerExceptions);
          else if (t.IsCanceled) completion.SetCanceled();
          else completion.SetResult(t.Result);
        }, TaskContinuationOptions.ExecuteSynchronously);
      });
    });
    return completion.Task;
  }

  private static SessionOpenResult Failed(params string[] notes) =>
    new(new TestResolutionResult(new TestResolution[0]), null, notes, new string[0], rescanned: false);

  /// <summary>Projects and target frameworks Rider knows tests for, with their build output.</summary>
  private static List<Target> CollectTargets(ISolution solution, List<string> notes)
  {
    var repository = solution.GetComponent<IUnitTestElementRepository>();
    var targets = new List<Target>();

    foreach (var project in solution.GetAllProjects().Where(p => p.IsProjectFromUserView()))
    {
      foreach (var targetFrameworkId in project.TargetFrameworkIds)
      {
        bool hasTests;
        using (UT.ReadLock())
          hasTests = repository.GetBy(project, targetFrameworkId).Any();
        if (!hasTests)
          continue;

        var output = project.GetOutputFilePath(targetFrameworkId);
        if (output == null || output.IsEmpty || !File.Exists(output.FullPath))
        {
          notes.Add($"Skipped {project.Name} ({targetFrameworkId.PresentableString}): no build output, build the project first.");
          continue;
        }

        targets.Add(new Target
        {
          ProjectId = project.GetPersistentID(),
          ProjectName = project.Name,
          TargetFrameworkId = targetFrameworkId,
          AssemblyPath = output.FullPath,
          IsNUnit = File.Exists(Path.Combine(Path.GetDirectoryName(output.FullPath) ?? "", "NUnit3.TestAdapter.dll"))
        });
      }
    }

    return targets;
  }
}
