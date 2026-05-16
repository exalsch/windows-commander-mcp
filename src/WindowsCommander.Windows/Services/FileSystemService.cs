using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class FileSystemService : IFileSystemService
{
    public IReadOnlyList<DirectoryEntry> ListDirectory(string path, bool recursive, bool includeHidden, string? pattern)
    {
        var directory = new DirectoryInfo(NormalizeExistingDirectory(path));
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
        };

        return directory
            .EnumerateFileSystemInfos(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, options)
            .Select(ToDirectoryEntry)
            .ToArray();
    }

    public async Task<FileReadResult> ReadFileAsync(string path, string? encoding, int? maxBytes, bool asBase64, CancellationToken cancellationToken)
    {
        var fullPath = NormalizeExistingFile(path);
        var bytes = await ReadLimitedBytesAsync(fullPath, maxBytes, cancellationToken);

        if (asBase64)
        {
            return new FileReadResult(fullPath, Convert.ToBase64String(bytes), "base64", IsBase64: true, bytes.Length);
        }

        var textEncoding = ResolveEncoding(encoding);
        return new FileReadResult(fullPath, textEncoding.GetString(bytes), textEncoding.WebName, IsBase64: false, bytes.Length);
    }

    public async Task<FileWriteResult> WriteFileAsync(string path, string content, string? encoding, bool overwrite, bool createDirectories, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var directoryPath = Path.GetDirectoryName(fullPath);
        var createdDirectory = false;

        if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
        {
            if (!createDirectories)
            {
                throw new DirectoryNotFoundException($"Directory does not exist: {directoryPath}");
            }

            Directory.CreateDirectory(directoryPath);
            createdDirectory = true;
        }

        if (File.Exists(fullPath) && !overwrite)
        {
            throw new IOException($"File already exists and overwrite is false: {fullPath}");
        }

        var bytes = ResolveEncoding(encoding).GetBytes(content);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return new FileWriteResult(fullPath, bytes.LongLength, createdDirectory);
    }

    public Task<PathOperationResult> CopyMoveDeletePathAsync(
        string action,
        string sourcePath,
        string? destinationPath,
        bool recursive,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var destinationFullPath = string.IsNullOrWhiteSpace(destinationPath) ? null : Path.GetFullPath(destinationPath);

        switch (action.ToLowerInvariant())
        {
            case "copy":
                CopyPath(sourceFullPath, RequireDestination(destinationFullPath), recursive, overwrite);
                break;
            case "move":
                MovePath(sourceFullPath, RequireDestination(destinationFullPath), overwrite);
                break;
            case "delete":
                DeletePath(sourceFullPath, recursive);
                break;
            case "recycle":
                throw new NotSupportedException("Recycle is not implemented in this slice; use delete for permanent deletion.");
            default:
                throw new ArgumentException($"Unsupported path action: {action}");
        }

        return Task.FromResult(new PathOperationResult(action, sourceFullPath, destinationFullPath, Completed: true));
    }

    public async Task<FileProperties> GetFilePropertiesAsync(string path, string? hashAlgorithm, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var info = GetFileSystemInfo(fullPath);
        var fileInfo = info as FileInfo;
        var hash = fileInfo is not null && !string.IsNullOrWhiteSpace(hashAlgorithm)
            ? await ComputeHashAsync(fileInfo.FullName, hashAlgorithm, cancellationToken)
            : null;

        return new FileProperties(
            info.FullName,
            info is DirectoryInfo ? "directory" : "file",
            fileInfo?.Length ?? 0,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc,
            info.Attributes.ToString(),
            fileInfo?.Extension ?? string.Empty,
            fileInfo is null ? null : FileVersionInfo.GetVersionInfo(fileInfo.FullName).FileVersion,
            string.IsNullOrWhiteSpace(hashAlgorithm) ? null : hashAlgorithm.ToUpperInvariant(),
            hash);
    }

    public IReadOnlyList<FileSearchResult> SearchFiles(IReadOnlyList<string> roots, string? namePattern, string? contentQuery, bool includeHidden, int? maxResults)
    {
        var limit = Math.Clamp(maxResults ?? 100, 1, 1000);
        var results = new List<FileSearchResult>(limit);

        foreach (var root in roots)
        {
            if (results.Count >= limit)
            {
                break;
            }

            var rootPath = NormalizeExistingDirectory(root);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = includeHidden ? 0 : FileAttributes.Hidden | FileAttributes.System
            };

            foreach (var file in Directory.EnumerateFiles(rootPath, string.IsNullOrWhiteSpace(namePattern) ? "*" : namePattern, options))
            {
                if (results.Count >= limit)
                {
                    break;
                }

                if (!MatchesContent(file, contentQuery))
                {
                    continue;
                }

                var info = new FileInfo(file);
                results.Add(new FileSearchResult(info.FullName, "file", info.Length, info.LastWriteTimeUtc));
            }
        }

        return results;
    }

    private static DirectoryEntry ToDirectoryEntry(FileSystemInfo info)
    {
        var fileInfo = info as FileInfo;
        return new DirectoryEntry(
            info.FullName,
            info is DirectoryInfo ? "directory" : "file",
            fileInfo?.Length ?? 0,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            info.LastAccessTimeUtc,
            info.Attributes.ToString(),
            fileInfo?.Extension ?? string.Empty);
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(string fullPath, int? maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes is null)
        {
            return await File.ReadAllBytesAsync(fullPath, cancellationToken);
        }

        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "max_bytes must be greater than zero.");
        }

        await using var stream = File.OpenRead(fullPath);
        var length = (int)Math.Min(maxBytes.Value, stream.Length);
        var buffer = new byte[length];
        var totalRead = 0;

        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead == length ? buffer : buffer[..totalRead];
    }

    private static Encoding ResolveEncoding(string? encoding)
    {
        return string.IsNullOrWhiteSpace(encoding) ? Encoding.UTF8 : Encoding.GetEncoding(encoding);
    }

    private static string NormalizeExistingDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Directory does not exist: {fullPath}");
        }

        return fullPath;
    }

    private static string NormalizeExistingFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File does not exist: {fullPath}", fullPath);
        }

        return fullPath;
    }

    private static FileSystemInfo GetFileSystemInfo(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path);
        }

        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        throw new FileNotFoundException($"Path does not exist: {path}", path);
    }

    private static string RequireDestination(string? destinationPath)
    {
        return string.IsNullOrWhiteSpace(destinationPath)
            ? throw new ArgumentException("destination_path is required for copy and move actions.")
            : destinationPath;
    }

    private static void CopyPath(string sourcePath, string destinationPath, bool recursive, bool overwrite)
    {
        if (File.Exists(sourcePath))
        {
            File.Copy(sourcePath, destinationPath, overwrite);
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source path does not exist: {sourcePath}", sourcePath);
        }

        if (!recursive)
        {
            throw new IOException("Recursive must be true to copy directories.");
        }

        CopyDirectory(sourcePath, destinationPath, overwrite);
    }

    private static void MovePath(string sourcePath, string destinationPath, bool overwrite)
    {
        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, destinationPath, overwrite);
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Source path does not exist: {sourcePath}", sourcePath);
        }

        if (Directory.Exists(destinationPath) && overwrite)
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        Directory.Move(sourcePath, destinationPath);
    }

    private static void DeletePath(string sourcePath, bool recursive)
    {
        if (File.Exists(sourcePath))
        {
            File.Delete(sourcePath);
            return;
        }

        if (Directory.Exists(sourcePath))
        {
            Directory.Delete(sourcePath, recursive);
            return;
        }

        throw new FileNotFoundException($"Source path does not exist: {sourcePath}", sourcePath);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            var destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(filePath));
            File.Copy(filePath, destinationFile, overwrite);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceDirectory))
        {
            var destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, destinationSubdirectory, overwrite);
        }
    }

    private static async Task<string> ComputeHashAsync(string path, string hashAlgorithm, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var normalizedAlgorithm = hashAlgorithm.ToUpperInvariant();
        var hashBytes = normalizedAlgorithm switch
        {
            "SHA256" => await SHA256.HashDataAsync(stream, cancellationToken),
            "SHA1" => await SHA1.HashDataAsync(stream, cancellationToken),
            "MD5" => await MD5.HashDataAsync(stream, cancellationToken),
            _ => throw new ArgumentException($"Unsupported hash algorithm: {hashAlgorithm}")
        };

        return Convert.ToHexString(hashBytes);
    }

    private static bool MatchesContent(string filePath, string? contentQuery)
    {
        if (string.IsNullOrWhiteSpace(contentQuery))
        {
            return true;
        }

        try
        {
            return File.ReadLines(filePath).Any(line => line.Contains(contentQuery, StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
