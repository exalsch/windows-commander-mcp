using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class EnvironmentService : IEnvironmentService
{
    public string? GetEnvironmentVariable(string name, string scope)
    {
        ValidateName(name);
        return Environment.GetEnvironmentVariable(name, ParseScope(scope));
    }

    public void SetEnvironmentVariable(string name, string? value, string scope)
    {
        ValidateName(name);
        Environment.SetEnvironmentVariable(name, value, ParseScope(scope));
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Environment variable name must not be empty.", nameof(name));
        }
    }

    private static EnvironmentVariableTarget ParseScope(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "process" => EnvironmentVariableTarget.Process,
            "user" => EnvironmentVariableTarget.User,
            "machine" => EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentException($"Unsupported environment variable scope: {scope}")
        };
    }
}
