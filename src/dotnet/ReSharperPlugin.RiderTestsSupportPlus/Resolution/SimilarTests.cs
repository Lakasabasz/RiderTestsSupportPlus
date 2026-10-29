using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Criteria;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using JetBrains.ReSharper.UnitTestFramework.Persistence;

namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// What was most likely meant by a test id that matches nothing: the same id written differently
/// (case, quotes around arguments, spaces) or tests with the same name elsewhere. Ids are compared in the framework's
/// form, so a VSTest id and a plain one are the same test.
/// </summary>
public sealed class SimilarTests
{
  private const int MaxCandidates = 5;

  private readonly ILookup<string, Candidate> myByNormalizedId;
  private readonly ILookup<string, Candidate> myByName;

  public SimilarTests([NotNull] IEnumerable<IUnitTestElement> elements)
  {
    List<Candidate> candidates;
    using (UT.ReadLock())
      candidates = elements.Select(e => new Candidate(VsTestIds.ToFrameworkId(e.NaturalId.TestId), e.NaturalId.TestId, e.Project?.Name))
        .Distinct().ToList();

    myByNormalizedId = candidates.ToLookup(x => Normalize(x.TestId));
    myByName = candidates.ToLookup(x => Name(x.TestId));
  }

  public static SimilarTests InRepository(IUnitTestElementRepository repository)
  {
    using (UT.ReadLock())
      return new SimilarTests(repository.Query(SolutionCriterion.Instance).ToList());
  }

  /// <summary>
  /// Ids of tests that are the same up to case, quotes and spaces: almost certainly what was meant. For a test that only
  /// differs in the provider's form of the id (the requested name was not matched, e.g. a VSTest id with '#' instead of '::'),
  /// Rider's own id, which matches exactly.
  /// </summary>
  public IReadOnlyList<string> Meant(string testId)
  {
    var frameworkId = VsTestIds.ToFrameworkId(testId);
    return myByNormalizedId[Normalize(frameworkId)]
      .Select(x => x.TestId != frameworkId ? x.TestId : x.RiderId)
      .Where(x => x != testId)
      .Distinct().Take(MaxCandidates).ToList();
  }

  /// <summary>Tests whose last segment (method or test case without arguments) has the same name, in other classes or projects.</summary>
  public IReadOnlyList<string> SameName(string testId)
  {
    var frameworkId = VsTestIds.ToFrameworkId(testId);
    var parent = TestIdPath.AncestorCandidates(frameworkId).LastOrDefault();
    return myByName[Name(frameworkId)]
      .Where(x => x.TestId != frameworkId)
      // Tests of the same class first, they differ only in arguments
      .OrderByDescending(x => parent != null && x.TestId.StartsWith(parent, StringComparison.Ordinal))
      .ThenBy(x => x.TestId)
      .Select(x => x.TestId)
      .Distinct().Take(MaxCandidates).ToList();
  }

  private static string Name(string testId)
  {
    var segments = TestIdPath.Split(testId);
    return TestIdPath.StripArguments(segments[segments.Count - 1]).ToLowerInvariant();
  }

  private static string Normalize(string testId)
  {
    var builder = new StringBuilder(testId.Length);
    foreach (var c in testId)
    {
      if (c != '"' && c != '\'' && !char.IsWhiteSpace(c))
        builder.Append(char.ToLowerInvariant(c));
    }

    return builder.ToString();
  }

  public sealed class Candidate : IEquatable<Candidate>
  {
    public Candidate(string testId, string riderId, [CanBeNull] string project)
    {
      TestId = testId;
      RiderId = riderId;
      Project = project;
    }

    /// <summary>In the framework's form (no VSTest prefix), as the tools accept it.</summary>
    public string TestId { get; }

    /// <summary><c>UnitTestElementId.TestId</c>, with the VSTest prefix when that provider owns the test.</summary>
    public string RiderId { get; }

    [CanBeNull] public string Project { get; }

    public bool Equals(Candidate other) => other != null && RiderId == other.RiderId && Project == other.Project;
    public override bool Equals(object obj) => Equals(obj as Candidate);
    public override int GetHashCode() => RiderId.GetHashCode() ^ (Project?.GetHashCode() ?? 0);
  }
}
