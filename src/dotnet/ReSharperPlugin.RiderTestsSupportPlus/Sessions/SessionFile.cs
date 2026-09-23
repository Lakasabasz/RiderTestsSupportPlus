using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.ProjectModel;
using JetBrains.Util;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using JetBrains.ReSharper.UnitTestFramework.Session;
using Newtonsoft.Json;
using ReSharperPlugin.RiderTestsSupportPlus.Resolution;

namespace ReSharperPlugin.RiderTestsSupportPlus.Sessions;

/// <summary>Contents of a <c>.rtsession</c> file (JSON).</summary>
public sealed class SessionFile
{
  public const string Extension = "rtsession";
  public const int CurrentFormatVersion = 1;

  public int FormatVersion { get; set; } = CurrentFormatVersion;
  public string Name { get; set; }
  public List<SavedTest> Tests { get; set; } = new();

  public static SessionFile From(IUnitTestSession session)
  {
    using (UT.ReadLock())
    {
      var elements = session.Elements.ToList();
      var inSession = new HashSet<IUnitTestElement>(elements, UnitTestElement.Comparer.ById);

      // Only leaves: a container in the session is represented by the tests under it,
      // so loading restores exactly the tests that were there, not whatever the container holds by then.
      var leaves = elements.Where(e => !e.Children.Any(inSession.Contains));

      return new SessionFile
      {
        Name = session.Name.Value,
        Tests = leaves.Select(SavedTest.From).OrderBy(x => x.ProjectName).ThenBy(x => x.TestId).ToList()
      };
    }
  }

  public void Save(string path)
  {
    using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
      Write(writer);
  }

  public static SessionFile Load(string path)
  {
    using (var reader = new StreamReader(path, Encoding.UTF8))
      return Read(reader);
  }

  /// <summary>Reads the file and opens the session. Completes on the main thread.</summary>
  public static Task<SessionOpenResult> LoadAsync(ISolution solution, FileSystemPath path)
  {
    SessionFile file;
    try
    {
      file = Load(path.FullPath);
    }
    catch (Exception e)
    {
      return Task.FromException<SessionOpenResult>(new InvalidDataException("Could not read the session file: " + e.Message, e));
    }

    var requests = file.Tests.Where(t => !string.IsNullOrEmpty(t.TestId)).Select(t => t.ToRequest()).ToList();
    return SessionOpener.ResolveAndOpenAsync(solution, file.Name ?? path.NameWithoutExtension, requests);
  }

  public void Write(TextWriter writer)
  {
    JsonSerializer.Create(new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore })
      .Serialize(writer, this);
  }

  public static SessionFile Read(TextReader reader)
  {
    var file = JsonSerializer.CreateDefault().Deserialize<SessionFile>(new JsonTextReader(reader));
    if (file == null)
      throw new InvalidDataException("The file is empty.");
    if (file.FormatVersion > CurrentFormatVersion)
      throw new InvalidDataException($"Unsupported session format version {file.FormatVersion}.");
    return file;
  }
}

public sealed class SavedTest
{
  public string ProviderId { get; set; }
  public string ProjectId { get; set; }
  public string ProjectName { get; set; }
  public string TargetFramework { get; set; }
  public string TestId { get; set; }
  public string Salt { get; set; }
  public string DisplayName { get; set; }

  /// <summary>Test ids of the parents, nearest first (<c>TestClass(A).TestName</c>, <c>TestClass(A)</c>, …).</summary>
  public List<string> Parents { get; set; } = new();

  public static SavedTest From(IUnitTestElement element)
  {
    var parents = new List<string>();
    for (var parent = element.Parent; parent != null; parent = parent.Parent)
      parents.Add(parent.NaturalId.TestId);

    return new SavedTest
    {
      ProviderId = element.NaturalId.ProviderId,
      ProjectId = element.NaturalId.ProjectId,
      ProjectName = element.Project.Name,
      TargetFramework = element.NaturalId.TargetFrameworkId,
      TestId = element.NaturalId.TestId,
      Salt = element.NaturalId.Salt,
      DisplayName = element.GetPresentation(),
      Parents = parents
    };
  }

  public TestRequest ToRequest()
  {
    return new TestRequest(TestId)
    {
      ProviderId = ProviderId,
      ProjectId = ProjectId,
      ProjectName = ProjectName,
      TargetFrameworkId = TargetFramework,
      Salt = Salt,
      DisplayName = DisplayName,
      // Saved parents above the class level (set-up fixtures, namespaces) would pull whole namespaces into the session.
      AncestorIds = (Parents ?? new List<string>()).Intersect(TestIdPath.AncestorCandidates(TestId))
        .Union(TestIdPath.AncestorCandidates(TestId)).ToList()
    };
  }
}
