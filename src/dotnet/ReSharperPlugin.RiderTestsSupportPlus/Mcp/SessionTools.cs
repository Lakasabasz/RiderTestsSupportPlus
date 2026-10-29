using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Criteria;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using JetBrains.ReSharper.UnitTestFramework.Persistence;
using JetBrains.ReSharper.UnitTestFramework.Session;
using JetBrains.ReSharper.UnitTestFramework.UI.Session;
using JetBrains.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReSharperPlugin.RiderTestsSupportPlus.Discovery;
using ReSharperPlugin.RiderTestsSupportPlus.Resolution;
using ReSharperPlugin.RiderTestsSupportPlus.RunSettings;
using ReSharperPlugin.RiderTestsSupportPlus.Sessions;

namespace ReSharperPlugin.RiderTestsSupportPlus.Mcp;

/// <summary>
/// The MCP tools for unit test sessions: create, read, update and delete sessions, save them, and explain how tests are
/// discovered. The frontend declares the tools (names, parameters, descriptions) and forwards each call here; every
/// answer, including every error, is a <see cref="ToolResponse"/>. Nothing here shows a dialog or a message box.
/// </summary>
public sealed class SessionTools
{
  // Declared by TestSessionToolset in the frontend, keep the names and parameters in sync.
  public const string List = "test_session_list";
  public const string Get = "test_session_get";
  public const string Create = "test_session_create";
  public const string Update = "test_session_update";
  public const string Delete = "test_session_delete";
  public const string Save = "test_session_save";
  public const string Discovery = "test_discovery_info";

  // Rider's own MCP tools
  private const string BuildStart = "build_solution_start";
  private const string BuildState = "build_solution_state";

  private const string IfExistsError = "error";
  private const string IfExistsReplace = "replace";

  /// <summary>An agent's call should not wait for a rescan of a big solution; tests found later join the session by themselves.</summary>
  private static readonly TimeSpan RescanTimeout = TimeSpan.FromSeconds(60);

  private const int MaxListed = 200;
  private const int MaxTestsInGet = 1000;

  private readonly ISolution mySolution;
  private readonly string myTool;
  private readonly JObject myArguments;
  private readonly ToolResponse myResponse;

  private SessionTools(ISolution solution, string tool, JObject arguments, ToolResponse response)
  {
    mySolution = solution;
    myTool = tool;
    myArguments = arguments;
    myResponse = response;
  }

  /// <summary>Runs a tool and returns its answer as JSON. Never throws: failures are answers too.</summary>
  public static async Task<string> HandleAsync(ISolution solution, string tool, string argumentsJson)
  {
    var response = new ToolResponse();
    try
    {
      JObject arguments;
      try
      {
        arguments = string.IsNullOrWhiteSpace(argumentsJson) ? new JObject() : JObject.Parse(argumentsJson);
      }
      catch (JsonException e)
      {
        throw new ToolException(ProblemCodes.InvalidArguments, "The arguments are not a JSON object: " + e.Message);
      }

      await new SessionTools(solution, tool, arguments, response).RunAsync().ConfigureAwait(false);
    }
    catch (ToolException e)
    {
      response.Error = new ToolProblem(e.Code, e.Message);
      response.Result ??= e.Result;
      response.Suggest(e.Suggestions.ToArray());
    }
    catch (OperationCanceledException)
    {
      response.Error = new ToolProblem(ProblemCodes.Cancelled, "The user cancelled the operation in Rider's background tasks.");
    }
    catch (Exception e)
    {
      response.Error = new ToolProblem(ProblemCodes.Internal, $"{e.GetType().Name}: {e.Message}");
      response.Suggest(Suggestion.AskUser("Report the error to the author of Tests Support Plus, with Rider's log (Help | Show Log)",
        "this is a bug of the plugin, not of the arguments"));
    }

    return response.ToJson();
  }

  private Task RunAsync()
  {
    switch (myTool)
    {
      case List:
        return Locked(ListSessions);
      case Get:
        return Locked(GetSession);
      case Create:
        return CreateAsync();
      case Update:
        return UpdateAsync();
      case Delete:
        return Locked(DeleteSession);
      case Save:
        return Locked(SaveSession);
      case Discovery:
        return Locked(DiscoveryInfoTool);
      default:
        throw new ToolException(ProblemCodes.InvalidArguments, $"Unknown tool '{myTool}'.");
    }
  }

  #region Tools

  private void ListSessions()
  {
    var sessions = Sessions();
    AgentSessions.Retain(mySolution, id => sessions.Any(s => s.Id == id));
    myResponse.Result = new { sessions = sessions.Select(Describe).ToList() };
  }

