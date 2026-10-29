# Tests Support Plus

Rider plugin by Łukasz Mastalerz that adds to the **Unit Tests** window what the built-in one lacks.
Everything else in the window keeps working as before.

> The source of truth is [eu.buddy.works/lakasabasz/ridertestssupportplus](https://eu.buddy.works/lakasabasz/ridertestssupportplus).
> The GitHub repository is a read-only mirror, updated on every push; changes made there are overwritten.

## Features

- **Save / Load Session** (`.rtsession`, JSON), including parametrized tests such as `TestClass(A).TestName(X)`.
  Tests are resolved by name after loading, so a session survives a changed project GUID or target framework.
  Missing tests trigger a rescan of the solution, then fall back to their nearest parent (method or class),
  and anything not resolved exactly is reported.
- **Import Session from `.runsettings`**: the session holds exactly the tests `dotnet vstest` would run with the file.
  - `RunConfiguration/TestCaseFilter` is evaluated by `dotnet vstest --ListFullyQualifiedTests`
    (for NUnit projects `Category` is treated as `TestCategory`, as NUnit3TestAdapter does).
  - `NUnit/Where` is evaluated by the NUnit engine of the test project (vstest's listing ignores it).
    With both present, `TestCaseFilter` wins, as in an actual run.
- Import and rescan run as background tasks with progress and cancellation.

## Usage

In the **Unit Tests** tool window open a session tab and use its *Export/Import* menu:

| Action | What it does |
|---|---|
| *Save Session (Tests Support Plus)…* | Saves the tests of the session to a `.rtsession` file |
| *Load Session (Tests Support Plus)…* | Opens a session from a `.rtsession` file, resolving its tests by name |
| *Import Session from .runsettings…* | Creates a session from the tests the file selects |

![The Export/Import menu of a session tab with the plugin's actions](docs/images/session-export-import-menu.png)

![A session loaded from demo/Bookstore/sessions/checkout.rtsession: parametrized fixtures and test cases resolved by name](docs/images/load-session-checkout.png)

Tests that no longer exist are replaced by their nearest parent and reported
(demo/Bookstore/sessions/outdated.rtsession):

![Report after loading a session saved before a refactoring: three tests replaced by a parent](docs/images/load-session-outdated-report.png)

*Load Session* and *Import Session from .runsettings* are also in **Explorer**: in the drop-down right after
its *Import Session* button and in the context menu of the test tree.

![The drop-down in Explorer next to its Import Session button](docs/images/explorer-import-menu.png)

Import lists the build output of the active configuration, so build the test projects first;
the report says which assembly (and from when) the tests were listed from.

![A session imported from demo/Bookstore/runsettings/pricing-poland.runsettings (NUnit/Where): 16 tests](docs/images/import-runsettings-session.png)

To try it out, open the demo solution [demo/Bookstore](demo/Bookstore/README.md): parametrized NUnit tests,
ready `.rtsession` files and `.runsettings` files, each with the scenario it shows.

## Tools for AI agents (MCP)

With Rider's MCP Server enabled (*Settings → Tools → MCP Server*), the plugin adds tools to it, so agents connected
to Rider (Claude Code, Junie, Cursor, …) manage test sessions themselves:

| Tool | What it does |
|---|---|
| `test_session_list` | Sessions with id, name, test count and whether an agent created them |
| `test_session_get` | A session's tests with their exact ids, project, framework and provider |
| `test_session_create` | A session from test ids, a `.runsettings` file or a `.rtsession` file; a taken name is an error unless `ifExists=replace` |
| `test_session_update` | Replaces, adds or removes tests and renames, keeping the session and its tab |
| `test_session_delete` | Deletes a session and closes its tab |
| `test_session_save` | Saves a session to a `.rtsession` file |
| `test_discovery_info` | How Rider discovers the tests: per project tests by provider (built-in NUnit or VSTest) and build state (missing, stale), the settings behind it and what follows from them |

Agents change and delete only the sessions they created, unless told the user's session may be changed (`allowUserSession`).
Every answer has a status, error and warning codes, and suggestions: ready-made next calls, e.g. Rider's
`build_solution_start` / `build_solution_state` and the same call again when a test assembly is missing or older than its sources,
or the corrected test ids (`ParamFixture(A)` → `ParamFixture("A")`).

## Requirements

- Rider 2026.2 (build 262)
- NUnit 3/4 test projects
- For `NUnit/Where`: .NET (Core) test projects referencing NUnit3TestAdapter
- For the MCP tools: Rider's MCP Server plugin (bundled, optional)

## Build and install

Requires JDK 25 and the .NET SDK.

```
./gradlew buildPlugin
```

Install `output/ReSharperPlugin.RiderTestsSupportPlus-<version>.zip` in Rider via
*Settings → Plugins → ⚙ → Install Plugin from Disk…* and restart Rider.

The version is `PluginVersion` in `gradle.properties`; bump it for every build you install.
The newest section of [CHANGELOG.md](CHANGELOG.md) becomes the release notes.

## Tests

```
./gradlew test
```

Starts a headless Rider with the plugin (Rider's integration test framework), opens
`src/test/testData/solutions/SampleTests` (NUnit 4), builds it and drives the plugin's flows through its protocol model.

## Project layout

| Path | Content |
|---|---|
| `src/dotnet/ReSharperPlugin.RiderTestsSupportPlus` | Backend (C#): test resolution, session files, `.runsettings` import, actions, MCP tools (`Mcp/`), discovery and build state (`Discovery/`) |
| `src/rider/main` | Frontend (Kotlin): proxy actions in the Unit Tests window, MCP tool declarations, `plugin.xml` |
| `src/tools/RiderTestsSupportPlus.NUnitLister` | Helper that lists tests selected by `NUnit/Where` with the NUnit engine |
| `protocol` | RD protocol model (used by the MCP tools and the integration tests) |
| `src/test` | Integration tests and the sample solution |
| `demo/Bookstore` | Demo solution for trying the plugin and taking screenshots |

Design decisions and findings: [DESIGN.md](DESIGN.md).

## License

[MIT](LICENSE)
