using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace ReSharperPlugin.RiderTestsSupportPlus.RunSettings;

/// <summary>
/// Lists the tests selected by <c>&lt;NUnit&gt;&lt;Where&gt;</c>, with NUnit's own filter engine.
/// </summary>
/// <remarks>
/// <c>Where</c> is evaluated by NUnit3TestAdapter only while running tests; <c>dotnet vstest --ListFullyQualifiedTests</c>
/// ignores it and lists everything. So the bundled helper (src/tools/RiderTestsSupportPlus.NUnitLister) runs
/// <c>Explore</c> of the NUnit engine found next to the test assembly, in the test's own runtime context, like testhost.
/// </remarks>
public static class NUnitWhereListing
{
  private const string HelperFileName = "RiderTestsSupportPlus.NUnitLister.dll";

  public static IReadOnlyList<string> ListFullyQualifiedTests(string dotnetExe, string assemblyPath, string runSettingsPath,
    CancellationToken ct)
  {
    assemblyPath = Path.GetFullPath(assemblyPath);
    var directory = Path.GetDirectoryName(assemblyPath)!;
    var name = Path.GetFileNameWithoutExtension(assemblyPath);
    var runtimeConfig = Path.Combine(directory, name + ".runtimeconfig.json");
    var deps = Path.Combine(directory, name + ".deps.json");
    if (!File.Exists(runtimeConfig) || !File.Exists(deps))
      throw new FileNotFoundException(
        "NUnit <Where> is supported only for .NET (Core) test projects, the build output has no runtimeconfig.json/deps.json.");
    if (!File.Exists(Path.Combine(directory, "nunit.engine.dll")))
      throw new FileNotFoundException("nunit.engine.dll was not found next to the test assembly (is NUnit3TestAdapter referenced?).");

    var helper = Path.Combine(Path.GetDirectoryName(typeof(NUnitWhereListing).Assembly.Location)!, HelperFileName);
    if (!File.Exists(helper))
      throw new FileNotFoundException("The plugin is incomplete, " + HelperFileName + " is missing.", helper);

    var arguments = string.Join(" ", "exec",
      "--runtimeconfig", ListingProcess.Quote(runtimeConfig), "--depsfile", ListingProcess.Quote(deps),
      ListingProcess.Quote(helper), ListingProcess.Quote(assemblyPath), ListingProcess.Quote(Path.GetFullPath(runSettingsPath)), "{0}");
    return ListingProcess.Run("NUnit engine", dotnetExe, arguments, Path.GetTempPath(), ct);
  }
}
