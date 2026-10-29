using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.Application.Settings;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DataContext;
using JetBrains.ProjectModel.Features.SolutionBuilders;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using JetBrains.ReSharper.UnitTestFramework.Exploration.Artifacts;
using JetBrains.ReSharper.UnitTestFramework.Persistence;
using JetBrains.ReSharper.UnitTestFramework.Settings;
using JetBrains.ReSharper.UnitTesting.VsTest.Provider;
using JetBrains.ReSharper.UnitTesting.VsTest.Provider.TestingPlatform;
using JetBrains.ReSharper.UnitTestProvider.nUnit;
using ReSharperPlugin.RiderTestsSupportPlus.Resolution;

namespace ReSharperPlugin.RiderTestsSupportPlus.Discovery;

/// <summary>
/// How Rider discovers the tests of the solution: what it actually discovered per project (the ground truth),
/// the settings that explain it, and what follows from them for resolving tests by name.
/// User configuration decides which provider owns a test, and thus its id and when it appears:
/// e.g. with the built-in NUnit provider disabled (RIDER-131305) NUnit tests come from VSTest with prefixed ids.
/// </summary>
public sealed class DiscoveryInfo
{
  public const string NUnitProviderId = "NUnit3x";
  public const string TestingPlatformProviderId = "Testing Platform";
  private static readonly string[] TestPackages =
    { "Microsoft.NET.Test.Sdk", "NUnit3TestAdapter", "NUnit", "xunit", "xunit.v3", "MSTest", "MSTest.TestAdapter", "TUnit" };

  public DiscoverySettings Settings { get; private set; }
  public List<TargetDiscovery> Projects { get; } = new();
  public bool BuildRunning { get; private set; }

  /// <summary>What the settings and the discovered tests mean for the tests an agent asks for, in plain words.</summary>
  public List<string> Implications { get; } = new();

  /// <summary>Missing and stale build outputs, and an IDE build in progress.</summary>
  [Newtonsoft.Json.JsonIgnore] public List<Problem> Problems { get; } = new();

  /// <param name="projectFilter">Projects to report; test projects only when null.</param>
  public static DiscoveryInfo Collect(ISolution solution, [CanBeNull] Func<IProject, bool> projectFilter = null)
  {
    var info = new DiscoveryInfo { Settings = DiscoverySettings.Read(solution) };
    info.BuildRunning = IsBuildRunning(solution);
    if (info.BuildRunning)
      info.Problems.Add(new Problem(ProblemCodes.BuildInProgress,
        "A build is running in Rider: tests appear in Rider's model and build outputs change only after it finishes."));

    var repository = solution.GetComponent<IUnitTestElementRepository>();
    var builds = new BuildOutput.Checker();
    foreach (var project in solution.GetAllProjects().Where(p => p.IsProjectFromUserView()).OrderBy(p => p.Name))
    {
      foreach (var targetFrameworkId in project.TargetFrameworkIds)
      {
        List<IUnitTestElement> elements;
        using (UT.ReadLock())
          elements = repository.GetBy(project, targetFrameworkId).ToList();
        if (projectFilter != null ? !projectFilter(project) : elements.Count == 0 && !ReferencesTestPackage(project))
          continue;

        var build = builds.Check(project, targetFrameworkId);
        var target = new TargetDiscovery(build, elements);
        info.Projects.Add(target);

        if (build.State == BuildOutputState.Missing)
          info.Problems.Add(new Problem(ProblemCodes.BuildOutputMissing, build.Describe() + ": build the solution.", build.Target));
        else if (build.State == BuildOutputState.Stale)
          info.Problems.Add(new Problem(ProblemCodes.BuildOutputStale, build.Describe() + ": build the solution.", build.Target));
      }
    }

    info.Implications.AddRange(info.Derive());
    return info;
  }

  public static bool IsBuildRunning(ISolution solution) => solution.GetComponent<BuildStateTracker>().BuildIsRunning.Value;

  /// <summary>A short form for tool answers that are not complete: the settings and only the problematic targets.</summary>
  public object Summary(IEnumerable<string> projectNames = null)
  {
    var names = projectNames == null ? null : new HashSet<string>(projectNames);
    return new
    {
      Settings,
      Projects = Projects.Where(p => names == null || names.Contains(p.Project)).ToList(),
      BuildRunning,
      Implications
    };
  }

  private static bool ReferencesTestPackage(IProject project)
  {
    try
    {
      return project.GetAllPackagesReferences().Any(r => TestPackages.Contains(r.Name, StringComparer.OrdinalIgnoreCase));
    }
    catch (Exception)
    {
      return false;
    }
  }