  private void GetSession()
  {
    var session = FindSession();
    var tests = SessionFile.From(session).Tests;
    myResponse.Result = new
    {
      session = Describe(session),
      tests = tests.Take(MaxTestsInGet).Select(t => new
      {
        testId = VsTestIds.ToFrameworkId(t.TestId),
        // Only when Rider's id differs, i.e. the VSTest provider owns the test
        riderId = VsTestIds.IsVsTestId(t.TestId) ? t.TestId : null,
        project = t.ProjectName,
        targetFramework = t.TargetFramework,
        provider = t.ProviderId
      }).ToList(),
      truncated = tests.Count > MaxTestsInGet ? (bool?)true : null
    };

    // Tests of a stale build may be gone or renamed, say so where the agent looks at them
    var projects = new HashSet<string>(tests.Select(t => t.ProjectName).Where(x => x != null));
    if (projects.Count > 0)
      ReportBuildProblems(DiscoveryInfo.Collect(mySolution, p => projects.Contains(p.Name)));
  }

  private async Task CreateAsync()
  {
    var tests = Texts("tests");
    var runSettings = Text("runSettingsPath");
    var sessionFile = Text("sessionFilePath");
    var name = Text("name");
    var project = Text("project");
    var ifExists = Text("ifExists") ?? IfExistsError;

    if (new object[] { tests, runSettings, sessionFile }.Count(x => x != null) != 1)
    {
      throw new ToolException(ProblemCodes.InvalidArguments,
        "Pass exactly one source of tests: 'tests' (test ids), 'runSettingsPath' (a .runsettings file) or 'sessionFilePath' (a .rtsession file).",
        Suggestion.Call(Create, new { name = name ?? "My tests", tests = new[] { "Ns.FixtureClass.TestMethod", "Ns.FixtureClass(\"arg\").TestMethod(1)" } },
          "a session from test ids, as NUnit names them (Rider's VSTest ids work too)"));
    }

    if (ifExists != IfExistsError && ifExists != IfExistsReplace)
      throw new ToolException(ProblemCodes.InvalidArguments, $"'ifExists' must be '{IfExistsError}' or '{IfExistsReplace}', not '{ifExists}'.",
        Suggestion.Call(Create, WithArgument("ifExists", IfExistsReplace), "replaces the tests of a session with the same name"));

    if (tests != null && name == null)
      throw new ToolException(ProblemCodes.InvalidArguments, "'name' is required with 'tests': it is the title of the session's tab.",
        Suggestion.Call(Create, WithArgument("name", "Agent: " + TestIdPath.StripArguments(TestIdPath.Split(VsTestIds.ToFrameworkId(tests[0])).Last())),
          "names the session after its first test"));

    await Locked(() =>
    {
      CheckUnitTestingEnabled();
      if (project != null) CheckProject(project);
    });

    List<TestRequest> requests;
    object listedFrom = null;
    IUnitTestSession existing;
    if (tests != null)
    {
      requests = Requests(tests, project, "tests");
      existing = await Locked(() => ExistingSession(name, ifExists));
    }
    else if (sessionFile != null)
    {
      var path = ExistingFile(sessionFile, "sessionFilePath", SessionFile.Extension);
      if (SessionFile.IsRunSettings(path))
        throw new ToolException(ProblemCodes.WrongFileKind, $"{path} is a .runsettings file, not a saved session.",
          Suggestion.Call(Create, WithArgument("sessionFilePath", null, ("runSettingsPath", sessionFile)), "creates the session from the tests the run settings select"));

      SessionFile file;
      try
      {
        file = SessionFile.LoadChecked(FileSystemPath.Parse(path));
      }
      catch (InvalidDataException e)
      {
        throw new ToolException(ProblemCodes.SessionFileUnreadable, e.Message);
      }

      name ??= file.Name ?? Path.GetFileNameWithoutExtension(path);
      requests = file.ToRequests();
      if (requests.Count == 0)
        throw new ToolException(ProblemCodes.NoTestsSelected, $"{path} contains no tests.");
      existing = await Locked(() => ExistingSession(name, ifExists));
    }
    else
    {
      var path = ExistingFile(runSettings, "runSettingsPath", "runsettings");
      if (SessionFile.LooksLikeSessionFile(path))
        throw new ToolException(ProblemCodes.WrongFileKind, $"{path} is a saved session, not run settings.",
          Suggestion.Call(Create, WithArgument("runSettingsPath", null, ("sessionFilePath", runSettings)), "creates the session from the saved one"));

      name ??= Path.GetFileNameWithoutExtension(path);
      // Before listing: it runs dotnet vstest on every test project, which takes a while
      existing = await Locked(() => ExistingSession(name, ifExists));
      var listing = await (await Locked(() =>
      {
        if (DiscoveryInfo.IsBuildRunning(mySolution))
          throw new ToolException(ProblemCodes.BuildInProgress,
            "A build is running in Rider: the test assemblies are being written, listing them now would fail or list the old tests.",
            Suggestion.Call(BuildState, new { rootFolder = SolutionDirectory }, "poll until 'state' is Completed"), Retry("retry after the build"));
        return RunSettingsImporter.ListAsync(mySolution, FileSystemPath.Parse(path));
      })).ConfigureAwait(false);

      ReportListing(listing);
      requests = listing.Requests;
      listedFrom = listing.Details;
    }

    var outcome = await ResolveAsync(requests);
    await Locked(() =>
    {
      var resolution = ReportResolution(outcome, tests != null ? "tests" : null);
      if (outcome.Result.Items.All(x => x.Kind == TestResolutionKind.Unresolved))
      {
        throw new ToolException(ProblemCodes.NoTestsResolved,
          $"None of the {requests.Count} tests was found in Rider's test model, so no session was " + (existing != null ? "changed." : "created."))
        {
          Result = new { resolution, listedFrom }
        };
      }

      var created = existing == null || !existing.Lifetime.IsAlive;
      IUnitTestSession session;
      if (created)
      {
        session = mySolution.GetComponent<IUnitTestSessionRepository>().CreateSession(outcome.Result.CreateCriterion(), name);
        AgentSessions.Add(mySolution, session.Id);
        mySolution.GetComponent<IUnitTestSessionConductor>().OpenSession(session);
      }
      else
      {
        session = existing;
        session.Criterion.Value = outcome.Result.CreateCriterion();
      }

      myResponse.Result = new { session = Describe(session), created, resolution, listedFrom };
    });
  }

