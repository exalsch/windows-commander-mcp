using System.Diagnostics;
using System.IO;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class ShellService : IShellService
{
    public void OpenPath(string pathOrUri, string? verb, IReadOnlyList<string>? arguments)
    {
        if (string.IsNullOrWhiteSpace(pathOrUri))
        {
            throw new ArgumentException("path_or_uri must not be empty.", nameof(pathOrUri));
        }

        var startInfo = new ProcessStartInfo(pathOrUri)
        {
            UseShellExecute = true
        };

        if (!string.IsNullOrWhiteSpace(verb))
        {
            startInfo.Verb = verb;
        }

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to open: {pathOrUri}");
    }

    public void ShowInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("path must not be empty.", nameof(path));
        }

        var fullPath = Path.GetFullPath(path);
        var argument = File.Exists(fullPath) ? $"/select,{fullPath}" : fullPath;
        _ = Process.Start(new ProcessStartInfo("explorer.exe", argument)
        {
            UseShellExecute = true
        });
    }
}
