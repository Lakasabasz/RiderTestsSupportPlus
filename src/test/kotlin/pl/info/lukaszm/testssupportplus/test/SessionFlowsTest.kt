package pl.info.lukaszm.testssupportplus.test

import com.intellij.ide.DataManager
import com.intellij.openapi.Disposable
import com.intellij.openapi.actionSystem.ActionManager
import com.intellij.openapi.actionSystem.DefaultActionGroup
import com.intellij.openapi.application.ApplicationManager
import com.intellij.openapi.fileChooser.FileChooserDescriptor
import com.intellij.openapi.fileChooser.FileChooserDialog
import com.intellij.openapi.fileChooser.FileChooserFactory
import com.intellij.openapi.fileChooser.FileSaverDescriptor
import com.intellij.openapi.fileChooser.FileSaverDialog
import com.intellij.openapi.project.Project
import com.intellij.openapi.util.Disposer
import com.intellij.openapi.vfs.VirtualFile
import com.intellij.openapi.vfs.VirtualFileWrapper
import com.intellij.testFramework.replaceService
import com.intellij.mcpserver.McpToolset
import com.intellij.mcpserver.impl.ReflectionToolsProvider
import com.jetbrains.rd.framework.IRdCall
import com.jetbrains.rd.ide.model.McpToolRequest
import com.jetbrains.rd.ide.model.SaveSessionRequest
import com.jetbrains.rd.ide.model.SessionReport
import com.jetbrains.rd.ide.model.riderTestsSupportPlusModel
import com.jetbrains.rd.util.lifetime.Lifetime
import com.jetbrains.rdclient.util.idea.waitAndPump
import com.jetbrains.rider.projectView.solution
import com.jetbrains.rider.test.annotations.Solution
import com.jetbrains.rider.test.annotations.TestSettings
import com.jetbrains.rider.test.enums.BuildTool
import com.jetbrains.rider.test.enums.sdk.SdkVersion
import com.jetbrains.rider.test.junit5.base.PerClassSolutionTestBase
import com.jetbrains.rider.test.scriptingApi.createUtFacade
import com.jetbrains.rider.test.scriptingApi.executeAction
import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.MethodOrderer
import org.junit.jupiter.api.Order
import org.junit.jupiter.api.Tag
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.TestMethodOrder
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
import pl.info.lukaszm.testssupportplus.mcp.TestSessionToolset
import java.awt.Component
import java.nio.file.Files
import java.nio.file.Path
import java.nio.file.attribute.FileTime
import java.time.Duration
import java.time.Instant
import javax.swing.JTextField

/**
 * End-to-end in a headless Rider with the backend: the sample NUnit solution is built, explored,
 * and the plugin's flows are driven through its protocol model (the same backend code the actions use).
 */
// The Rider test framework requires an episode tag on every test (TeamCity test grouping).
@Tag("episode/ridertestssupportplus")
@Solution("SampleTests")
@TestSettings(sdkVersion = SdkVersion.AUTODETECT, buildTool = BuildTool.AUTODETECT)
@TestMethodOrder(MethodOrderer.OrderAnnotation::class)
class SessionFlowsTest : PluginSolutionTest() {