  private async Task UpdateAsync()
  {
    var tests = Texts("tests");
    var addTests = Texts("addTests");
    var removeTests = Texts("removeTests");
    var newName = Text("name");
    var project = Text("project");

    if (tests != null && (addTests != null || removeTests != null))
      throw new ToolException(ProblemCodes.InvalidArguments,
        "Pass either 'tests' (replaces all tests of the session) or 'addTests'/'removeTests' (change some), not both.");
    if (tests == null && addTests == null && removeTests == null && newName == null)
      throw new ToolException(ProblemCodes.InvalidArguments, "Nothing to change: pass 'tests', 'addTests', 'removeTests' or 'name'.",
        Suggestion.Call(Get, new { session = Text("session") }, "shows the tests the session has now"));

    var session = await Locked(() =>
    {
      CheckUnitTestingEnabled();
      var s = FindSession();
      CheckCanChange(s);
      if (newName != null && Sessions().Any(x => x != s && x.Name.Value == newName))
        throw new ToolException(ProblemCodes.SessionExists, $"Another session is already named '{newName}'.",
          Suggestion.Call(List, null, "shows the names in use"));
      if (project != null) CheckProject(project);
      return s;
    });

    var replaced = tests != null ? await ResolveAsync(Requests(tests, project, "tests")) : null;
    var added = addTests != null ? await ResolveAsync(Requests(addTests, project, "addTests")) : null;

    await Locked(() =>
    {
      if (!session.Lifetime.IsAlive)
        throw new ToolException(ProblemCodes.SessionNotFound, "The session was closed while its tests were being resolved.",
          Suggestion.Call(List, null, "shows the sessions that exist now"));
      CheckCanChange(session);

      object replacedReport = null, addedReport = null, removedReport = null;
      if (replaced != null)
      {
        replacedReport = ReportResolution(replaced, "tests");
        if (replaced.Result.Items.All(x => x.Kind == TestResolutionKind.Unresolved))
          throw new ToolException(ProblemCodes.NoTestsResolved, "None of the tests was found in Rider's test model, the session was not changed.")
            { Result = new { session = Describe(session), resolution = replacedReport } };
        session.Criterion.Value = replaced.Result.CreateCriterion();
      }

      if (added != null)
      {
        addedReport = ReportResolution(added, "addTests");
        var nothingAdded = added.Result.Items.All(x => x.Kind == TestResolutionKind.Unresolved);
        if (nothingAdded && removeTests == null && newName == null)
          throw new ToolException(ProblemCodes.NoTestsResolved, "None of the tests to add was found in Rider's test model, the session was not changed.")
            { Result = new { session = Describe(session), added = addedReport } };
        if (!nothingAdded)
          session.AddElementsMatching(added.Result.CreateCriterion());
      }

      if (removeTests != null)
        removedReport = RemoveTests(session, removeTests, project);

      if (newName != null)
        session.Name.Value = newName;

      myResponse.Result = new { session = Describe(session), replaced = replacedReport, added = addedReport, removed = removedReport };
    });
  }

  private void DeleteSession()
  {
    var session = FindSession();
    CheckCanChange(session);
    var described = Describe(session);
    // Destroying also closes the session's tab and removes it from the state Rider restores on start
    mySolution.GetComponent<IUnitTestSessionRepository>().DestroySession(session);
    AgentSessions.Remove(mySolution, session.Id);
    myResponse.Result = new { deleted = described };
  }

