package pl.info.lukaszm.testssupportplus.test

import com.jetbrains.rd.framework.IRdCall
import com.jetbrains.rd.ide.model.McpToolRequest
import com.jetbrains.rd.ide.model.SessionReport
import com.jetbrains.rd.ide.model.riderTestsSupportPlusModel
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rdclient.util.idea.waitAndPump
import com.jetbrains.rider.projectView.solution
import com.jetbrains.rider.test.junit5.base.PerClassSolutionTestBase
import com.jetbrains.rider.test.scriptingApi.createUtFacade
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration

/**
 * A sample NUnit solution in a headless Rider with the backend, built and explored once per test class,
 * and helpers to drive the plugin's flows through its protocol model (the same backend code the actions
 * and the MCP tools use).
 */
abstract class PluginSolutionTest : PerClassSolutionTestBase() {

    protected val ns = "Sample.Ns"

    protected fun <A, T> call(call: IRdCall<A, T>, argument: A): T {
        val task = call.start(Lifetime.Eternal, argument)
        waitAndPump(Duration.ofMinutes(5), { task.result.valueOrNull != null }, { "RD call did not finish" })
        return task.result.valueOrNull!!.unwrap()
    }

    protected fun model() = project.solution.riderTestsSupportPlusModel

    /** Runs once before the solution is built and explored, e.g. to change backend settings. */
    protected open fun configure() {}

    protected fun prepare() {
        if (!prepared.add(javaClass)) return
        configure()
        buildWholeSolution(defaultSettings, null, Duration.ofMinutes(5))
        val ut = createUtFacade(project)
        try { ut.waitForDiscovering() } finally { ut.close() }
    }

    protected fun sessionFile(vararg tests: Triple<String, String?, String?>, name: String = "Loaded"): Path {
        // (testId, projectId, targetFramework); null = leave the saved value out
        val items = tests.joinToString(",\n") { (id, projectId, tfm) ->
            buildString {
                append("{\"ProviderId\":\"NUnit3x\",\"ProjectName\":\"SampleTests\",\"TestId\":")
                append(json(id))
                projectId?.let { append(",\"ProjectId\":").append(json(it)) }
                tfm?.let { append(",\"TargetFramework\":").append(json(it)) }
                append("}")
            }
        }
        val file = Files.createTempFile("session", ".rtsession")
        Files.writeString(file, "{\"FormatVersion\":1,\"Name\":${json(name)},\"Tests\":[\n$items\n]}")
        return file
    }

    protected fun json(s: String) = "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\""

    protected fun report(r: SessionReport) = "exact=${r.exact}\nancestors=${r.ancestors}\nunresolved=${r.unresolved}\nnotes=${r.notes}"

    /** Calls an MCP tool on the backend, as TestSessionToolset does, and returns its answer. */
    protected fun mcp(tool: String, vararg arguments: Pair<String, Any?>): JsonObject {
        val json = buildJsonObject {
            for ((key, value) in arguments) put(key, when (value) {
                null -> continue
                is String -> JsonPrimitive(value)
                is Boolean -> JsonPrimitive(value)
                is List<*> -> JsonArray(value.map { JsonPrimitive(it as String) })
                else -> error("Unsupported argument $key=$value")
            })
        }
        return Json.parseToJsonElement(call(model().mcpTool, McpToolRequest(tool, json.toString()))).jsonObject
    }

    protected fun JsonElement.at(vararg path: String): JsonElement = path.fold(this) { e, key -> e.jsonObject[key] ?: error("No '$key' in $e") }
    protected fun JsonElement.text(vararg path: String) = at(*path).jsonPrimitive.content
    protected fun JsonObject.status() = text("status")
    protected fun JsonObject.errorCode() = text("error", "code")
    protected fun JsonObject.warningCodes() = this["warnings"]?.jsonArray?.map { it.text("code") } ?: emptyList()
    protected fun JsonObject.suggestedTools() = this["suggestions"]?.jsonArray?.mapNotNull { it.jsonObject["tool"]?.jsonPrimitive?.content } ?: emptyList()

    /** Number of tests in the session once Rider has evaluated its criterion. */
    protected fun waitForTestCount(sessionId: String, expected: Int): JsonObject {
        var answer = mcp("test_session_get", "session" to sessionId)
        waitAndPump(Duration.ofSeconds(60), {
            answer = mcp("test_session_get", "session" to sessionId)
            answer.at("result", "session", "tests").jsonPrimitive.int == expected
        }, { "session $sessionId should have $expected tests: $answer" })
        return answer
    }

    private companion object {
        val prepared = mutableSetOf<Class<*>>()
    }
}
