using JetBrains.Annotations;

namespace ReSharperPlugin.RiderTestsSupportPlus;

/// <summary>
/// Something that went wrong or makes a result incomplete. The message is for people (the Unit Tests window's report),
/// the code lets the MCP tools tell an agent what to do about it.
/// </summary>
public sealed class Problem
{
  public Problem(string code, string message, [CanBeNull] string project = null)
  {
    Code = code;
    Message = message;
    Project = project;
  }

  /// <summary>One of <see cref="ProblemCodes"/>.</summary>
  public string Code { get; }

  public string Message { get; }

  /// <summary>Project the problem is about, e.g. <c>Tests (net8.0)</c>.</summary>
  [CanBeNull] public string Project { get; }

  public override string ToString() => Message;
}

/// <summary>Codes of <see cref="Problem"/>s and of MCP tool errors. Agents match on them, never rename one.</summary>
public static class ProblemCodes
{
  // Arguments and lookups
  public const string InvalidArguments = "INVALID_ARGUMENTS";
  public const string SessionNotFound = "SESSION_NOT_FOUND";
  public const string SessionAmbiguous = "SESSION_AMBIGUOUS";
  public const string SessionExists = "SESSION_EXISTS";
  public const string SessionNotOwned = "SESSION_NOT_OWNED";
  public const string SessionBusy = "SESSION_BUSY";
  public const string SessionLocked = "SESSION_LOCKED";
  public const string ProjectNotFound = "PROJECT_NOT_FOUND";
  public const string FileNotFound = "FILE_NOT_FOUND";
  public const string FileExists = "FILE_EXISTS";
  public const string NotInSession = "NOT_IN_SESSION";

  // Files
  public const string WrongFileKind = "WRONG_FILE_KIND";
  public const string SessionFileUnreadable = "SESSION_FILE_UNREADABLE";
  public const string RunSettingsUnreadable = "RUNSETTINGS_UNREADABLE";
  public const string SaveFailed = "SAVE_FAILED";

  // Discovery and build
  public const string UnitTestingDisabled = "UNIT_TESTING_DISABLED";
  public const string DotnetCliMissing = "DOTNET_CLI_MISSING";
  public const string NoBuiltTestProjects = "NO_BUILT_TEST_PROJECTS";
  public const string BuildOutputMissing = "BUILD_OUTPUT_MISSING";
  public const string BuildOutputStale = "BUILD_OUTPUT_STALE";
  public const string BuildInProgress = "BUILD_IN_PROGRESS";
  public const string ListingFailed = "LISTING_FAILED";
  public const string NoTestsSelected = "NO_TESTS_SELECTED";
  public const string NUnitNotDiscovered = "NUNIT_NOT_DISCOVERED";

  // Resolution
  public const string NoTestsResolved = "NO_TESTS_RESOLVED";
  public const string TestsUnresolved = "TESTS_UNRESOLVED";
  public const string TestsReplacedByParent = "TESTS_REPLACED_BY_PARENT";
  public const string RescanTimedOut = "RESCAN_TIMED_OUT";

  public const string Cancelled = "CANCELLED";
  public const string Internal = "INTERNAL_ERROR";
}