  private void SaveSession()
  {
    var session = FindSession();
    var path = Text("path") ?? throw new ToolException(ProblemCodes.InvalidArguments,
      "'path' is required: the .rtsession file to write, absolute or relative to the solution directory.",
      Suggestion.Call(Save, WithArgument("path", FileNameOf(session.Name.Value) + "." + SessionFile.Extension), "saves next to the solution"));

    var fullPath = ResolvePath(path);
    if (!fullPath.EndsWith("." + SessionFile.Extension, StringComparison.OrdinalIgnoreCase))
      fullPath += "." + SessionFile.Extension;
    var directory = Path.GetDirectoryName(fullPath);
    if (directory == null || !Directory.Exists(directory))
      throw new ToolException(ProblemCodes.FileNotFound, $"The directory {directory} does not exist.",
        Suggestion.Call(Save, WithArgument("path", Path.GetFileName(fullPath)), "saves next to the solution"));
    if (File.Exists(fullPath) && !Flag("overwrite"))
      throw new ToolException(ProblemCodes.FileExists, $"{fullPath} already exists.",
        Suggestion.Call(Save, WithArgument("overwrite", true), "replaces the file, if that is what the user wants"));

    var file = SessionFile.From(session);
    try
    {
      file.Save(fullPath);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      throw new ToolException(ProblemCodes.SaveFailed, $"Could not write {fullPath}: {e.Message}");
    }

    myResponse.Result = new { path = fullPath, tests = file.Tests.Count, session = Describe(session) };
  }

  private void DiscoveryInfoTool()
  {
    var project = Text("project");
    if (project != null) CheckProject(project);
    var info = DiscoveryInfo.Collect(mySolution, project == null ? null : p => p.Name == project);
    myResponse.Result = info;
    ReportBuildProblems(info);
    ReportSettingsProblems(info.Settings);
  }

  #endregion

  #region Sessions

  private List<IUnitTestSession> Sessions() =>
    mySolution.GetComponent<IUnitTestSessionRepository>().Query().ToList().OrderBy(s => s.Name.Value).ToList();

  /// <summary>The session named by the <c>session</c> argument: its id, or its name if that is unique.</summary>
  private IUnitTestSession FindSession()
  {
    var handle = Text("session") ?? throw new ToolException(ProblemCodes.InvalidArguments,
      "'session' is required: the id (preferred) or the name of a session.",
      Suggestion.Call(List, null, "lists the sessions with their ids"));

    var sessions = Sessions();
    if (Guid.TryParse(handle, out var id) && sessions.FirstOrDefault(s => s.Id == id) is { } byId)
      return byId;

    var byName = sessions.Where(s => s.Name.Value == handle).ToList();
    if (byName.Count == 0)
      byName = sessions.Where(s => string.Equals(s.Name.Value, handle, StringComparison.OrdinalIgnoreCase)).ToList();
    if (byName.Count == 1)
      return byName[0];

    if (byName.Count > 1)
    {
      throw new ToolException(ProblemCodes.SessionAmbiguous, $"{byName.Count} sessions are named '{handle}': pass the id of one of them.",
        byName.Select(s => Suggestion.Call(myTool, WithArgument("session", s.Id.ToString()), $"the one with {TestCount(s)} tests")).ToArray())
      {
        Result = new { sessions = byName.Select(Describe).ToList() }
      };
    }

    throw new ToolException(ProblemCodes.SessionNotFound, $"No unit test session has the id or name '{handle}'.",
      sessions.Count > 0
        ? new[] { Suggestion.Call(myTool, WithArgument("session", sessions[0].Id.ToString()), "pick the right one from 'sessions'") }
        : new[] { Suggestion.Call(Create, null, "there are no sessions, create one") })
    {
      Result = new { sessions = sessions.Take(MaxListed).Select(Describe).ToList() }
    };
  }

  /// <summary>The session <c>create</c> would collide with, or null; throws unless replacing it is asked for and allowed.</summary>
  [CanBeNull]
  private IUnitTestSession ExistingSession(string name, string ifExists)
  {
    var same = Sessions().Where(s => s.Name.Value == name).ToList();
    if (same.Count == 0)
      return null;

    if (ifExists == IfExistsError)
    {
      var suggestions = new List<Suggestion>
      {
        Suggestion.Call(Create, WithArgument("ifExists", IfExistsReplace), "replaces its tests with these, keeping the session and its tab")
      };
      var tests = Texts("tests");
      if (tests != null)
        suggestions.Add(Suggestion.Call(Update, new { session = same[0].Id.ToString(), addTests = tests }, "adds these tests to it instead"));
      suggestions.Add(Suggestion.Call(Create, WithArgument("name", name + " (2)"), "a separate session, only if the user wants two"));
      throw new ToolException(ProblemCodes.SessionExists,
        $"A session named '{name}' already exists. Reuse it instead of creating another tab with the same name.", suggestions.ToArray())
      {
        Result = new { existing = same.Select(Describe).ToList() }
      };
    }

    if (same.Count > 1)
      throw new ToolException(ProblemCodes.SessionAmbiguous, $"{same.Count} sessions are named '{name}', it is not clear which one to replace.",
        same.Select(s => Suggestion.Call(Update, new { session = s.Id.ToString(), tests = Texts("tests") }, $"replaces the tests of the one with {TestCount(s)} tests")).ToArray());

    CheckCanChange(same[0]);
    return same[0];
  }

