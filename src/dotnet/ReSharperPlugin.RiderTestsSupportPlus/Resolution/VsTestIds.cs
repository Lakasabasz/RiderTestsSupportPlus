namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// Test ids of Rider's VSTest provider for tests run by NUnit3TestAdapter. The provider prefixes the adapter's
/// FullyQualifiedName with the executor URI: <c>executor://nunit3testexecutor/::Ns.C(A).M(X)</c> for a test and
/// <c>executor://nunit3testexecutor/#Ns.C(A).M</c> for its method, fixture and class.
/// Used when the built-in NUnit provider is disabled, e.g. to work around RIDER-131305.
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