  private IEnumerable<string> Derive()
  {
    var s = Settings;
    if (!s.UnitTestingEnabled)
    {
      yield return "Unit testing is disabled in Rider (Settings | Build, Execution, Deployment | Unit Testing | Enable unit testing): " +
                   "no tests are discovered, so no test can be resolved.";
      yield break;
    }

    var nunitDisabled = s.DisabledProviders.Contains(NUnitProviderId);
    var nunitIgnoredByVsTest = s.VsTestIgnoresNUnitAdapter;
    var vsTestDisabled = s.DisabledProviders.Contains(VsTestIds.ProviderId);

    if (nunitDisabled && (nunitIgnoredByVsTest || vsTestDisabled))
      yield return "The built-in NUnit provider is disabled and VSTest " + (vsTestDisabled ? "is disabled too" : "ignores NUnit3TestAdapter") +
                   ": NUnit tests are not discovered at all. Enable one of them in Settings | Build, Execution, Deployment | Unit Testing.";
    else if (nunitDisabled)
      yield return "The built-in NUnit provider is disabled, NUnit tests are discovered by the VSTest provider through NUnit3TestAdapter " +
                   "(a common workaround for RIDER-131305). Their ids are the adapter's FullyQualifiedName prefixed with " +
                   "'executor://nunit3testexecutor/': '::' only for test cases generated from arguments (Ns.C(a).M(1)), '#' for " +
                   "everything else, including tests without arguments (#Ns.C.M), methods and classes. When a plain name is not found, " +
                   "'didYouMean' gives Rider's exact id. VSTest runs the adapter on the built assembly, " +
                   "so new tests appear only after a build.";
    else if (!nunitIgnoredByVsTest && !vsTestDisabled)
      yield return "Both the built-in NUnit provider and VSTest may discover NUnit tests (NUnit3TestAdapter is not ignored by VSTest): " +
                   "the same test can then exist twice, with the plain and with the prefixed id.";

    if (!nunitDisabled && Projects.Any(p => p.Providers.Any(x => x.Provider == NUnitProviderId)))
    {
      yield return s.NUnitDiscoveryMethod == nameof(DiscoveryMethod.Metadata)
        ? "The built-in NUnit provider discovers tests from the metadata of the built assembly: test cases created at run time " +
          "(TestCaseSource, ValueSource, generated arguments) may appear only after the tests run. Until then a request for such a case " +
          "is replaced by its method, which runs all of its cases."
        : "The built-in NUnit provider discovers tests by running NUnit's discovery on the built assembly: every case appears, " +
          "but only after the project is built.";
    }

    foreach (var both in Projects.Where(p => p.Providers.Any(x => x.Provider == NUnitProviderId) &&
                                             p.Providers.Any(x => x.Provider == VsTestIds.ProviderId)))
      yield return $"{both.Project} ({both.TargetFramework}): tests come from both NUnit3x and VsTest, each test exists twice.";

    foreach (var target in Projects.Where(p => p.Tests == 0 && p.NUnitAdapter && p.Build.State != nameof(BuildOutputState.Missing)))
      yield return $"{target.Project} ({target.TargetFramework}): NUnit3TestAdapter is next to the assembly, but Rider has discovered " +
                   "no tests: wait for discovery to finish, rebuild, or check the providers above.";

    if (!string.IsNullOrEmpty(s.DefaultRunSettingsFile))
      yield return $"Rider runs tests with the run settings {s.DefaultRunSettingsFile} (Unit Testing | Test Runner): its TestCaseFilter " +
                   "or NUnit Where also applies to sessions, which may run fewer tests than they show.";
    if (s.AutoDetectRunSettings)
      yield return "Rider picks up a .runsettings file next to the solution or project by itself (Unit Testing | Test Runner): " +
                   "its filters also apply to sessions, which may run fewer tests than they show.";
  }
}

/// <summary>Effective settings of the solution that decide how tests are discovered and run.</summary>
public sealed class DiscoverySettings
{
  public bool UnitTestingEnabled { get; private set; }

  /// <summary>Ids of disabled providers, e.g. <c>NUnit3x</c>, <c>VsTest</c>.</summary>
  public List<string> DisabledProviders { get; private set; }

  /// <summary>Executor URIs of adapters the VSTest provider leaves to other providers.</summary>
  public List<string> VsTestIgnoredExecutors { get; private set; }

  /// <summary><c>Metadata</c> or <c>TestRunner</c>.</summary>
  public string NUnitDiscoveryMethod { get; private set; }

  public bool TestingPlatformIgnoresProjectsOfOtherProviders { get; private set; }

  [CanBeNull] public string DefaultRunSettingsFile { get; private set; }
  public bool AutoDetectRunSettings { get; private set; }

  private const string NUnitExecutor = "executor://NUnit3TestExecutor/";
  private const string NUnitAdapterFile = "NUnit3.TestAdapter.dll";

  /// <summary>Whether VSTest leaves NUnit3TestAdapter to the built-in NUnit provider (Rider's default).</summary>
  public bool VsTestIgnoresNUnitAdapter { get; private set; }

