using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ReSharperPlugin.RiderTestsSupportPlus.Mcp;

/// <summary>
/// The answer of every MCP tool. Agents act on what they get back, so every problem comes with a code and with
/// suggestions: concrete next calls (tool and arguments) or, when only the user can help, what to ask them to do.
/// A partial result is not an error: the session was created or changed, and <see cref="Warnings"/> say what is missing.
/// </summary>
public sealed class ToolResponse
{
  private static readonly JsonSerializerSettings JsonSettings = new()
  {
    ContractResolver = new CamelCasePropertyNamesContractResolver(),
    NullValueHandling = NullValueHandling.Ignore,
    Formatting = Formatting.Indented
  };

  /// <summary><c>ok</c>, <c>partial</c> (done, with <see cref="Warnings"/>) or <c>failed</c> (nothing changed, see <see cref="Error"/>).</summary>
  public string Status => Error != null ? "failed" : Warnings.Count > 0 ? "partial" : "ok";

  [CanBeNull] public ToolProblem Error { get; set; }

  [CanBeNull] public object Result { get; set; }

  public List<ToolProblem> Warnings { get; } = new();

  /// <summary>What to do next, most useful first.</summary>
  public List<Suggestion> Suggestions { get; } = new();

  public bool ShouldSerializeWarnings() => Warnings.Count > 0;
  public bool ShouldSerializeSuggestions() => Suggestions.Count > 0;

  public void Warn(string code, string message, params Suggestion[] suggestions)
  {
    if (Warnings.Any(x => x.Code == code && x.Message == message))
      return;
    Warnings.Add(new ToolProblem(code, message));
    Suggest(suggestions);
  }

  public void Suggest(params Suggestion[] suggestions)
  {
    foreach (var suggestion in suggestions)
    {
      if (!Suggestions.Any(x => x.SameAs(suggestion)))
        Suggestions.Add(suggestion);
    }
  }

  public string ToJson() => JsonConvert.SerializeObject(this, JsonSettings);
}

public sealed class ToolProblem
{
  public ToolProblem(string code, string message)
  {
    Code = code;
    Message = message;
  }

  /// <summary>One of <see cref="ProblemCodes"/>.</summary>
  public string Code { get; }

  public string Message { get; }
}

/// <summary>A next step: a tool call with its arguments, or an action only the user can take (a setting, a decision).</summary>
public sealed class Suggestion
{
  private Suggestion(string tool, JObject arguments, string userAction, string reason)
  {
    Tool = tool;
    Arguments = arguments;
    UserAction = userAction;
    Reason = reason;
  }

  [CanBeNull] public string Tool { get; }
  [CanBeNull] public JObject Arguments { get; }

  /// <summary>Set instead of <see cref="Tool"/> when only the user can do it; the agent should ask them.</summary>
  [CanBeNull] public string UserAction { get; }

  public string Reason { get; }

  /// <param name="arguments">An anonymous object, e.g. <c>new { session = id }</c>.</param>
  public static Suggestion Call(string tool, [CanBeNull] object arguments, string reason) =>
    new(tool, arguments == null ? null : JObject.FromObject(arguments, JsonSerializer.CreateDefault(new JsonSerializerSettings
    {
      NullValueHandling = NullValueHandling.Ignore
    })), null, reason);

  public static Suggestion AskUser(string action, string reason) => new(null, null, action, reason);

  internal bool SameAs(Suggestion other) =>
    Tool == other.Tool && UserAction == other.UserAction && JToken.DeepEquals(Arguments, other.Arguments);
}

/// <summary>Ends a tool call with <see cref="ToolResponse.Error"/>; nothing was changed.</summary>
public sealed class ToolException : Exception
{
  public ToolException(string code, string message, params Suggestion[] suggestions) : base(message)
  {
    Code = code;
    Suggestions = suggestions;
  }

  public string Code { get; }
  public IReadOnlyList<Suggestion> Suggestions { get; }

  /// <summary>Context that helps to recover, e.g. the existing sessions when the requested one is not found.</summary>
  [CanBeNull] public object Result { get; set; }
}
