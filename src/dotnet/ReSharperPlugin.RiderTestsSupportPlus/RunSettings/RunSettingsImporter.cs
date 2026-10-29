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
using ReSharperPlugin.RiderTestsSupportPlus.Discovery;
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
    return ListAsync(solution, runSettingsPath).ContinueWith(t =>
    {
      var listing = t.Result;
      var notes = listing.Problems.Select(x => x.Message).ToList();
      if (listing.Requests.Count == 0)
        return Task.FromResult(Failed(notes.Concat(listing.Details).ToArray()));
      // The rescan, if needed, shows its own progress.
      return SessionOpener.ResolveAndOpenAsync(solution, listing.SessionName, listing.Requests, notes, listing.Details);
    }, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
  }

  /// <summary>
  /// Lists the tests the file selects in every built test project. Completes on the main thread;
  /// cancelled when the user cancels the background task.
  /// </summary>
  public static Task<RunSettingsListing> ListAsync(ISolution solution, FileSystemPath runSettingsPath)
  {
    solution.Locks.AssertReadAccessAllowed();

    var listing = new RunSettingsListing(runSettingsPath.NameWithoutExtension);
    var dotnet = SolutionToolsetEx.GetDotNetCoreToolset(solution.GetComponent<ISolutionToolset>())?.Cli?.ExePath;
    if (dotnet == null)
    {
      listing.Problems.Add(new Problem(ProblemCodes.DotnetCliMissing,
        "The .NET CLI is not configured (Settings | Build, Execution, Deployment | Toolset and Build)."));
      return Task.FromResult(listing);
    }

    var targets = CollectTargets(solution, listing);
    if (targets.Count == 0)
    {
      listing.Problems.Insert(0, new Problem(ProblemCodes.NoBuiltTestProjects, "No built test projects were found. Build the solution and try again."));
      return Task.FromResult(listing);
    }

    var lifetime = solution.GetSolutionLifetimes().UntilSolutionCloseLifetime;
    var dotnetPath = dotnet.FullPath;
    var settingsPath = runSettingsPath.FullPath;

    var completion = new TaskCompletionSource<RunSettingsListing>();
    var progress = BackgroundTask.Start(solution, "Importing tests from " + runSettingsPath.Name);
    Task.Run(() =>
    {
      var errors = new List<Problem>();
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
          ? new Problem(ProblemCodes.WrongFileKind,
            $"{runSettingsPath.Name} is a saved test session, not run settings: open it with Load Session (Tests Support Plus).")
          : new Problem(ProblemCodes.RunSettingsUnreadable, "Could not read the run settings: " + e.Message));
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
          listing.Details.Add($"{target.ProjectName} ({target.TargetFrameworkId.PresentableString}): {names.Count} tests listed from " +
                              $"{target.AssemblyPath}, built {File.GetLastWriteTime(target.AssemblyPath):yyyy-MM-dd HH:mm}");
          foreach (var fqn in names)
          {
            listing.Requests.Add(new TestRequest(fqn)
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
          var project = $"{target.ProjectName} ({target.TargetFrameworkId.PresentableString})";
          errors.Add(new Problem(ProblemCodes.ListingFailed, $"{project}: {e.Message}", project));
        }
      }

      if (nunitSettingsPath != null)
      {
        try { File.Delete(nunitSettingsPath); } catch { /* best effort */ }
      }

      progress.Dispose();
      if (progress.Token.IsCancellationRequested)
      {
        completion.SetCanceled();
        return;
      }

      solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "RiderTestsSupportPlus.ImportRunSettings", () =>
      {
        listing.Problems.AddRange(errors);
        // Unreadable run settings select nothing, the error says why
        if (listing.Requests.Count == 0 && settings != null)
          listing.Problems.Insert(0, new Problem(ProblemCodes.NoTestsSelected, "The run settings select no tests."));
        completion.SetResult(listing);
      });
    });
    return completion.Task;
  }

  private static SessionOpenResult Failed(params string[] notes) =>
    new(new TestResolutionResult(new TestResolution[0]), null, notes, new string[0], rescanned: false);

  /// <summary>Projects and target frameworks Rider knows tests for, with their build output.</summary>
  private static List<Target> CollectTargets(ISolution solution, RunSettingsListing listing)
  {
    var repository = solution.GetComponent<IUnitTestElementRepository>();
    var builds = new BuildOutput.Checker();
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

        var build = builds.Check(project, targetFrameworkId);
        listing.Builds.Add(build);
        if (build.State == BuildOutputState.Missing)
        {
          listing.Problems.Add(new Problem(ProblemCodes.BuildOutputMissing,
            $"Skipped {build.Target}: no build output, build the project first.", build.Target));
          continue;
        }

        if (build.State == BuildOutputState.Stale)
        {
          listing.Problems.Add(new Problem(ProblemCodes.BuildOutputStale,
            $"{build.Describe()}: the tests are listed from the old build, build the solution to list the current ones.", build.Target));
        }

        targets.Add(new Target
        {
          ProjectId = project.GetPersistentID(),
          ProjectName = project.Name,
          TargetFrameworkId = targetFrameworkId,
          AssemblyPath = build.AssemblyPath,
          IsNUnit = File.Exists(Path.Combine(Path.GetDirectoryName(build.AssemblyPath) ?? "", "NUnit3.TestAdapter.dll"))
        });
      }
    }

    return targets;
  }
}

/// <summary>The tests a <c>.runsettings</c> file selects, before they are resolved against Rider's test model.</summary>
public sealed class RunSettingsListing
{
  public RunSettingsListing(string sessionName)
  {
    SessionName = sessionName;
  }

  public string SessionName { get; }

  public List<TestRequest> Requests { get; } = new();

  /// <summary>Problems that make the listing incomplete or empty, in the order to report them.</summary>
  public List<Problem> Problems { get; } = new();

  /// <summary>Which build each project was listed from.</summary>
  public List<string> Details { get; } = new();

  /// <summary>Build outputs of the test projects, listed or not.</summary>
  public List<BuildOutput> Builds { get; } = new();
}