  public static DiscoverySettings Read(ISolution solution)
  {
    var store = solution.GetComponent<ISettingsStore>().BindToContextTransient(ContextRange.Smart(solution.ToDataContext2()));
    var ignoredAdapters = Indexed(store, (VsTestProviderSettings x) => x.ExecutorExcludeList);
    var useRunSettingsFile = store.GetValue((UnitTestRunnerSettings x) => x.RunConfigurationFilename);
    return new DiscoverySettings
    {
      UnitTestingEnabled = store.GetValue((UnitTestingSettings x) => x.EnableUnitTesting),
      DisabledProviders = Indexed(store, (UnitTestingSettings x) => x.DisabledProviders)
        .Where(x => x.Value is true).Select(x => x.Key.ToString()).OrderBy(x => x).ToList(),
      VsTestIgnoredExecutors = ignoredAdapters.Select(x => x.Key.ToString()).OrderBy(x => x).ToList(),
      // VSTest matches the adapter file names (the values), the URIs only name them
      VsTestIgnoresNUnitAdapter = ignoredAdapters.Any(x =>
        string.Equals(x.Value as string, NUnitAdapterFile, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(x.Key as string, NUnitExecutor, StringComparison.OrdinalIgnoreCase)),
      NUnitDiscoveryMethod = store.GetValue((NUnitProviderSettings x) => x.TestDiscoveryFromArtifactsMethod).ToString(),
      TestingPlatformIgnoresProjectsOfOtherProviders =
        store.GetValue((TestingPlatformTestProviderSettings x) => x.IgnoreProjectsSupportedByOtherProviders),
      DefaultRunSettingsFile = string.IsNullOrWhiteSpace(useRunSettingsFile) ? null : useRunSettingsFile,
      AutoDetectRunSettings = store.GetValue((UnitTestRunnerSettings x) => x.AutoDetectRunConfigurationFile)
    };
  }

  private static IDictionary<object, object> Indexed<TKey, TEntry>(IContextBoundSettingsStore store,
    System.Linq.Expressions.Expression<Func<TKey, TEntry>> entry) =>
    store.EnumIndexedValues(store.Schema.GetIndexedEntry(entry), null);
}

/// <summary>What Rider discovered in one project and target framework, and its build output.</summary>
public sealed class TargetDiscovery
{
  public TargetDiscovery(BuildOutput build, IReadOnlyCollection<IUnitTestElement> elements)
  {
    Project = build.ProjectName;
    TargetFramework = build.TargetFrameworkId.PresentableString;
    Build = new BuildInfo(build);
    NUnitAdapter = build.AssemblyPath != null &&
                   File.Exists(Path.Combine(Path.GetDirectoryName(build.AssemblyPath) ?? "", "NUnit3.TestAdapter.dll"));

    using (UT.ReadLock())
    {
      Tests = elements.Count(e => !e.Children.Any());
      Providers = elements
        .GroupBy(e => (e.NaturalId.ProviderId, Executor(e.NaturalId.TestId)))
        .Select(g => new ProviderCount(g.Key.ProviderId, g.Key.Item2, g.Count(), g.Count(e => !e.Children.Any())))
        .OrderByDescending(x => x.Elements).ToList();
    }
  }

  public string Project { get; }
  public string TargetFramework { get; }
  public List<ProviderCount> Providers { get; }

  /// <summary>Elements without children: tests and test cases.</summary>
  public int Tests { get; }

  public bool NUnitAdapter { get; }
  public BuildInfo Build { get; }

  /// <summary>Executor URI of a VSTest id (<c>executor://nunit3testexecutor/</c>), null for other providers.</summary>
  [CanBeNull]
  private static string Executor(string testId)
  {
    if (!testId.StartsWith("executor://", StringComparison.OrdinalIgnoreCase))
      return null;
    var end = testId.IndexOf("/::", StringComparison.Ordinal);
    if (end < 0) end = testId.IndexOf("/#", StringComparison.Ordinal);
    return end < 0 ? null : testId.Substring(0, end + 1);
  }
}

public sealed class ProviderCount
{
  public ProviderCount(string provider, [CanBeNull] string executor, int elements, int tests)
  {
    Provider = provider;
    Executor = executor;
    Elements = elements;
    Tests = tests;
  }

  public string Provider { get; }
  [CanBeNull] public string Executor { get; }

  /// <summary>Every node: classes, methods and tests.</summary>
  public int Elements { get; }

  public int Tests { get; }
}

public sealed class BuildInfo
{
  public BuildInfo(BuildOutput build)
  {
    State = build.State.ToString();
    Assembly = build.AssemblyPath;
    BuiltAt = build.BuiltAt?.ToString("yyyy-MM-dd HH:mm:ss");
    NewerSource = build.NewestInput;
    NewerSourceChangedAt = build.NewestInputAt?.ToString("yyyy-MM-dd HH:mm:ss");
  }

  /// <summary><c>Fresh</c>, <c>Stale</c> or <c>Missing</c>.</summary>
  public string State { get; }

  [CanBeNull] public string Assembly { get; }
  [CanBeNull] public string BuiltAt { get; }
  [CanBeNull] public string NewerSource { get; }
  [CanBeNull] public string NewerSourceChangedAt { get; }
}
