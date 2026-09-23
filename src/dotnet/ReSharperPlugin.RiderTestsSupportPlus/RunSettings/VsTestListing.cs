using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>
/// Lists tests selected by a <c>.runsettings</c> file using <c>dotnet vstest --ListFullyQualifiedTests</c>.
/// </summary>
/// <remarks>
/// vstest itself evaluates <c>RunConfiguration/TestCaseFilter</c>, with its own filter engine against the discovered
/// test cases' properties and traits. An adapter that filters by itself during a run can accept more property names
/// (see <see cref="NUnitFilterAliases"/>), so the listing matches a run only for properties vstest knows.
/// Discovery through <c>Microsoft.TestPlatform.TranslationLayer</c> was tried first and rejected: it returns every test and
/// ignores the filter (both from the run settings and from <c>TestPlatformOptions.TestCaseFilter</c>); vstest applies the
/// filter only in the console's listing and run paths.
/// </remarks>
public static class VsTestListing
{
  public static IReadOnlyList<string> ListFullyQualifiedTests(string dotnetExe, string assemblyPath, string runSettingsPath,
    CancellationToken ct)
  {
    assemblyPath = Path.GetFullPath(assemblyPath);
    runSettingsPath = Path.GetFullPath(runSettingsPath);
    var arguments = string.Join(" ", "vstest", ListingProcess.Quote(assemblyPath), "--ListFullyQualifiedTests",
      "--ListTestsTargetPath:{0}", ListingProcess.Quote("--Settings:" + runSettingsPath));
    return ListingProcess.Run("dotnet vstest", dotnetExe, arguments, Path.GetDirectoryName(assemblyPath), ct);
  }
}
