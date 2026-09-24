using System;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Application.DataContext;
using JetBrains.Application.UI.Actions;
using JetBrains.Application.UI.ActionsRevised.Menu;
using JetBrains.Application.UI.ActionSystem.ActionsRevised.Menu;
using JetBrains.Application.UI.Controls.FileSystem;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DataContext;
using JetBrains.ReSharper.UnitTestFramework.Actions;
using JetBrains.ReSharper.UnitTestFramework.Session;
using JetBrains.Util;
using ReSharperPlugin.RiderTestsSupportPlus.RunSettings;
using ReSharperPlugin.RiderTestsSupportPlus.Sessions;

namespace ReSharperPlugin.RiderTestsSupportPlus.Actions;

// Action ids are referenced by the frontend proxies in src/rider/main/kotlin, keep them in sync.

[Action(Id, "Save Session (Tests Support Plus)…")]
public class SaveSessionAction : IExecutableAction
{
  public const string Id = "RiderTestsSupportPlus.SaveSession";

  public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate) =>
    context.GetData(ProjectModelDataConstants.SOLUTION) != null && GetSession(context) != null;

  public void Execute(IDataContext context, DelegateExecute nextExecute)
  {
    var solution = context.GetData(ProjectModelDataConstants.SOLUTION);
    var session = GetSession(context);
    if (solution == null || session == null)
      return;

    var invalidChars = Path.GetInvalidFileNameChars();
    var suggestedName = new string(session.Name.Value.Where(c => !invalidChars.Contains(c)).ToArray());
    var path = context.GetComponent<ICommonFileDialogs>().BrowseForSaveFile("Save Test Session",
      new[] { new ChooseFileType(SessionFile.Extension, "Test session") },
      solution.SolutionDirectory.ToNativeFileSystemPath(), suggestedName);
    if (path == null)
      return;
    if (!path.FullPath.EndsWith("." + SessionFile.Extension, StringComparison.OrdinalIgnoreCase))
      path = FileSystemPath.Parse(path.FullPath + "." + SessionFile.Extension);

    try
    {
      SessionFile.From(session).Save(path.FullPath);
    }
    catch (Exception e)
    {
      MessageBox.ShowError("Could not save the session: " + e.Message, "Tests Support Plus");
    }
  }

  private static IUnitTestSession GetSession(IDataContext context) =>
    context.GetData(UnitTestDataConstants.Session.IN_CONTEXT) ?? context.GetData(UnitTestDataConstants.Session.CURRENT);
}

[Action(Id, "Load Session (Tests Support Plus)…")]
public class LoadSessionAction : IExecutableAction
{
  public const string Id = "RiderTestsSupportPlus.LoadSession";

  public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate) =>
    context.GetData(ProjectModelDataConstants.SOLUTION) != null;

  public void Execute(IDataContext context, DelegateExecute nextExecute)
  {
    var solution = context.GetData(ProjectModelDataConstants.SOLUTION);
    if (solution == null)
      return;

    var path = context.GetComponent<ICommonFileDialogs>().BrowseForOpenFile("Load Test Session",
      new[] { new ChooseFileType(SessionFile.Extension, "Test session") },
      solution.SolutionDirectory.ToNativeFileSystemPath());
    if (path == null)
      return;

    SessionOpener.ShowReportWhenDone(solution, SessionFile.LoadAsync(solution, path));
  }
}

[Action(Id, "Import Session from .runsettings…")]
public class ImportRunSettingsAction : IExecutableAction
{
  public const string Id = "RiderTestsSupportPlus.ImportRunSettings";

  public bool Update(IDataContext context, ActionPresentation presentation, DelegateUpdate nextUpdate) =>
    context.GetData(ProjectModelDataConstants.SOLUTION) != null;

  public void Execute(IDataContext context, DelegateExecute nextExecute)
  {
    var solution = context.GetData(ProjectModelDataConstants.SOLUTION);
    if (solution == null)
      return;

    var path = context.GetComponent<ICommonFileDialogs>().BrowseForOpenFile("Import Tests from Run Settings",
      new[] { new ChooseFileType("runsettings", "Run settings") },
      solution.SolutionDirectory.ToNativeFileSystemPath());
    if (path == null)
      return;

    SessionOpener.ShowReportWhenDone(solution, RunSettingsImporter.ImportAsync(solution, path));
  }
}