  /// <summary>Agents change only their own sessions, idle and unlocked, unless the user's session is explicitly allowed.</summary>
  private void CheckCanChange(IUnitTestSession session)
  {
    if (!AgentSessions.IsOwned(mySolution, session.Id) && !Flag("allowUserSession"))
    {
      throw new ToolException(ProblemCodes.SessionNotOwned,
        $"The session '{session.Name.Value}' belongs to the user, it was not created by an agent. Change or delete it only when the user asked for it.",
        Suggestion.Call(Create, null, "work on a session of your own"),
        Suggestion.Call(myTool, WithArgument("allowUserSession", true), "only when the user asked to change this very session"));
    }

    if (session.IsLocked.Value)
      throw new ToolException(ProblemCodes.SessionLocked, $"The session '{session.Name.Value}' is locked.",
        Suggestion.AskUser("Unlock the session in the Unit Tests window (the lock button of its tab)", "a locked session keeps its tests unchanged on purpose"));

    if (!session.IsIdle.Value)
      throw new ToolException(ProblemCodes.SessionBusy, $"Tests of the session '{session.Name.Value}' are running.",
        Suggestion.Call(Get, new { session = session.Id.ToString() }, "check until 'running' is false"),
        Retry("retry once the run has finished"));
  }

  private object Describe(IUnitTestSession session) => new
  {
    id = session.Id.ToString(),
    name = session.Name.Value,
    tests = TestCount(session),
    ownedByAgent = AgentSessions.IsOwned(mySolution, session.Id),
    active = session.IsActive.Value,
    running = !session.IsIdle.Value,
    locked = session.IsLocked.Value
  };

  /// <summary>Tests without children in the session, the way they are saved.</summary>
  private static int TestCount(IUnitTestSession session) => SessionFile.From(session).Tests.Count;

  private object RemoveTests(IUnitTestSession session, List<string> ids, [CanBeNull] string project)
  {
    List<IUnitTestElement> elements;
    using (UT.ReadLock())
      elements = session.Elements.ToList();

    var byId = elements.ToLookup(e => VsTestIds.ToFrameworkId(e.NaturalId.TestId));
    var naturalIds = new HashSet<UnitTestElementId>();
    var removed = new List<string>();
    var notInSession = new List<object>();
    SimilarTests similar = null;
    foreach (var id in ids)
    {
      var matches = byId[VsTestIds.ToFrameworkId(id)].Where(e => project == null || e.Project?.Name == project).ToList();
      if (matches.Count > 0)
      {
        naturalIds.UnionWith(matches.Select(x => x.NaturalId));
        removed.Add(id);
        continue;
      }

      similar ??= new SimilarTests(elements);
      notInSession.Add(new { test = id, didYouMean = similar.Meant(id).Concat(similar.SameName(id)).Distinct().ToList() });
    }

    // With its descendants and with tests that appear under it later, as Rider's own "Remove from session" does
    if (naturalIds.Count > 0)
      session.ExcludeElementsMatching(new TestAncestorCriterion(naturalIds));

    if (notInSession.Count > 0)
      myResponse.Warn(ProblemCodes.NotInSession, $"{notInSession.Count} of the tests to remove are not in the session, see 'removed.notInSession'.",
        Suggestion.Call(Get, new { session = session.Id.ToString() }, "lists the session's tests with their exact ids"));

    return new { removed, notInSession };
  }

  #endregion

  #region Resolution and reporting

  private Task<ResolveOutcome> ResolveAsync(IReadOnlyList<TestRequest> requests) =>
    Locked(() => SessionOpener.ResolveAsync(mySolution, requests, RescanTimeout)).Unwrap();

  private List<TestRequest> Requests(List<string> ids, [CanBeNull] string project, string argument)
  {
    if (ids.Count == 0)
      throw new ToolException(ProblemCodes.InvalidArguments, $"'{argument}' is empty.");

    return ids.Distinct().Select(id => new TestRequest(id)
    {
      ProjectName = project,
      AncestorIds = TestIdPath.AncestorCandidates(VsTestIds.ToFrameworkId(id))
    }).ToList();
  }

