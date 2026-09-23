using System.Linq;
using System.Xml.Linq;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>Which filters a <c>.runsettings</c> file carries.</summary>
public sealed class RunSettingsFile
{
  private RunSettingsFile(string testCaseFilter, string nunitWhere)
  {
    TestCaseFilter = testCaseFilter;
    NUnitWhere = nunitWhere;
  }

  /// <summary><c>RunConfiguration/TestCaseFilter</c>, evaluated by vstest.</summary>
  public string TestCaseFilter { get; }

  /// <summary><c>NUnit/Where</c>, evaluated by the NUnit adapter.</summary>
  public string NUnitWhere { get; }

  /// <summary>
  /// With both present, NUnit3TestAdapter runs what <c>TestCaseFilter</c> selects and ignores <c>Where</c>
  /// (checked with NUnit3TestAdapter 4.6), so <c>Where</c> applies only on its own.
  /// </summary>
  public bool UsesNUnitWhere => TestCaseFilter == null && NUnitWhere != null;

  public static RunSettingsFile Load(string path)
  {
    var root = XDocument.Load(path).Root;
    return new RunSettingsFile(
      NonEmpty(root?.Element("RunConfiguration")?.Element("TestCaseFilter")?.Value),
      NonEmpty(root?.Element("NUnit")?.Element("Where")?.Value));
  }

  private static string NonEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
