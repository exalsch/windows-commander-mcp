namespace WindowsCommander.Core.Safety;

public interface IRiskPolicyService
{
    RiskLevel Classify(string toolName, IReadOnlyDictionary<string, object?> arguments);

    bool RequiresConfirmation(string toolName, IReadOnlyDictionary<string, object?> arguments);
}

public interface IAuditLog
{
    void Record(AuditEntry entry);

    IReadOnlyList<AuditEntry> GetRecent(int limit, bool includeSensitiveArguments);
}
