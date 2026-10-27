using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.ReSharper.UnitTestFramework.Criteria;
using JetBrains.ReSharper.UnitTestFramework.Elements;
using JetBrains.ReSharper.UnitTestFramework.Persistence;

namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// Finds elements of Rider's unit test model by test id.
/// Matching is by <c>UnitTestElementId.TestId</c>, which for NUnit is exactly the adapter's FullyQualifiedName
/// (<c>Ns.TestClass(A).TestName(X)</c>); project and target framework only narrow the result. This is what makes
/// saved sessions survive a changed project GUID or target framework, unlike the built-in session import,
/// which requires the whole natural id to match. With the VSTest provider the id is the same name prefixed with
/// the executor URI (<see cref="VsTestIds"/>), so a request resolves under either provider.
/// </summary>
public sealed class TestElementResolver
{
  private readonly IUnitTestElementRepository myRepository;

  public TestElementResolver([NotNull] IUnitTestElementRepository repository)
  {
    myRepository = repository;
  }

  public TestResolutionResult Resolve([NotNull] IEnumerable<TestRequest> requests)
  {
    using (UT.ReadLock())
    {
      return new TestResolutionResult(requests.Select(ResolveOne).ToList());
    }
  }

  private TestResolution ResolveOne(TestRequest request)
  {
    var element = Find(request, request.TestId, request.ProviderId) ?? FindInOtherProvider(request, request.TestId, VsTestIds.Test);
    if (element != null)
      return new TestResolution(request, TestResolutionKind.Exact, element);

    foreach (var ancestorId in request.AncestorIds)
    {
      var ancestor = Find(request, ancestorId, request.ProviderId) ?? FindInOtherProvider(request, ancestorId, VsTestIds.Container);
      if (ancestor != null)
        return new TestResolution(request, TestResolutionKind.Ancestor, ancestor);
    }

    return new TestResolution(request, TestResolutionKind.Unresolved, null);
  }

  /// <summary>
  /// The same test under the other provider's id: the built-in NUnit provider uses the adapter's FullyQualifiedName,
  /// the VSTest provider prefixes it with the executor URI. A session saved or listed with one provider
  /// thus still resolves after switching to the other.
  /// </summary>
  [CanBeNull]
  private IUnitTestElement FindInOtherProvider(TestRequest request, string testId, Func<string, string> toVsTestId)
  {
    var vsTestId = toVsTestId(testId);
    if (!VsTestIds.IsVsTestId(testId))
      return Find(request, vsTestId, VsTestIds.ProviderId);

    // Ancestors of a VSTest test id carry the test's separator, containers have their own
    return Find(request, VsTestIds.ToFrameworkId(testId), null)
           ?? (vsTestId != testId ? Find(request, vsTestId, VsTestIds.ProviderId) : null);
  }

  [CanBeNull]
  private IUnitTestElement Find(TestRequest request, string testId, [CanBeNull] string providerId)
  {
    if (request.ProjectId != null && request.TargetFrameworkId != null && providerId != null)
    {
      var exact = myRepository.GetBy(new UnitTestElementId(request.ProjectId, request.TargetFrameworkId,
        providerId, testId, testId == request.TestId ? request.Salt : null));
      if (exact != null)
        return exact;
    }

    return myRepository.Query(new TestIdCriterion(testId))
      .Where(x => IsCandidate(request, x, providerId))
      .OrderByDescending(x => Score(request, x))
      .FirstOrDefault();
  }

  private static bool IsCandidate(TestRequest request, IUnitTestElement element, [CanBeNull] string providerId)
  {
    if (providerId != null && element.NaturalId.ProviderId != providerId)
      return false;

    // A test with the same name in a different project is a different test.
    if (request.ProjectId != null || request.ProjectName != null)
      return element.NaturalId.ProjectId == request.ProjectId || element.Project.Name == request.ProjectName;

    return true;
  }

  private static int Score(TestRequest request, IUnitTestElement element)
  {
    var score = 0;
    if (element.NaturalId.ProjectId == request.ProjectId) score += 4;
    if (element.Project.Name == request.ProjectName) score += 2;
    if (element.NaturalId.TargetFrameworkId == request.TargetFrameworkId) score += 1;
    return score;
  }
}