  /// <param name="argument">The argument the test ids came from, to suggest a corrected call; null for files.</param>
  private object ReportResolution(ResolveOutcome outcome, [CanBeNull] string argument)
  {
    var result = outcome.Result;
    var exact = result.Exact.ToList();
    var ancestors = result.Ancestors.ToList();
    var unresolved = result.Unresolved.ToList();

    // A typo in an argument (ParamFixture(A) for ParamFixture("A")) still finds the class, so replaced tests get hints too
    var similar = unresolved.Count > 0 || ancestors.Count > 0
      ? SimilarTests.InRepository(mySolution.GetComponent<IUnitTestElementRepository>())
      : null;
    var unresolvedReport = unresolved.Take(MaxListed).Select(x =>
    {
      var meant = similar!.Meant(x.Request.TestId);
      return new
      {
        test = x.Request.TestId,
        project = x.Request.ProjectName,
        didYouMean = meant,
        sameName = meant.Count == 0 ? similar.SameName(x.Request.TestId) : null
      };
    }).ToList();
    var replacedReport = ancestors.Take(MaxListed).Select(x => new
    {
      test = x.Request.TestId,
      addedInstead = x.Element.NaturalId.TestId,
      didYouMean = similar!.Meant(x.Request.TestId) is { Count: > 0 } ids ? ids : null
    }).ToList();

    var corrections = unresolvedReport.Where(x => x.didYouMean.Count > 0).Select(x => (x.test, meant: x.didYouMean[0]))
      .Concat(replacedReport.Where(x => x.didYouMean != null).Select(x => (x.test, meant: x.didYouMean[0])))
      .ToList();
    var correctedCall = argument != null && corrections.Count > 0
      ? Suggestion.Call(myTool, WithArgument(argument,
          Texts(argument)!.Select(id => corrections.FirstOrDefault(c => c.test == id).meant ?? id).ToList(), RetryArguments()),
        "the same call with the ids from 'didYouMean' (ids are case-sensitive and string arguments are quoted, as NUnit names them)")
      : null;

    if (ancestors.Count > 0)
    {
      var typos = replacedReport.Count(x => x.didYouMean != null);
      myResponse.Warn(ProblemCodes.TestsReplacedByParent,
        $"{Tests(ancestors.Count)} not in Rider's test model yet, their parent (method or class) was added instead and runs all of its children. " +
        (typos > 0
          ? $"{Tests(typos)} found under a different id, see 'didYouMean' in 'resolution.replacedTests' (a misspelling, " +
            "or Rider's id of the test differs in form); the corrected call adds them exactly. "
          : "") +
        "The tests join the session by themselves once Rider discovers them, usually after a build or a run.",
        new[] { correctedCall, Suggestion.Call(Discovery, null, "explains when Rider discovers tests with the current settings") }
          .Where(x => x != null).ToArray());
    }

    if (unresolved.Count > 0)
    {
      myResponse.Warn(ProblemCodes.TestsUnresolved,
        $"{Tests(unresolved.Count)} not found in Rider's test model, see 'resolution.unresolvedTests' for similar ids.",
        new[] { correctedCall, Suggestion.Call(Discovery, null, "shows which projects Rider discovered tests in and how") }
          .Where(x => x != null).ToArray());
    }

    if (outcome.RescanTimedOut)
    {
      myResponse.Warn(ProblemCodes.RescanTimedOut,
        $"Rider is still rescanning the solution after {RescanTimeout.TotalSeconds:0} s. Tests it finds later join the session by themselves.",
        Suggestion.Call(Get, null, "look at the session again in a minute (pass its id)"));
    }

    object discovery = null;
    if (ancestors.Count > 0 || unresolved.Count > 0)
    {
      // Projects the missing tests belong to, if known; otherwise every test project
      var projects = new HashSet<string>(ancestors.Select(x => x.Element.Project?.Name)
        .Concat(unresolved.Select(x => x.Request.ProjectName)).Where(x => x != null));
      var info = DiscoveryInfo.Collect(mySolution, projects.Count > 0 ? p => projects.Contains(p.Name) : null);
      ReportBuildProblems(info);
      ReportSettingsProblems(info.Settings);
      discovery = info.Summary();
    }

    return new
    {
      requested = result.Items.Count,
      resolved = exact.Count,
      replacedByParent = ancestors.Count,
      notFound = unresolved.Count,
      rescanned = outcome.Rescanned,
      resolvedTests = exact.Take(MaxListed).Select(x => new
      {
        test = x.Request.TestId,
        riderId = x.Element.NaturalId.TestId != x.Request.TestId ? x.Element.NaturalId.TestId : null,
        provider = x.Element.NaturalId.ProviderId
      }).ToList(),
      replacedTests = replacedReport,
      unresolvedTests = unresolvedReport,
      truncated = result.Items.Count > MaxListed ? (bool?)true : null,
      discovery
    };
  }

