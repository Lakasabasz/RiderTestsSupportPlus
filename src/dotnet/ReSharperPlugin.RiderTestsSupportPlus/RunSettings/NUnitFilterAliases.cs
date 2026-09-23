using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>
/// Makes the listing of NUnit tests agree with an actual <c>dotnet vstest</c> run.
/// </summary>
/// <remarks>
/// When NUnit3TestAdapter runs tests it evaluates the filter itself and accepts <c>Category</c> as an alias of
/// <c>TestCategory</c>. The listing is filtered by vstest's own engine, which only knows <c>TestCategory</c>, so
/// <c>Category!=Slow</c> selects slow tests too (verified with NUnit 4.2 and NUnit3TestAdapter 4.6).
/// Only the property name is renamed; the expression is still evaluated by vstest.
/// </remarks>
public static class NUnitFilterAliases
{
  // A property name starts a condition: at the start or after an operator or parenthesis, and is followed by an operator.
  private static readonly Regex CategoryProperty = new(@"(?<=(^|[&|(!])\s*)Category(?=\s*(!=|!~|=|~))");

  /// <returns>Path of an adjusted copy of the run settings, or <c>null</c> when no change is needed.</returns>
  public static string CreateAdjustedCopy(string runSettingsPath)
  {
    var document = XDocument.Load(runSettingsPath, LoadOptions.PreserveWhitespace);
    var filters = document.Descendants("TestCaseFilter").Where(e => e.Parent?.Name == "RunConfiguration").ToList();
    var changed = false;

    foreach (var filter in filters)
    {
      var adjusted = CategoryProperty.Replace(filter.Value, "TestCategory");
      if (adjusted == filter.Value)
        continue;
      filter.Value = adjusted;
      changed = true;
    }

    if (!changed)
      return null;

    // Next to the original, so relative paths inside the run settings keep working.
    var directory = Path.GetDirectoryName(runSettingsPath) ?? Path.GetTempPath();
    var copy = Path.Combine(directory, ".RiderTestsSupportPlus-" + Guid.NewGuid().ToString("N") + ".runsettings");
    try
    {
      document.Save(copy);
    }
    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
    {
      copy = Path.Combine(Path.GetTempPath(), Path.GetFileName(copy));
      document.Save(copy);
    }

    return copy;
  }
}
