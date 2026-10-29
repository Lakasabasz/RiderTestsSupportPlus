namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// Test ids of Rider's VSTest provider for tests run by NUnit3TestAdapter: the adapter's FullyQualifiedName prefixed with
/// the executor URI. The provider (VsTestTestCaseMapping.BuildHierarchy) walks the name: classes, parametrized fixtures and
/// methods become elements with '#' (<c>executor://nunit3testexecutor/#Ns.C("A").M</c>); only a name with something left
/// after its method, i.e. test case arguments, gets a leaf with '::' (<c>executor://nunit3testexecutor/::Ns.C("A").M(1)</c>).
/// A test without arguments is thus its method element, <c>#Ns.C.M</c>, and has no '::' id; a name whose class Rider cannot
/// find (e.g. not built yet) is a single '::' leaf. Used when the built-in NUnit provider is disabled, e.g. to work around
/// RIDER-131305.
/// </summary>
public static class VsTestIds
{
  // Uri normalization lowercases the host, so the provider stores the executor in lower case
  private const string NUnitExecutor = "executor://nunit3testexecutor/";
  private const string TestSeparator = "::";
  private const string ContainerSeparator = "#";

  public const string ProviderId = "VsTest";

  public static string Test(string testId) => NUnitExecutor + TestSeparator + ToFrameworkId(testId);

  public static string Container(string testId) => NUnitExecutor + ContainerSeparator + ToFrameworkId(testId);

  /// <summary>The ids a test or container with the adapter's name <paramref name="frameworkId"/> can have, the test case form first.</summary>
  public static string[] Forms(string frameworkId) => new[] { Test(frameworkId), Container(frameworkId) };

  public static bool IsVsTestId(string testId) => testId.StartsWith(NUnitExecutor);

  /// <summary>The adapter's FullyQualifiedName of a VSTest id; other ids are returned unchanged.</summary>
  public static string ToFrameworkId(string testId)
  {
    if (!IsVsTestId(testId))
      return testId;

    var rest = testId.Substring(NUnitExecutor.Length);
    if (rest.StartsWith(TestSeparator))
      return rest.Substring(TestSeparator.Length);
    return rest.StartsWith(ContainerSeparator) ? rest.Substring(ContainerSeparator.Length) : rest;
  }
}
