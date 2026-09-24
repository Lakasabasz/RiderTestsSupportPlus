using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.UnitTestFramework.Exploration;
using JetBrains.ReSharper.UnitTestFramework.Persistence;
using JetBrains.ReSharper.UnitTestFramework.Session;
using JetBrains.ReSharper.UnitTestFramework.UI.Session;
using JetBrains.Util;
using ReSharperPlugin.RiderTestsSupportPlus.Resolution;

namespace ReSharperPlugin.RiderTestsSupportPlus.Sessions;

public sealed class SessionOpenResult
{
  public SessionOpenResult(TestResolutionResult resolution, IUnitTestSession session, IReadOnlyList<string> notes,
    IReadOnlyList<string> details, bool rescanned)
  {
    Resolution = resolution;
    Session = session;
    Notes = notes;
    Details = details;
    Rescanned = rescanned;
  }

  public TestResolutionResult Resolution { get; }

  /// <summary><c>null</c> when nothing could be resolved and no session was created.</summary>
  public IUnitTestSession Session { get; }

  /// <summary>Problems found before resolution, e.g. projects without build output.</summary>
  public IReadOnlyList<string> Notes { get; }

  /// <summary>Information that is not a problem, e.g. which build the tests were listed from.</summary>
  public IReadOnlyList<string> Details { get; }

  public bool Rescanned { get; }

  public bool IsClean => Session != null && Resolution.IsComplete && Notes.Count == 0;
}

/// <summary>
/// Resolves requested tests, rescans the solution when some are missing, and creates and opens the session.
/// </summary>
public static class SessionOpener
{
  private const int MaxListedTests = 25;

  /// <summary>Completes on the main thread, after the session was opened.</summary>
  public static Task<SessionOpenResult> ResolveAndOpenAsync(ISolution solution, string sessionName,
    IReadOnlyList<TestRequest> requests, IReadOnlyList<string> notes = null, IReadOnlyList<string> details = null)
  {
    solution.Locks.AssertReadAccessAllowed();
    notes ??= new string[0];
    details ??= new string[0];

    var resolver = new TestElementResolver(solution.GetComponent<IUnitTestElementRepository>());
    var result = resolver.Resolve(requests);
    if (result.IsComplete || requests.Count == 0)
      return Task.FromResult(Open(solution, sessionName, result, notes, details, rescanned: false));

    // Parametrized tests are often created only by metadata exploration of the built assembly, so a rescan may find them.
    // RescanAll does not clear the element repository, it re-explores projects and completes when exploration is done.
    var lifetime = solution.GetSolutionLifetimes().UntilSolutionCloseLifetime;
    var progress = BackgroundTask.Start(solution, "Finding missing tests", indeterminate: true);
    progress.Report(0, $"{result.Items.Count(x => x.Kind != TestResolutionKind.Exact)} of {requests.Count} tests not found, rescanning the solution");
    Task rescan;
    try
    {
      rescan = solution.GetComponent<IUnitTestExplorationFacade>().RescanAll(progress.Token);
    }
    catch (Exception e)
    {
      rescan = Task.FromException(e);
    }

    var completion = new TaskCompletionSource<SessionOpenResult>();
    rescan.ContinueWith(task =>
    {
      progress.Dispose();
      solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "RiderTestsSupportPlus.OpenSession", () =>
      {
        try
        {
          // Cancelling the rescan still opens the session, with what was resolved so far.
          var rescanned = task.Status == TaskStatus.RanToCompletion;
          var finalResult = rescanned ? resolver.Resolve(requests) : result;
          completion.SetResult(Open(solution, sessionName, finalResult, notes, details, rescanned));
        }
        catch (Exception e)
        {
          completion.SetException(e);
        }
      });
    }, TaskScheduler.Default);
    return completion.Task;
  }

  private static SessionOpenResult Open(ISolution solution, string sessionName, TestResolutionResult result,
    IReadOnlyList<string> notes, IReadOnlyList<string> details, bool rescanned)
  {
    IUnitTestSession session = null;
    if (result.Items.Any(x => x.Kind != TestResolutionKind.Unresolved))
    {
      session = solution.GetComponent<IUnitTestSessionRepository>().CreateSession(result.CreateCriterion(), sessionName);
      solution.GetComponent<IUnitTestSessionConductor>().OpenSession(session);
    }

    return new SessionOpenResult(result, session, notes, details, rescanned);
  }

  /// <summary>
  /// An error when no session was created, a warning when something was not resolved exactly or went wrong,
  /// otherwise an information with the details, if there are any.
  /// </summary>
  public static void ShowReport(SessionOpenResult result)
  {
    if (result.IsClean && result.Details.Count == 0)
      return;

    var report = BuildReport(result);
    if (result.Session == null)
      MessageBox.ShowError(report, "Tests Support Plus");
    else if (!result.IsClean)
      MessageBox.ShowExclamation(report, "Tests Support Plus");
    else
      MessageBox.ShowInfo(report, "Tests Support Plus");
  }

  /// <summary>Shows the result on the main thread once <paramref name="task"/> completes.</summary>
  public static void ShowReportWhenDone(ISolution solution, Task<SessionOpenResult> task)
  {
    var lifetime = solution.GetSolutionLifetimes().UntilSolutionCloseLifetime;
    task.ContinueWith(t =>
    {
      solution.Locks.ExecuteOrQueueReadLockEx(lifetime, "RiderTestsSupportPlus.ShowReport", () =>
      {
        if (t.IsFaulted)
          MessageBox.ShowError(t.Exception?.GetBaseException().Message ?? "Unknown error", "Tests Support Plus");
        else if (!t.IsCanceled)
          ShowReport(t.Result);
      });
    }, TaskScheduler.Default);
  }

  private static string BuildReport(SessionOpenResult openResult)
  {
    var result = openResult.Resolution;
    var text = new StringBuilder();
    text.AppendLine(openResult.Session != null
      ? result.IsComplete ? "The session was created." : "The session was created, but not every test was resolved exactly."
      : "No tests could be resolved, the session was not created.");
    text.AppendLine();
    text.AppendLine($"Resolved: {result.Exact.Count()}, replaced by a parent: {result.Ancestors.Count()}, unresolved: {result.Unresolved.Count()}" +
                    (openResult.Rescanned ? " (after rescanning the solution)." : "."));

    var ancestors = result.Ancestors.ToList();
    if (ancestors.Count > 0)
    {
      text.AppendLine();
      text.AppendLine("Not found yet, their parent was added instead (it runs all of its children; build or run the tests so Rider discovers them):");
      AppendList(text, ancestors.Select(x => $"{x.Request.TestId}  →  {x.Element.NaturalId.TestId}"));
    }

    var unresolved = result.Unresolved.ToList();
    if (unresolved.Count > 0)
    {
      text.AppendLine();
      text.AppendLine("Not found:");
      AppendList(text, unresolved.Select(x => x.Request.ToString()));
    }

    if (openResult.Notes.Count > 0)
    {
      text.AppendLine();
      AppendList(text, openResult.Notes);
    }

    if (openResult.Details.Count > 0)
    {
      text.AppendLine();
      AppendList(text, openResult.Details);
    }

    return text.ToString();
  }

  private static void AppendList(StringBuilder text, IEnumerable<string> items)
  {
    var list = items.ToList();
    foreach (var item in list.Take(MaxListedTests))
      text.AppendLine("  • " + item);
    if (list.Count > MaxListedTests)
      text.AppendLine($"  … and {list.Count - MaxListedTests} more");
  }
}