  private void ReportListing(RunSettingsListing listing)
  {
    if (listing.Requests.Count == 0)
    {
      var first = listing.Problems.FirstOrDefault() ?? new Problem(ProblemCodes.NoTestsSelected, "The run settings select no tests.");
      foreach (var other in listing.Problems.Skip(1))
        myResponse.Warn(other.Code, other.Message, SuggestionsFor(other));
      throw new ToolException(first.Code, first.Message, SuggestionsFor(first)) { Result = new { listedFrom = listing.Details } };
    }

    foreach (var problem in listing.Problems)
      myResponse.Warn(problem.Code, problem.Message, SuggestionsFor(problem));
  }

  private Suggestion[] SuggestionsFor(Problem problem)
  {
    switch (problem.Code)
    {
      case ProblemCodes.BuildOutputMissing:
      case ProblemCodes.BuildOutputStale:
      case ProblemCodes.NoBuiltTestProjects:
        return BuildAndRetry("builds the test projects, the tests are listed from their assemblies");
      case ProblemCodes.BuildInProgress:
        return new[] { Suggestion.Call(BuildState, new { rootFolder = SolutionDirectory }, "poll until 'state' is Completed"), Retry("retry after the build") };
      case ProblemCodes.ListingFailed:
        return BuildAndRetry("a broken or partial build is the usual cause; the build reports compilation errors")
          .Append(Suggestion.Call(Discovery, null, "shows the build state of every test project")).ToArray();
      case ProblemCodes.DotnetCliMissing:
        return new[] { Suggestion.AskUser("Configure the .NET CLI in Settings | Build, Execution, Deployment | Toolset and Build", "listing runs dotnet vstest") };
      case ProblemCodes.NoTestsSelected:
        return new[]
        {
          Suggestion.Call(Discovery, null, "shows which tests Rider knows; compare with the filter (TestCaseFilter or NUnit Where) in the file"),
          Suggestion.Call(Create, new { name = Text("name") ?? "My tests", tests = new[] { "Ns.FixtureClass.TestMethod" } }, "a session from explicit test ids instead")
        };
      case ProblemCodes.WrongFileKind:
        return new[] { Suggestion.Call(Create, WithArgument("runSettingsPath", null, ("sessionFilePath", Text("runSettingsPath"))), "the file is a saved session") };
      default:
        return new Suggestion[0];
    }
  }

  private void ReportBuildProblems(DiscoveryInfo info)
  {
    foreach (var problem in info.Problems)
      myResponse.Warn(problem.Code, problem.Message, SuggestionsFor(problem));
  }

  private void ReportSettingsProblems(DiscoverySettings settings)
  {
    var nunitDisabled = settings.DisabledProviders.Contains(DiscoveryInfo.NUnitProviderId);
    if (nunitDisabled && (settings.VsTestIgnoresNUnitAdapter || settings.VsTestIgnoresAllAdapters ||
                          settings.DisabledProviders.Contains(VsTestIds.ProviderId)))
    {
      myResponse.Warn(ProblemCodes.NUnitNotDiscovered,
        "Neither the built-in NUnit provider nor VSTest discovers NUnit tests with the current settings, see 'implications'.",
        Suggestion.AskUser(
          "Enable the NUnit provider, or let VSTest use NUnit3TestAdapter, in Settings | Build, Execution, Deployment | Unit Testing",
          "only the user should change how their tests are discovered"));
    }
  }

  #endregion

  #region Checks and helpers

  private void CheckUnitTestingEnabled()
  {
    if (!DiscoverySettings.Read(mySolution).UnitTestingEnabled)
      throw new ToolException(ProblemCodes.UnitTestingDisabled, "Unit testing is disabled in Rider, no tests are discovered.",
        Suggestion.AskUser("Enable unit testing in Settings | Build, Execution, Deployment | Unit Testing", "only the user should change it"));
  }

