package pl.info.lukaszm.testssupportplus.mcp

import com.intellij.mcpserver.McpToolCallResult
import com.intellij.mcpserver.McpToolset
import com.intellij.mcpserver.annotations.McpDescription
import com.intellij.mcpserver.annotations.McpTool
import com.intellij.mcpserver.project
import com.intellij.openapi.application.EDT
import com.intellij.openapi.project.Project
import com.jetbrains.rd.ide.model.McpToolRequest
import com.jetbrains.rd.ide.model.riderTestsSupportPlusModel
import com.jetbrains.rider.projectView.solution
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonObjectBuilder
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.put

private const val ANSWER = """
Every answer is JSON: {status: ok|partial|failed, error: {code, message}, result, warnings: [{code, message}],
suggestions: [{tool, arguments, reason} | {userAction, reason}]}. 'partial' means done, with the problems listed in warnings.
On failure nothing was changed. Follow 'suggestions': they are ready-made next calls (e.g. Rider's build_solution_start and
build_solution_state, then the same call again) or, with 'userAction', something only the user can do: ask them, do not
work around it.
"""

private const val TEST_IDS = """
Test ids are NUnit's fully qualified names, as `dotnet test --list-tests` and the Unit Tests window name them:
Namespace.Class.Method, with arguments Namespace.Class("fixtureArg").Method(1,"text") (string arguments quoted,
case-sensitive). A class or a method without arguments stands for all of its tests. Rider's own ids are accepted too:
with the VSTest provider they are prefixed, executor://nunit3testexecutor/#Namespace.Class.Method for a test without
arguments, executor://nunit3testexecutor/::Namespace.Class.Method(1) for a case (test_session_get lists them as riderId).
When an id is not found, the answer's 'didYouMean' gives the exact one.
"""

/**
 * MCP tools of Rider's MCP server for unit test sessions: an agent creates, changes and deletes sessions in the Unit Tests
 * window, saves them, and learns how the user's settings make Rider discover tests. Only the declarations live here:
 * the backend (Mcp/SessionTools.cs) checks the arguments and writes every answer, including every error.
 * Registered only when the MCP Server plugin is enabled (testssupportplus-mcp.xml).
 */
@Suppress("FunctionName", "unused")
class TestSessionToolset : McpToolset {

    @McpTool(name = "test_session_list")
    @McpDescription("""
Lists the unit test sessions (tabs of Rider's Unit Tests window): id, name, number of tests, whether an agent created it
(ownedByAgent), and whether it is active, running or locked. Call it before creating a session: reuse or update a session
you created instead of creating another one, the user does not want a pile of tabs. $ANSWER""")
    suspend fun test_session_list(): McpToolCallResult = call("test_session_list") {}

    @McpTool(name = "test_session_get")
    @McpDescription("""
Shows a unit test session with its tests: the exact test ids (and Rider's id when the VSTest provider owns the test),
project, target framework and provider. Warns about missing or stale builds of its projects. $ANSWER""")
    suspend fun test_session_get(
        @McpDescription("Id (preferred) or name of the session, from test_session_list")
        session: String,
    ): McpToolCallResult = call("test_session_get") { put("session", session) }

    @McpTool(name = "test_session_create")
    @McpDescription("""
Creates a unit test session in Rider's Unit Tests window and opens its tab. Pass exactly one source of tests:
'tests' (a list of test ids), 'runSettingsPath' (the tests a .runsettings file selects: TestCaseFilter is evaluated by
dotnet vstest, NUnit Where by the NUnit engine, both on the built test assemblies) or 'sessionFilePath' (a saved .rtsession).
Tests are resolved by name in Rider's test model; a test Rider has not discovered yet is replaced by its method or class
(warning TESTS_REPLACED_BY_PARENT), a test that matches nothing is reported with similar ids (TESTS_UNRESOLVED).
A session with the same name is an error by default: update it, or pass ifExists=replace.
The answer explains discovery problems (missing or stale builds, provider settings) when not every test was found.
$TEST_IDS $ANSWER""")
    suspend fun test_session_create(
        @McpDescription("Title of the session's tab. Required with 'tests'; defaults to the file name for the file sources")
        name: String? = null,
        @McpDescription("Test ids to put in the session")
        tests: List<String>? = null,
        @McpDescription(".runsettings file whose filter selects the tests; absolute or relative to the solution directory")
        runSettingsPath: String? = null,
        @McpDescription(".rtsession file saved by test_session_save or by the Unit Tests window; absolute or relative to the solution directory")
        sessionFilePath: String? = null,
        @McpDescription("Look the test ids up only in this project (its name in the solution)")
        project: String? = null,
        @McpDescription("When a session with this name exists: 'error' (default) or 'replace' its tests, keeping the session and its tab")
        ifExists: String? = null,
        @McpDescription("Allow replacing a session the user created (not an agent). Only when the user asked for it")
        allowUserSession: Boolean = false,
    ): McpToolCallResult = call("test_session_create") {
        put("name", name)
        putList("tests", tests)
        put("runSettingsPath", runSettingsPath)
        put("sessionFilePath", sessionFilePath)
        put("project", project)
        put("ifExists", ifExists)
        put("allowUserSession", allowUserSession)
    }

