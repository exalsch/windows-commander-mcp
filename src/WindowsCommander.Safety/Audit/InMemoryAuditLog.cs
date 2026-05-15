using WindowsCommander.Core.Safety;

namespace WindowsCommander.Safety.Audit;

public sealed class InMemoryAuditLog : IAuditLog
{
    private readonly object gate = new();
    private readonly Queue<AuditEntry> entries = new();
    private readonly int capacity;

    public InMemoryAuditLog(int capacity = 500)
    {
        this.capacity = Math.Max(1, capacity);
    }

    public void Record(AuditEntry entry)
    {
        lock (gate)
        {
            entries.Enqueue(entry);

            while (entries.Count > capacity)
            {
                entries.Dequeue();
            }
        }
    }

    public IReadOnlyList<AuditEntry> GetRecent(int limit, bool includeSensitiveArguments)
    {
        lock (gate)
        {
            return entries
                .Reverse()
                .Take(Math.Clamp(limit, 1, capacity))
                .Select(entry => includeSensitiveArguments ? entry : entry with { RedactedArguments = Redact(entry.RedactedArguments) })
                .ToArray();
        }
    }

    private static IReadOnlyDictionary<string, object?> Redact(IReadOnlyDictionary<string, object?> arguments)
    {
        return arguments.ToDictionary(
            pair => pair.Key,
            pair => IsSensitive(pair.Key) ? "***REDACTED***" : pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSensitive(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("key", StringComparison.OrdinalIgnoreCase);
    }
}
