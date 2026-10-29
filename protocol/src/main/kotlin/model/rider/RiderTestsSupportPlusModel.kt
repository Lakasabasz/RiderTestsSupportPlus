package model.rider

import com.jetbrains.rd.generator.nova.*
import com.jetbrains.rd.generator.nova.PredefinedType.*
import com.jetbrains.rider.model.nova.ide.SolutionModel

/**
 * The same flows as the Unit Tests window actions, without file dialogs and message boxes.
 * Used by the integration tests; the actions call the same backend code.
 */
@Suppress("unused")
object RiderTestsSupportPlusModel : Ext(SolutionModel.Solution) {
    private val SessionReport = structdef {
        field("sessionName", string.nullable)
        /** Test ids resolved exactly. */
        field("exact", immutableList(string))
        /** "requested test id -> ancestor test id" for tests replaced by their parent. */
        field("ancestors", immutableList(string))
        field("unresolved", immutableList(string))
        field("notes", immutableList(string))
        field("rescanned", bool)
    }

    private val SaveSessionRequest = structdef {
        /** Session to save; the active one when null. */
        field("sessionName", string.nullable)
        field("path", string)
    }

    /**
     * A call of one of the MCP tools (test_session_*, test_discovery_info). The backend owns the contract the agent sees:
     * it reads the arguments and builds the whole answer, so the frontend only passes JSON through.
     */
    private val McpToolRequest = structdef {
        field("tool", string)
        /** JSON object with the tool's arguments. */
        field("arguments", string)
    }

    init {
        /** Returns the number of saved tests. */
        call("saveSession", SaveSessionRequest, int)
        call("loadSession", string, SessionReport)
        call("importRunSettings", string, SessionReport)
        /** Returns the tool's answer as JSON (see Mcp/ToolResponse.cs); never fails, problems are in the answer. */
        call("mcpTool", McpToolRequest, string)
    }
}
