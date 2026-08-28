namespace VolturaAir.Host.Features.AiAssistant;

internal sealed record CodexThreadSummary(string Id, string Title, string WorkingDirectory);
internal sealed record CodexTranscriptEntry(string Id, string Sender, string Text);
internal sealed record CodexThreadDetail(CodexThreadSummary Summary, IReadOnlyList<CodexTranscriptEntry> Entries);
internal sealed record CodexTurnHandle(string ThreadId, string TurnId);

internal sealed class CodexCompatibilityException(string message, Exception? innerException = null)
    : Exception(message, innerException);