    @McpTool(name = "test_session_update")
    @McpDescription("""
Changes a unit test session in place, keeping its tab: replace all of its tests ('tests'), or add and remove some
('addTests', 'removeTests'), and/or rename it ('name'). Removing a class or method removes all of its tests.
Only sessions created by an agent can be changed, unless the user asked to change theirs (allowUserSession).
Running or locked sessions cannot be changed. $TEST_IDS $ANSWER""")
    suspend fun test_session_update(
        @McpDescription("Id (preferred) or name of the session, from test_session_list")
        session: String,
        @McpDescription("Test ids that replace all tests of the session")
        tests: List<String>? = null,
        @McpDescription("Test ids to add")
        addTests: List<String>? = null,
        @McpDescription("Test ids to remove (as test_session_get lists them)")
        removeTests: List<String>? = null,
        @McpDescription("New title of the session's tab")
        name: String? = null,
        @McpDescription("Look the test ids up only in this project (its name in the solution)")
        project: String? = null,
        @McpDescription("Allow changing a session the user created (not an agent). Only when the user asked for it")
        allowUserSession: Boolean = false,
    ): McpToolCallResult = call("test_session_update") {
        put("session", session)
        putList("tests", tests)
        putList("addTests", addTests)
        putList("removeTests", removeTests)
        put("name", name)
        put("project", project)
        put("allowUserSession", allowUserSession)
    }

    @McpTool(name = "test_session_delete")
    @McpDescription("""
Deletes a unit test session and closes its tab. Delete the sessions you created when they are no longer needed.
Only sessions created by an agent can be deleted, unless the user asked to delete theirs (allowUserSession).
A running session cannot be deleted. $ANSWER""")
    suspend fun test_session_delete(
        @McpDescription("Id (preferred) or name of the session, from test_session_list")
        session: String,
        @McpDescription("Allow deleting a session the user created (not an agent). Only when the user asked for it")
        allowUserSession: Boolean = false,
    ): McpToolCallResult = call("test_session_delete") {
        put("session", session)
        put("allowUserSession", allowUserSession)
    }

    @McpTool(name = "test_session_save")
    @McpDescription("""
Saves the tests of a unit test session to a .rtsession file (JSON), which test_session_create (sessionFilePath) and the
Unit Tests window load again, resolving the tests by name. $ANSWER""")
    suspend fun test_session_save(
        @McpDescription("Id (preferred) or name of the session, from test_session_list")
        session: String,
        @McpDescription("File to write, absolute or relative to the solution directory; .rtsession is appended when missing")
        path: String,
        @McpDescription("Replace the file if it exists")
        overwrite: Boolean = false,
    ): McpToolCallResult = call("test_session_save") {
        put("session", session)
        put("path", path)
        put("overwrite", overwrite)
    }

    @McpTool(name = "test_discovery_info")
    @McpDescription("""
Explains how Rider discovers the tests of the solution, which decides the test ids and when tests appear. Per project and
target framework: tests found per provider (NUnit3x = Rider's built-in NUnit provider, VsTest = through the adapter, with
prefixed ids), the build output and whether it is missing or older than the sources. Settings: disabled providers,
adapters ignored by VSTest, NUnit discovery method (metadata or test runner), a default .runsettings Rider runs tests with.
'implications' says what follows for the tests you ask for. Call it when tests are not found or are replaced by a parent.
$ANSWER""")
    suspend fun test_discovery_info(
        @McpDescription("Only this project (its name in the solution); all test projects when omitted")
        project: String? = null,
    ): McpToolCallResult = call("test_discovery_info") { put("project", project) }

    private suspend fun call(tool: String, arguments: JsonObjectBuilder.() -> Unit): McpToolCallResult {
        val project = currentCoroutineContext().project
        val answer = try {
            invoke(project, tool, buildJsonObject(arguments))
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            // The backend answers every call; this is the protocol failing, e.g. the solution is still loading
            val message = "Rider's backend did not answer (${e.message}). Wait until the solution has loaded and retry."
            return McpToolCallResult.error(buildJsonObject {
                put("status", "failed")
                put("error", buildJsonObject {
                    put("code", "BACKEND_UNAVAILABLE")
                    put("message", message)
                })
            }.toString())
        }
        // The backend's status decides: 'failed' changed nothing, 'partial' did the job with warnings
        val failed = Json.parseToJsonElement(answer).jsonObject["status"]?.jsonPrimitive?.content == "failed"
        return if (failed) McpToolCallResult.error(answer) else McpToolCallResult.text(answer)
    }

    companion object {
        /** Calls a tool on the backend and returns its JSON answer. */
        suspend fun invoke(project: Project, tool: String, arguments: JsonObject): String =
            withContext(Dispatchers.EDT) {
                project.solution.riderTestsSupportPlusModel.mcpTool.startSuspending(McpToolRequest(tool, arguments.toString()))
            }
    }
}

private fun JsonObjectBuilder.putList(key: String, values: List<String>?) {
    if (values != null) put(key, JsonArray(values.map(::JsonPrimitive)))
}
