using System.Collections.Generic;

namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// A test to find in Rider's unit test model. Everything except <see cref="TestId"/> is optional and only narrows the search.
/// </summary>
public sealed class TestRequest
{
  public TestRequest(string testId)
  {
    TestId = testId;
  }

  /// <summary>Id as produced by the test framework, e.g. <c>Ns.TestClass(A).TestName(X)</c>.</summary>
  public string TestId { get; }

  public string ProviderId { get; set; }

  /// <summary>Persistent id of the project (<c>UnitTestElementId.ProjectId</c>).</summary>
  public string ProjectId { get; set; }

  public string ProjectName { get; set; }

  /// <summary><c>TargetFrameworkId.UniqueString</c>.</summary>
  public string TargetFrameworkId { get; set; }

  public string Salt { get; set; }

  public string DisplayName { get; set; }

  /// <summary>Test ids to fall back to when the test itself is missing, nearest first.</summary>
  public IReadOnlyList<string> AncestorIds { get; set; } = new string[0];

  public override string ToString() => ProjectName != null ? $"{TestId} [{ProjectName}]" : TestId;
}
