package com.jetbrains.rider.plugins.ridertestssupportplus.test

import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.jetbrains.rd.ide.model.SaveSessionRequest
import com.jetbrains.rd.ide.model.SessionReport
import com.jetbrains.rd.ide.model.riderTestsSupportPlusModel
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rd.framework.IRdCall
import com.jetbrains.rdclient.util.idea.waitAndPump
import com.jetbrains.rider.projectView.solution
import com.jetbrains.rider.test.annotations.Solution
import com.jetbrains.rider.test.annotations.TestSettings
import com.jetbrains.rider.test.enums.BuildTool
import com.jetbrains.rider.test.enums.sdk.SdkVersion
import com.jetbrains.rider.test.junit5.base.PerClassSolutionTestBase
import com.jetbrains.rider.test.scriptingApi.createUtFacade
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.MethodOrderer
import org.junit.jupiter.api.Order
import org.junit.jupiter.api.Tag
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestMethodOrder
import java.nio.file.Files
import java.nio.file.Path
import java.time.Duration

/**
 * End-to-end in a headless Rider with the backend: the sample NUnit solution is built, explored,
 * and the plugin's flows are driven through its protocol model (the same backend code the actions use).
 */
// The Rider test framework requires an episode tag on every test (TeamCity test grouping).
@Tag("episode/ridertestssupportplus")
@Solution("SampleTests")
@TestSettings(sdkVersion = SdkVersion.AUTODETECT, buildTool = BuildTool.AUTODETECT)
@TestMethodOrder(MethodOrderer.OrderAnnotation::class)
class SessionFlowsTest : PerClassSolutionTestBase() {

    private val ns = "Sample.Ns"

    private fun <A, T> call(call: IRdCall<A, T>, argument: A): T {
        val task = call.start(Lifetime.Eternal, argument)
        waitAndPump(Duration.ofMinutes(5), { task.result.valueOrNull != null }, { "RD call did not finish" })
        return task.result.valueOrNull!!.unwrap()
    }

    private fun model() = project.solution.riderTestsSupportPlusModel

    private fun prepare() {
        if (prepared) return
        buildWholeSolution(defaultSettings, null, Duration.ofMinutes(5))
        val ut = createUtFacade(project)
        try { ut.waitForDiscovering() } finally { ut.close() }
        prepared = true
    }

