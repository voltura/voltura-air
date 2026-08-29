namespace VolturaAir.Host.Features.AiAssistant;

internal static class AiAssistantProfile
{
    internal const string ThreadName = "Voltura Air Assistant";
    internal static readonly string[] DisabledFeatures =
    [
        "apps", "auth_elicitation", "browser_use", "browser_use_external", "browser_use_full_cdp_access",
        "computer_use", "hooks", "image_generation", "in_app_browser", "multi_agent", "plugin_sharing",
        "plugins", "recommended_plugins", "remote_plugin", "shell_snapshot", "shell_tool",
        "skill_mcp_dependency_install", "tool_call_mcp_elicitation", "unified_exec"
    ];
    internal const string DeveloperInstructions =
        "You are the Voltura Air Assistant. Help with Voltura Air from the bundled maintained documentation and answer " +
        "read-only questions using only the host-provided read tools. Begin with one concise sentence describing what you " +
        "will check, then perform only the necessary read-only investigation and provide the complete " +
        "answer. Never create, edit, move, or delete data; change settings; start, stop, or control applications or processes; " +
        "access the network; expose credentials or authentication material; or execute Voltura actions. Never ask for or " +
        "attempt to use shell, command execution, file-change, app, browser, MCP, or computer-control tools. Refuse requests to " +
        "perform an action or reveal secrets. If available information does not establish an answer, say so instead of guessing.";

    internal static string KnowledgeRoot => Path.Combine(AppContext.BaseDirectory, "AiAssistantKnowledge");
    internal static string SkillPath => Path.Combine(KnowledgeRoot, "AssistantSkill", "SKILL.md");

    internal static AiAssistantAvailability Availability =>
        !CodexAppServerProcess.IsAvailable
            ? AiAssistantAvailability.CodexMissing
            : !Directory.Exists(KnowledgeRoot) ||
              !File.Exists(Path.Combine(KnowledgeRoot, "docs", "README.md")) ||
              !File.Exists(SkillPath)
                ? AiAssistantAvailability.KnowledgeMissing
                : AiAssistantAvailability.Ready;

    internal static bool IsAvailable => Availability == AiAssistantAvailability.Ready;
}

internal enum AiAssistantAvailability
{
    Ready,
    CodexMissing,
    KnowledgeMissing
}