  private void CheckProject(string project)
  {
    var projects = mySolution.GetAllProjects().Where(p => p.IsProjectFromUserView()).Select(p => p.Name).ToList();
    if (projects.Contains(project))
      return;

    var similar = projects.Where(p => string.Equals(p, project, StringComparison.OrdinalIgnoreCase) ||
                                      p.IndexOf(project, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
    var testProjects = DiscoveryInfo.Collect(mySolution).Projects.Select(p => p.Project).Distinct().ToList();
    throw new ToolException(ProblemCodes.ProjectNotFound, $"The solution has no project named '{project}' (names are case-sensitive).",
      similar.Take(3).Select(p => Suggestion.Call(myTool, WithArgument("project", p), "a project with a similar name"))
        .Append(Suggestion.Call(myTool, WithArgument("project", null), "without 'project', tests are looked up in every project")).ToArray())
    {
      Result = new { testProjects }
    };
  }

  private string SolutionDirectory => mySolution.SolutionDirectory.FullPath;

  private string ResolvePath(string path) =>
    Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(SolutionDirectory, path));

  private string ExistingFile(string path, string argument, string extension)
  {
    var fullPath = ResolvePath(path);
    if (File.Exists(fullPath))
      return fullPath;

    var found = FindFiles("." + extension);
    throw new ToolException(ProblemCodes.FileNotFound,
      $"{fullPath} does not exist (relative paths are resolved against the solution directory {SolutionDirectory}).",
      found.Select(f => Suggestion.Call(myTool, WithArgument(argument, f), $"a .{extension} file in the solution directory")).ToArray());
  }

  /// <summary>Files with the extension under the solution directory, relative to it; skips build outputs and tool folders.</summary>
  private List<string> FindFiles(string extension)
  {
    var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".idea", ".vs", "node_modules", "packages" };
    var result = new List<string>();
    var pending = new Queue<string>();
    pending.Enqueue(SolutionDirectory);
    for (var visited = 0; pending.Count > 0 && visited < 2000 && result.Count < 5; visited++)
    {
      var directory = pending.Dequeue();
      try
      {
        result.AddRange(Directory.EnumerateFiles(directory, "*" + extension).Select(f => f.Substring(SolutionDirectory.Length).TrimStart('/', '\\')));
        foreach (var child in Directory.EnumerateDirectories(directory).Where(d => !skipped.Contains(Path.GetFileName(d))))
          pending.Enqueue(child);
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
      }
    }

    return result.Take(5).ToList();
  }

  private static string FileNameOf(string name) =>
    new(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

  private Suggestion[] BuildAndRetry(string reason) => new[]
  {
    Suggestion.Call(BuildStart, new { rootFolder = SolutionDirectory }, reason),
    Suggestion.Call(BuildState, new { rootFolder = SolutionDirectory },
      "poll until 'state' is Completed; when buildIsSuccess is false, fix the reported errors first"),
    Retry("retry once the build has succeeded")
  };

  private Suggestion Retry(string reason) => Suggestion.Call(myTool, WithArgument(null, null, RetryArguments()), reason);

  /// <summary>
  /// Arguments that make repeating this call safe: a repeated create replaces the session it created (or would have),
  /// instead of failing on the name.
  /// </summary>
  private (string Name, object Value)[] RetryArguments() =>
    myTool == Create ? new (string, object)[] { ("ifExists", IfExistsReplace) } : new (string, object)[0];

  private static string Tests(int count) => count == 1 ? "1 test is" : $"{count} tests are";

  /// <summary>The arguments of this call with one changed (removed when null) and others added, for a corrected retry.</summary>
  private JObject WithArgument([CanBeNull] string name, [CanBeNull] object value, params (string Name, object Value)[] others)
  {
    var arguments = (JObject)myArguments.DeepClone();
    if (name != null && value == null)
      arguments.Remove(name);
    else if (name != null)
      arguments[name] = JToken.FromObject(value);
    foreach (var (otherName, otherValue) in others)
      arguments[otherName] = JToken.FromObject(otherValue);
    return arguments;
  }

  [CanBeNull]
  private string Text(string name)
  {
    var token = myArguments[name];
    if (token == null || token.Type == JTokenType.Null)
      return null;
    if (token.Type != JTokenType.String)
      throw new ToolException(ProblemCodes.InvalidArguments, $"'{name}' must be a string, not {token.Type}.");
    var value = token.Value<string>();
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }

  [CanBeNull]
  private List<string> Texts(string name)
  {
    var token = myArguments[name];
    if (token == null || token.Type == JTokenType.Null)
      return null;
    // A single id is a list of one
    if (token.Type == JTokenType.String)
      return new List<string> { token.Value<string>().Trim() };
    if (token.Type != JTokenType.Array || token.Any(x => x.Type != JTokenType.String || string.IsNullOrWhiteSpace(x.Value<string>())))
      throw new ToolException(ProblemCodes.InvalidArguments, $"'{name}' must be a list of non-empty test ids.");
    return token.Select(x => x.Value<string>().Trim()).ToList();
  }

  private bool Flag(string name) => myArguments[name]?.Type == JTokenType.Boolean && myArguments[name].Value<bool>();

  /// <summary>Runs on the main thread under a read lock, where the unit test framework expects its callers.</summary>
  private Task Locked(Action action) => Locked<object>(() =>
  {
    action();
    return null;
  });

  private Task<T> Locked<T>(Func<T> action)
  {
    var completion = new TaskCompletionSource<T>();
    var lifetime = mySolution.GetSolutionLifetimes().UntilSolutionCloseLifetime;
    mySolution.Locks.ExecuteOrQueueReadLockEx(lifetime, "RiderTestsSupportPlus.McpTool", () =>
    {
      try
      {
        completion.SetResult(action());
      }
      catch (Exception e)
      {
        completion.SetException(e);
      }
    });
    return completion.Task;
  }

  #endregion
}