    @Test
    @Order(1)
    fun actionsAreRegisteredInUnitTestsWindow() {
        val am = ActionManager.getInstance()
        fun group(id: String) = am.getAction(id) as DefaultActionGroup
        fun actionIds(g: DefaultActionGroup): List<String> = g.childActionsOrStubs.flatMap {
            if (it is DefaultActionGroup) actionIds(it) else listOfNotNull(am.getId(it))
        }
        fun assertActions(groupId: String, expected: List<String>, absent: List<String> = emptyList()) {
            val ids = actionIds(group(groupId))
            for (id in expected)
                assertTrue(ids.contains("RiderTestsSupportPlus.Frontend.$id"), "$id missing in $groupId: $ids")
            for (id in absent)
                assertTrue(!ids.contains("RiderTestsSupportPlus.Frontend.$id"), "$id unexpected in $groupId: $ids")
        }

        // Session tab: Export/Import drop-down
        assertActions("Rider.UnitTesting.ExportOptions", listOf("SaveSession", "LoadSession", "ImportRunSettings"))
        // Explorer: no session to save; a drop-down right after Rider's Import Session button, and the context menu
        assertActions("Rider.UnitTesting.TopExplorerToolbar", listOf("LoadSession", "ImportRunSettings"), listOf("SaveSession"))
        assertActions("Rider.UnitTesting.Explorer.PopupContext.Plugin", listOf("LoadSession", "ImportRunSettings"), listOf("SaveSession"))
        val toolbar = group("Rider.UnitTesting.TopExplorerToolbar").childActionsOrStubs.map { am.getId(it) }
        assertEquals("RiderTestsSupportPlus.ExplorerImport",
            toolbar.getOrNull(toolbar.indexOf("RiderUnitTestImportSessionAction") + 1), "Explorer toolbar: $toolbar")
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

    @Test
    @Order(7)
    fun aWrongFilePointsToTheRightAction() {
        prepare()
        // Load Session and Import Session from .runsettings sit next to each other in the menus
        val session = sessionFile(Triple("$ns.Plain.Simple", null, null)).toString()
        val imported = call(model().importRunSettings, session)
        assertTrue(imported.notes.any { it.contains("open it with Load Session") }, report(imported))
        assertTrue(imported.notes.none { it.contains("select no tests") }, report(imported))

        val runSettings = activeSolutionDirectory.resolve("filter.runsettings").toString()
        val error = runCatching { call(model().loadSession, runSettings) }.exceptionOrNull()
        assertTrue(error?.message?.contains("use Import Session from .runsettings") == true, "load error: $error")
    }

    @Test
    @Order(8)
    fun frontendActionsRunTheirBackendActions() {
        prepare()
        // The proxies forward execution to the backend action with the id they name (their update is frontend-only),
        // so run each one and check the backend asked for its file; the dialog is cancelled, nothing else happens.
        // Save needs a session: open one and activate its tab, so the data context is the Unit Tests window's.
        call(model().loadSession, sessionFile(Triple("$ns.Plain.Simple", null, null), name = "Actions").toString())
        val ut = createUtFacade(project)
        val titles = mutableListOf<String>()
        val disposable = Disposer.newDisposable()
        try {
            ut.activateSession(ut.waitForAnySession(Duration.ofSeconds(60)))
            ApplicationManager.getApplication().replaceService(FileChooserFactory::class.java,
                CancellingFileChooserFactory(FileChooserFactory.getInstance()) { titles += it }, disposable)
            for ((id, title) in listOf(
                "SaveSession" to "Save Test Session",
                "LoadSession" to "Load Test Session",
                "ImportRunSettings" to "Import Tests from Run Settings",
            )) {
                titles.clear()
                executeAction("RiderTestsSupportPlus.Frontend.$id", DataManager.getInstance().dataContext)
                waitAndPump(Duration.ofSeconds(30), { titles.isNotEmpty() }, { "$id did not reach its backend action" })
                assertEquals(listOf(title), titles, id)
            }
        } finally {
            Disposer.dispose(disposable)
            ut.close()
        }
    }

    @Test
    @Order(9)
    fun mcpToolsAreRegistered() {
        assertTrue(McpToolset.EP.extensionList.any { it is TestSessionToolset }, "toolset not registered")
        val tools = ReflectionToolsProvider().getTools().associateBy { it.descriptor.name }
        for (name in listOf("test_session_list", "test_session_get", "test_session_create", "test_session_update",
            "test_session_delete", "test_session_save", "test_discovery_info"))
            assertTrue(tools.containsKey(name), "$name missing in ${tools.keys}")

        // The schema agents see: optional parameters are optional, list parameters are arrays
        val create = tools.getValue("test_session_create").descriptor.inputSchema
        assertEquals(setOf("name", "tests", "runSettingsPath", "sessionFilePath", "project", "ifExists", "allowUserSession"),
            create.propertiesSchema.keys.intersect(setOf("name", "tests", "runSettingsPath", "sessionFilePath", "project", "ifExists", "allowUserSession")),
            create.propertiesSchema.toString())
        assertTrue(create.requiredProperties.none { it in create.propertiesSchema.keys - "projectPath" }, create.requiredProperties.toString())
        assertEquals("array", create.propertiesSchema.getValue("tests").jsonObject["type"]?.jsonPrimitive?.content, create.propertiesSchema.toString())
        assertEquals(setOf("session"), tools.getValue("test_session_update").descriptor.inputSchema.requiredProperties - "projectPath")
    }

    @Test
    @Order(10)
    fun mcpCreateUpdateDeleteKeepsOneSession() {
        prepare()
        val simple = "$ns.Plain.Simple"
        val case1 = "$ns.ParamFixture(\"A\").Method(1)"
        val created = mcp("test_session_create", "name" to "Agent CRUD", "tests" to listOf(simple))
        assertEquals("ok", created.status(), created.toString())
        val id = created.text("result", "session", "id")
        assertEquals("true", created.text("result", "session", "ownedByAgent"), created.toString())
        waitForTestCount(id, 1)

        // The same name again: an error pointing at the existing session, nothing created
        val duplicate = mcp("test_session_create", "name" to "Agent CRUD", "tests" to listOf(case1))
        assertEquals("SESSION_EXISTS", duplicate.errorCode(), duplicate.toString())
        assertTrue(duplicate.suggestedTools().contains("test_session_update"), duplicate.toString())

        // Changing it in place keeps the id (and the tab)
        val added = mcp("test_session_update", "session" to id, "addTests" to listOf(case1))
        assertEquals("ok", added.status(), added.toString())
        assertEquals(id, added.text("result", "session", "id"))
        waitForTestCount(id, 2)

        val removed = mcp("test_session_update", "session" to "Agent CRUD", "removeTests" to listOf(simple), "name" to "Agent CRUD 2")
        assertEquals("ok", removed.status(), removed.toString())
        val afterRemove = waitForTestCount(id, 1)
        assertEquals(case1, afterRemove.at("result", "tests").jsonArray.single().text("testId"), afterRemove.toString())
        assertEquals("Agent CRUD 2", afterRemove.text("result", "session", "name"))

        val replaced = mcp("test_session_create", "name" to "Agent CRUD 2", "tests" to listOf(simple), "ifExists" to "replace")
        assertEquals(id, replaced.text("result", "session", "id"), replaced.toString())
        assertEquals("false", replaced.text("result", "created"))

        val saved = Files.createTempFile("agent", ".rtsession")
        val save = mcp("test_session_save", "session" to id, "path" to saved.toString(), "overwrite" to true)
        assertEquals("ok", save.status(), save.toString())
        assertTrue(Files.readString(saved).contains(simple.replace("\"", "\\\"")), Files.readString(saved))

        val deleted = mcp("test_session_delete", "session" to id)
        assertEquals("ok", deleted.status(), deleted.toString())
        val list = mcp("test_session_list")
        assertTrue(list.at("result", "sessions").jsonArray.none { it.text("id") == id }, list.toString())
        assertEquals("SESSION_NOT_FOUND", mcp("test_session_get", "session" to id).errorCode())
    }

    @Test
    @Order(11)
    fun mcpProtectsTheUsersSessions() {
        prepare()
        // Sessions opened through the Unit Tests window belong to the user
        call(model().loadSession, sessionFile(Triple("$ns.Plain.Simple", null, null), name = "User session").toString())
        val denied = mcp("test_session_delete", "session" to "User session")
        assertEquals("SESSION_NOT_OWNED", denied.errorCode(), denied.toString())
        assertTrue(denied.suggestedTools().contains("test_session_create"), denied.toString())

        val allowed = mcp("test_session_delete", "session" to "User session", "allowUserSession" to true)
        assertEquals("ok", allowed.status(), allowed.toString())
    }

    @Test
    @Order(12)
    fun mcpSuggestsTheIdThatWasMeant() {
        prepare()
        // Unquoted string argument (still finds the class, which replaces it) and wrong case: suggested back, with a corrected call
        val answer = mcp("test_session_create", "name" to "Agent typo",
            "tests" to listOf("$ns.ParamFixture(A).Method(1)", "$ns.plain.simple", "$ns.Plain.Simple"))
        assertEquals("partial", answer.status(), answer.toString())
        assertTrue(answer.warningCodes().containsAll(listOf("TESTS_UNRESOLVED", "TESTS_REPLACED_BY_PARENT")), answer.toString())
        fun meant(list: String) = answer.at("result", "resolution", list).jsonArray
            .associate { it.text("test") to it.jsonObject["didYouMean"]?.jsonArray?.map { x -> x.jsonPrimitive.content } }
        assertEquals(listOf("$ns.ParamFixture(\"A\").Method(1)"), meant("replacedTests")["$ns.ParamFixture(A).Method(1)"], answer.toString())
        assertEquals(listOf("$ns.Plain.Simple"), meant("unresolvedTests")["$ns.plain.simple"], answer.toString())
        // Repeating create replaces the session it has just created instead of failing on the name
        val retry = answer["suggestions"]!!.jsonArray.first { it.jsonObject["tool"]?.jsonPrimitive?.content == "test_session_create" }
        assertEquals(listOf("$ns.ParamFixture(\"A\").Method(1)", "$ns.Plain.Simple", "$ns.Plain.Simple"),
            retry.at("arguments", "tests").jsonArray.map { it.jsonPrimitive.content }, retry.toString())
        assertEquals("replace", retry.text("arguments", "ifExists"), retry.toString())
        mcp("test_session_delete", "session" to answer.text("result", "session", "id"))

        val nothing = mcp("test_session_create", "name" to "Agent none", "tests" to listOf("$ns.Nope.Missing"))
        assertEquals("NO_TESTS_RESOLVED", nothing.errorCode(), nothing.toString())
        assertTrue(mcp("test_session_list").at("result", "sessions").jsonArray.none { it.text("name") == "Agent none" })
    }

    @Test
    @Order(13)
    fun mcpCreatesFromRunSettingsAndPointsAtTheRightSource() {
        prepare()
        val answer = mcp("test_session_create", "runSettingsPath" to "filter.runsettings", "name" to "Agent filter")
        assertEquals("ok", answer.status(), answer.toString())
        assertEquals(6, answer.at("result", "resolution", "resolved").jsonPrimitive.int, answer.toString())
        mcp("test_session_delete", "session" to answer.text("result", "session", "id"))

        val session = sessionFile(Triple("$ns.Plain.Simple", null, null)).toString()
        val wrongKind = mcp("test_session_create", "runSettingsPath" to session)
        assertEquals("WRONG_FILE_KIND", wrongKind.errorCode(), wrongKind.toString())
        val fix = wrongKind["suggestions"]!!.jsonArray.first().jsonObject
        assertEquals(session, fix.text("arguments", "sessionFilePath"), wrongKind.toString())

        val missing = mcp("test_session_create", "runSettingsPath" to "nope.runsettings")
        assertEquals("FILE_NOT_FOUND", missing.errorCode(), missing.toString())
        assertTrue(missing.toString().contains("filter.runsettings"), "should suggest the existing files: $missing")
    }

    @Test
    @Order(14)
    fun mcpDiscoveryInfoReportsProvidersAndStaleBuilds() {
        prepare()
        val info = mcp("test_discovery_info")
        assertEquals("ok", info.status(), info.toString())
        val project = info.at("result", "projects").jsonArray.single { it.text("project") == "SampleTests" }
        assertTrue(project.at("providers").jsonArray.any { it.text("provider") == "NUnit3x" }, project.toString())
        assertEquals("Fresh", project.text("build", "state"), project.toString())

        // A source newer than the assembly: the build is stale and the answer says how to build
        val source = activeSolutionDirectory.resolve("SampleTests/Tests.cs")
        val original = Files.getLastModifiedTime(source)
        try {
            Files.setLastModifiedTime(source, FileTime.from(Instant.now().plusSeconds(3600)))
            val stale = mcp("test_discovery_info", "project" to "SampleTests")
            assertEquals("partial", stale.status(), stale.toString())
            assertTrue(stale.warningCodes().contains("BUILD_OUTPUT_STALE"), stale.toString())
            assertTrue(stale.suggestedTools().containsAll(listOf("build_solution_start", "build_solution_state", "test_discovery_info")), stale.toString())
        } finally {
            Files.setLastModifiedTime(source, original)
        }

        val unknown = mcp("test_discovery_info", "project" to "sampletests")
        assertEquals("PROJECT_NOT_FOUND", unknown.errorCode(), unknown.toString())
        assertTrue(unknown.toString().contains("\"SampleTests\""), unknown.toString())
    }

    /** Records the title of every file dialog and cancels it. */
    private class CancellingFileChooserFactory(
        private val original: FileChooserFactory,
        private val onDialog: (String) -> Unit,
    ) : FileChooserFactory() {
        override fun createFileChooser(descriptor: FileChooserDescriptor, project: Project?, parent: Component?) =
            FileChooserDialog { _, _ -> onDialog(descriptor.title); emptyArray() }

        override fun createSaveFileDialog(descriptor: FileSaverDescriptor, project: Project?) = cancelledSave(descriptor)
        override fun createSaveFileDialog(descriptor: FileSaverDescriptor, parent: Component) = cancelledSave(descriptor)

        private fun cancelledSave(descriptor: FileSaverDescriptor) = object : FileSaverDialog {
            override fun save(baseDir: VirtualFile?, filename: String?): VirtualFileWrapper? {
                onDialog(descriptor.title)
                return null
            }

            override fun save(baseDir: Path?, filename: String?): VirtualFileWrapper? {
                onDialog(descriptor.title)
                return null
            }
        }

        override fun createPathChooser(descriptor: FileChooserDescriptor, project: Project?, parent: Component?) =
            original.createPathChooser(descriptor, project, parent)

        override fun createFileTextField(descriptor: FileChooserDescriptor, showHidden: Boolean, parent: Disposable?) =
            original.createFileTextField(descriptor, showHidden, parent)

        override fun installFileCompletion(field: JTextField, descriptor: FileChooserDescriptor, showHidden: Boolean, parent: Disposable?) =
            original.installFileCompletion(field, descriptor, showHidden, parent)
    }
}
