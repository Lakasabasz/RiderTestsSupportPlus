using System.Collections.Generic;
using System.Text;

namespace ReSharperPlugin.RiderTestsSupportPlus.Resolution;

/// <summary>
/// Splits test ids like <c>Ns.TestClass("a.b").TestName(1.5d)</c> into segments. Dots inside parentheses
/// and string/char literals are not separators, so arguments containing dots survive intact.
/// </summary>
public static class TestIdPath
{
  public static IReadOnlyList<string> Split(string testId)
  {
    var segments = new List<string>();
    var current = new StringBuilder();
    var depth = 0;
    char? quote = null;

    for (var i = 0; i < testId.Length; i++)
    {
      var c = testId[i];
      if (quote != null)
      {
        current.Append(c);
        if (c == '\\' && i + 1 < testId.Length)
          current.Append(testId[++i]);
        else if (c == quote)
          quote = null;
        continue;
      }

      switch (c)
      {
        case '"':
        case '\'':
          quote = c;
          break;
        case '(':
          depth++;
          break;
        case ')':
          if (depth > 0) depth--;
          break;
        case '.' when depth == 0:
          segments.Add(current.ToString());
          current.Clear();
          continue;
      }

      current.Append(c);
    }

    segments.Add(current.ToString());
    return segments;
  }

  /// <summary>
  /// Candidate ids of ancestors, nearest first, limited to the method and class level:
  /// <c>Ns.C(A).M(X)</c> gives <c>Ns.C(A).M</c>, <c>Ns.C(A)</c>, <c>Ns.C</c>.
  /// Namespaces are never returned, a fallback that wide would silently pull in unrelated tests.
  /// </summary>
  public static IReadOnlyList<string> AncestorCandidates(string testId)
  {
    var segments = Split(testId);
    var result = new List<string>();
    if (segments.Count < 2)
      return result;

    var method = segments[segments.Count - 1];
    var type = segments[segments.Count - 2];
    var ns = Join(segments, segments.Count - 2);

    var typeId = Combine(ns, type);
    var methodWithoutArgs = StripArguments(method);
    if (methodWithoutArgs != method)
      result.Add(typeId + "." + methodWithoutArgs);

    result.Add(typeId);

    var typeWithoutArgs = StripArguments(type);
    if (typeWithoutArgs != type)
      result.Add(Combine(ns, typeWithoutArgs));

    return result;
  }

  public static string StripArguments(string segment)
  {
    var index = segment.IndexOf('(');
    return index > 0 ? segment.Substring(0, index) : segment;
  }

  private static string Join(IReadOnlyList<string> segments, int count)
  {
    var builder = new StringBuilder();
    for (var i = 0; i < count; i++)
    {
      if (i > 0) builder.Append('.');
      builder.Append(segments[i]);
    }

    return builder.ToString();
  }

  private static string Combine(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
