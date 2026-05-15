namespace WindowsCommander.Core.Safety;

public sealed record AuditEntry(
    string OperationId,
    string ToolName,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Status,
    IReadOnlyDictionary<string, object?> RedactedArguments,
    string? ErrorSummary);
