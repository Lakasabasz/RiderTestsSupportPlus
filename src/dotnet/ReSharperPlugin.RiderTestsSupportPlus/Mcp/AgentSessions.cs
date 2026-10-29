using System;
using System.Linq;
using JetBrains.Application.Settings;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DataContext;
using JetBrains.ReSharper.UnitTestFramework.Settings;

namespace ReSharperPlugin.RiderTestsSupportPlus.Mcp;

[SettingsKey(typeof(UnitTestingSettings), "Tests Support Plus: unit test sessions created by AI agents through the MCP tools")]
public class AgentSessionsSettings
{
  [SettingsIndexedEntry("Ids of the sessions an agent created; it changes and deletes only these unless told otherwise")]
  public IIndexedEntry<string, bool> SessionIds;
}

/// <summary>
/// Sessions created through the MCP tools. Agents change and delete only these by default, so they clean up
/// after themselves without touching the user's sessions. Stored next to the sessions themselves
/// (Rider persists sessions in the solution's settings), so ownership survives a restart.
/// </summary>
public static class AgentSessions
{
  public static bool IsOwned(ISolution solution, Guid sessionId) =>
    Store(solution).GetIndexedValue((AgentSessionsSettings x) => x.SessionIds, sessionId.ToString());

  public static void Add(ISolution solution, Guid sessionId) =>
    Store(solution).SetIndexedValue((AgentSessionsSettings x) => x.SessionIds, sessionId.ToString(), true);

  public static void Remove(ISolution solution, Guid sessionId) =>
    Store(solution).RemoveIndexedValue((AgentSessionsSettings x) => x.SessionIds, sessionId.ToString());

  /// <summary>Forgets sessions that no longer exist, e.g. closed by the user.</summary>
  public static void Retain(ISolution solution, Func<Guid, bool> exists)
  {
    var store = Store(solution);
    var stale = store.EnumIndexedValues(store.Schema.GetIndexedEntry((AgentSessionsSettings x) => x.SessionIds), null)
      .Keys.OfType<string>()
      .Where(key => !Guid.TryParse(key, out var id) || !exists(id))
      .ToList();
    foreach (var key in stale)
      store.RemoveIndexedValue((AgentSessionsSettings x) => x.SessionIds, key);
  }

  // The same context as Rider's session store (UnitTestSessionRepository)
  private static IContextBoundSettingsStore Store(ISolution solution) =>
    solution.GetComponent<ISettingsStore>()
      .BindToContextTransient(ContextRange.ManuallyRestrictWritesToOneContext(solution.ToDataContext2()));
}
