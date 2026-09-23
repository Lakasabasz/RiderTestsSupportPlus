using System;
using System.Threading;
using JetBrains.Application.Threading;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost.Progress;

namespace ReSharperPlugin.RiderTestsSupportPlus.Sessions;

/// <summary>
/// A cancellable task in the IDE's background tasks (status bar progress). Thread-safe: updates are marshalled to the main thread.
/// </summary>
public sealed class BackgroundTask : IDisposable
{
  private readonly ISolution mySolution;
  private readonly LifetimeDefinition myDefinition;
  private readonly CancellationTokenSource myCancellation = new();
  private readonly IProperty<string> myDescription;
  private readonly IProperty<double> myProgress;

  private BackgroundTask(ISolution solution, string title, bool indeterminate)
  {
    mySolution = solution;
    myDefinition = solution.GetSolutionLifetimes().UntilSolutionCloseLifetime.CreateNested();
    myDescription = new Property<string>("RiderTestsSupportPlus.Description", "");
    myProgress = new Property<double>("RiderTestsSupportPlus.Progress", 0);

    var builder = BackgroundProgressBuilder.Create()
      .WithTitle(title)
      .WithDescription(myDescription)
      .AsCancelable(() => myCancellation.Cancel());
    builder = indeterminate ? builder.AsIndeterminate() : builder.WithProgress(myProgress);

    solution.GetComponent<BackgroundProgressManager>().AddNewTask(myDefinition.Lifetime, builder.Build());
  }

  /// <summary>Must be called on the main thread.</summary>
  public static BackgroundTask Start(ISolution solution, string title, bool indeterminate = false) =>
    new(solution, title, indeterminate);

  public CancellationToken Token => myCancellation.Token;

  /// <param name="fraction">0..1</param>
  public void Report(double fraction, string description)
  {
    OnMainThread(() =>
    {
      myProgress.Value = Math.Max(0, Math.Min(1, fraction));
      myDescription.Value = description;
    });
  }

  public void Dispose() => OnMainThread(() => myDefinition.Terminate());

  private void OnMainThread(Action action)
  {
    if (!myDefinition.Lifetime.IsAlive)
      return;
    mySolution.Locks.ExecuteOrQueueReadLockEx(myDefinition.Lifetime, "RiderTestsSupportPlus.BackgroundTask", action);
  }
}
