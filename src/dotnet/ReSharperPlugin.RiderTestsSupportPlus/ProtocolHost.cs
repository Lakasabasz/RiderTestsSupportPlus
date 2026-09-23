#if RIDER
using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.ReSharper.UnitTestFramework.Session;
using JetBrains.Rider.Model;
using JetBrains.Util;
using ReSharperPlugin.RiderTestsSupportPlus.RunSettings;
using ReSharperPlugin.RiderTestsSupportPlus.Sessions;

namespace ReSharperPlugin.RiderTestsSupportPlus;

/// <summary>Exposes the session flows over the protocol, without dialogs and message boxes (used by integration tests).</summary>
[SolutionComponent(Instantiation.ContainerAsyncPrimaryThread)]
public class ProtocolHost
{
  public ProtocolHost(Lifetime lifetime, ISolution solution)
  {
    var model = solution.GetProtocolSolution().GetRiderTestsSupportPlusModel();

    model.SaveSession.Set((_, request) =>
    {
      var manager = solution.GetComponent<IUnitTestSessionManager>();
      // The backend's active session follows the frontend's tab activation, which is asynchronous.
      var session = request.SessionName == null
        ? manager.ActiveSession
        : manager.Sessions.LastOrDefault(s => s.Name.Value == request.SessionName);
      if (session == null)
        return RdTask.Faulted<int>(new InvalidOperationException($"No unit test session '{request.SessionName ?? "(active)"}'."));
      var file = SessionFile.From(session);
      file.Save(request.Path);
      return RdTask.Successful(file.Tests.Count);
    });

    model.LoadSession.Set((_, path) => ToRdTask(SessionFile.LoadAsync(solution, FileSystemPath.Parse(path))));
    model.ImportRunSettings.Set((_, path) => ToRdTask(RunSettingsImporter.ImportAsync(solution, FileSystemPath.Parse(path))));
  }

  private static RdTask<SessionReport> ToRdTask(Task<SessionOpenResult> task)
  {
    var result = new RdTask<SessionReport>();
    task.ContinueWith(t =>
    {
      if (t.IsFaulted) result.Set(t.Exception!.GetBaseException());
      else if (t.IsCanceled) result.SetCancelled();
      else result.Set(ToReport(t.Result));
    }, TaskContinuationOptions.ExecuteSynchronously);
    return result;
  }

  private static SessionReport ToReport(SessionOpenResult result) => new(
    result.Session?.Name.Value,
    result.Resolution.Exact.Select(x => x.Request.TestId).ToList(),
    result.Resolution.Ancestors.Select(x => x.Request.TestId + " -> " + x.Element.NaturalId.TestId).ToList(),
    result.Resolution.Unresolved.Select(x => x.Request.TestId).ToList(),
    result.Notes.Concat(result.Details).ToList(),
    result.Rescanned);
}
#endif
