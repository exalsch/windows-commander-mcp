using WindowsCommander.Core.Safety;
using WindowsCommander.Safety.Audit;

namespace WindowsCommander.Tests;

public class InMemoryAuditLogTests
{
    [Fact]
    public void GetRecent_RedactsSensitiveArguments_WhenSensitiveArgumentsAreDisabled()
    {
        var log = new InMemoryAuditLog();
        log.Record(new AuditEntry(
            "op-1",
            "execute_process",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "success",
            new Dictionary<string, object?>
            {
                ["apiKey"] = "secret-value",
                ["name"] = "visible-value"
            },
            null));

        var entries = log.GetRecent(limit: 1, includeSensitiveArguments: false);

        Assert.Equal("***REDACTED***", entries[0].RedactedArguments["apiKey"]);
        Assert.Equal("visible-value", entries[0].RedactedArguments["name"]);
    }
}