    private fun sessionFile(vararg tests: Triple<String, String?, String?>, name: String = "Loaded"): Path {
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

    private fun json(s: String) = "\"" + s.replace("\\", "\\\\").replace("\"", "\\\"") + "\""

    private fun report(r: SessionReport) = "exact=${r.exact}\nancestors=${r.ancestors}\nunresolved=${r.unresolved}\nnotes=${r.notes}"

    @Test
    @Order(1)
    fun actionsAreRegisteredInUnitTestsWindow() {
        val group = ActionManager.getInstance().getAction("Rider.UnitTesting.ExportOptions") as DefaultActionGroup
        val ids = ActionManager.getInstance().let { am ->
            fun collect(g: DefaultActionGroup): List<String> = g.childActionsOrStubs.flatMap {
                if (it is DefaultActionGroup) collect(it) else listOfNotNull(am.getId(it))
            }
            collect(group)
        }
        for (id in listOf("SaveSession", "LoadSession", "ImportRunSettings"))
            assertTrue(ids.contains("RiderTestsSupportPlus.Frontend.$id"), "$id missing in $ids")
    }

    @Test
    @Order(2)
    fun importRunSettingsSelectsWhatDotnetVstestRuns() {
        prepare()
        val runSettings = activeSolutionDirectory.resolve("filter.runsettings")
        val r = call(model().importRunSettings, runSettings.toString())
        // Category!=Slow & FullyQualifiedName~ParamFixture: an actual `dotnet vstest` run executes exactly these 6
        val expected = listOf("\"A\"", "\"b.c\"").flatMap { a ->
            listOf("$ns.ParamFixture($a).Method(1)", "$ns.ParamFixture($a).Method(2)", "$ns.ParamFixture($a).Plain")
        }
        assertEquals(expected.sorted(), r.exact.sorted(), report(r))
        assertTrue(r.ancestors.isEmpty() && r.unresolved.isEmpty(), report(r))
        assertEquals("filter", r.sessionName)

        // The frontend's Unit Tests window shows the new session with those tests
        val ut = createUtFacade(project)
        try {
            val session = ut.waitForAnySession(Duration.ofSeconds(60))
            // Counts every node: project, namespace, fixtures, methods and the 6 tests; also expands the tree
            ut.waitForSessionElements(session, 13, Duration.ofSeconds(60))
            val names = ut.getAllTests(session).map { it.descriptor.text }
            assertEquals(listOf("Method(1)", "Method(1)", "Method(2)", "Method(2)", "Plain", "Plain"), names.sorted(), "session tree")
        } finally {
            ut.close()
        }
    }

    @Test
    @Order(3)
    fun importRunSettingsWithNUnitWhere() {
        prepare()
        // `dotnet vstest --ListFullyQualifiedTests` ignores <NUnit><Where>; the plugin asks the NUnit engine instead
        val r = call(model().importRunSettings, activeSolutionDirectory.resolve("where.runsettings").toString())
        assertEquals(listOf("$ns.ParamFixture(\"A\").Method(1)", "$ns.Plain.Simple"), r.exact.sorted(), report(r))
        assertTrue(r.ancestors.isEmpty() && r.unresolved.isEmpty(), report(r))
        assertTrue(r.notes.any { it.contains("2 tests listed from") }, report(r))
    }

    @Test
    @Order(3)
    fun loadResolvesParametrizedTestsByName() {
        prepare()
        val ids = listOf(
            "$ns.ParamFixture(\"A\").Method(1)",
            "$ns.ParamFixture(\"b.c\").Method(\"x.y\")",
            "$ns.Plain.Two(\"a,b\",1.5d)",
            "$ns.Plain.Two(\"q\\\"uote\",2)",
        )
        val r = call(model().loadSession, sessionFile(*ids.map { Triple(it, null, null) }.toTypedArray()).toString())
        assertEquals(ids.sorted(), r.exact.sorted(), report(r))
        assertEquals("Loaded", r.sessionName)
    }

    @Test
    @Order(4)
    fun loadSurvivesChangedProjectIdAndTargetFramework() {
        prepare()
        val id = "$ns.ParamFixture(\"A\").Method(2)"
        val r = call(model().loadSession,
            sessionFile(Triple(id, "{00000000-0000-0000-0000-000000000000}", ".NETCoreApp,Version=v1.0")).toString())
        assertEquals(listOf(id), r.exact, report(r))
    }

    @Test
    @Order(5)
    fun missingTestFallsBackToItsParent() {
        prepare()
        val id = "$ns.ParamFixture(\"A\").Method(99)"
        val r = call(model().loadSession, sessionFile(Triple(id, null, null)).toString())
        assertEquals(listOf("$id -> $ns.ParamFixture(\"A\").Method"), r.ancestors, report(r))
        assertTrue(r.rescanned, "missing tests should trigger a rescan")
    }

    @Test
    @Order(6)
    fun saveThenLoadRoundTrip() {
        prepare()
        val ids = listOf("$ns.ParamFixture(\"b.c\").Method(2)", "$ns.Plain.Simple")
        val loaded = call(model().loadSession,
            sessionFile(*ids.map { Triple(it, null, null) }.toTypedArray(), name = "RoundTrip").toString())
        assertEquals(ids.sorted(), loaded.exact.sorted(), report(loaded))

        val saved = Files.createTempFile("saved", ".rtsession")
        val count = call(model().saveSession, SaveSessionRequest("RoundTrip", saved.toString()))
        val content = Files.readString(saved)
        assertEquals(ids.size, count, content)
        for (id in ids) assertTrue(content.contains("\"TestId\": ${json(id)}"), content)

        val reloaded = call(model().loadSession, saved.toString())
        assertEquals(ids.sorted(), reloaded.exact.sorted(), report(reloaded))
    }

    private companion object {
        var prepared = false
    }
}
