using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;
using NUnit.Engine;

namespace RiderTestsSupportPlus.NUnitLister;

/// <summary>
/// Writes the full names of the test cases that the run settings' <c>NUnit/Where</c> selects, one per line.
/// NUnit's full name is what NUnit3TestAdapter reports as FullyQualifiedName.
/// Explore only: nothing is executed, and nothing is written next to the test assembly.
/// </summary>
internal static class Program
{
  private static int Main(string[] args)
  {
    if (args.Length != 3)
    {
      Console.Error.WriteLine("Usage: RiderTestsSupportPlus.NUnitLister <test.dll> <runsettings> <output>");
      return 1;
    }

    var assembly = Path.GetFullPath(args[0]);
    var bin = Path.GetDirectoryName(assembly)!;
    // The engine comes with the adapter's build files and is usually missing from the test's deps.json
    AssemblyLoadContext.Default.Resolving += (context, name) =>
    {
      var path = Path.Combine(bin, name.Name + ".dll");
      return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
    };
    try
    {
      return Run(assembly, args[1], args[2]);
    }
    catch (Exception e)
    {
      Console.Error.WriteLine(e);
      return 2;
    }
  }

  // Separate method, so that engine types are resolved only after the Resolving handler is in place
  [MethodImpl(MethodImplOptions.NoInlining)]
  private static int Run(string assembly, string runSettings, string output)
  {
    var where = XDocument.Load(runSettings).Root?.Element("NUnit")?.Element("Where")?.Value;
    var work = Path.Combine(Path.GetTempPath(), "RiderTestsSupportPlus-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(work);
    try
    {
      using var engine = new NUnit.Engine.TestEngine();
      engine.WorkDirectory = work;
      engine.InternalTraceLevel = InternalTraceLevel.Off;
      engine.Initialize();

      var filterBuilder = engine.Services.GetService<ITestFilterService>().GetTestFilterBuilder();
      if (!string.IsNullOrWhiteSpace(where))
        filterBuilder.SelectWhere(where);

      var package = new TestPackage(assembly);
      package.AddSetting("WorkDirectory", work);
      using var runner = engine.GetRunner(package);
      var tree = runner.Explore(filterBuilder.GetFilter());
      var names = tree.SelectNodes("//test-case")!.Cast<XmlNode>()
        .Select(n => n.Attributes!["fullname"]!.Value).Distinct().ToList();
      File.WriteAllLines(output, names);
      return 0;
    }
    finally
    {
      try { Directory.Delete(work, true); } catch { }
    }
  }
}
