using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.UnitTestFramework.Criteria;
using JetBrains.ReSharper.UnitTestFramework.Elements;

namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

public enum TestResolutionKind
{
  /// <summary>The test itself was found.</summary>
  Exact,

  /// <summary>Only an ancestor (method or class) was found; the session runs all of its children.</summary>
  Ancestor,

  Unresolved
}

public sealed class TestResolution
{
  public TestResolution(TestRequest request, TestResolutionKind kind, IUnitTestElement element)
  {
    Request = request;
    Kind = kind;
    Element = element;
  }

  public TestRequest Request { get; }
  public TestResolutionKind Kind { get; }

  /// <summary>The test for <see cref="TestResolutionKind.Exact"/>, the ancestor for <see cref="TestResolutionKind.Ancestor"/>.</summary>
  public IUnitTestElement Element { get; }

  /// <summary>
  /// Natural id the test would have once Rider creates it (parametrized tests often appear only after a build or run).
  /// Keeping it in the session criterion lets the session pick the test up by itself.
  /// </summary>
  public UnitTestElementId ExpectedId =>
    Element == null
      ? null
      : new UnitTestElementId(Element.NaturalId.ProjectId, Element.NaturalId.TargetFrameworkId, Element.NaturalId.ProviderId,
        Request.TestId, Request.Salt);
}

public sealed class TestResolutionResult
{
  public TestResolutionResult(IReadOnlyList<TestResolution> items)
  {
    Items = items;
  }

  public IReadOnlyList<TestResolution> Items { get; }

  public IEnumerable<TestResolution> Exact => Items.Where(x => x.Kind == TestResolutionKind.Exact);
  public IEnumerable<TestResolution> Ancestors => Items.Where(x => x.Kind == TestResolutionKind.Ancestor);
  public IEnumerable<TestResolution> Unresolved => Items.Where(x => x.Kind == TestResolutionKind.Unresolved);

  public bool IsComplete => Items.All(x => x.Kind == TestResolutionKind.Exact);

  /// <summary>
  /// Exact tests by natural id, missing tests through their nearest ancestor (with all of its children),
  /// plus the expected ids of missing tests so they join the session once Rider discovers them.
  /// </summary>
  public IUnitTestElementCriterion CreateCriterion()
  {
    var ids = new HashSet<UnitTestElementId>();
    var ancestorIds = new HashSet<UnitTestElementId>();

    foreach (var item in Items)
    {
      switch (item.Kind)
      {
        case TestResolutionKind.Exact:
          ids.Add(item.Element.NaturalId);
          break;
        case TestResolutionKind.Ancestor:
          ancestorIds.Add(item.Element.NaturalId);
          ids.Add(item.ExpectedId);
          break;
      }
    }

    IUnitTestElementCriterion criterion = null;
    if (ids.Count > 0)
      criterion = criterion.Or(new TestElementCriterion(ids));
    if (ancestorIds.Count > 0)
      criterion = criterion.Or(new TestAncestorCriterion(ancestorIds));

    return criterion ?? NothingCriterion.Instance;
  }
}
