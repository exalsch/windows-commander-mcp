namespace WindowsCommander.Core.Models;

public sealed record DirectoryEntry(
    string Path,
    string Type,
    long Size,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset AccessedAt,
    string Attributes,
    string Extension);

public sealed record FileReadResult(
    string Path,
    string Content,
    string Encoding,
    bool IsBase64,
    long BytesRead);

public sealed record FileWriteResult(
    string Path,
    long BytesWritten,
    bool CreatedDirectory);

public sealed record PathOperationResult(
    string Action,
    string SourcePath,
    string? DestinationPath,
    bool Completed);

public sealed record FileProperties(
    string Path,
    string Type,
    long Size,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt,
    DateTimeOffset AccessedAt,
    string Attributes,
    string Extension,
    string? Version,
    string? HashAlgorithm,
    string? Hash);

public sealed record FileSearchResult(
    string Path,
    string Type,
    long Size,
    DateTimeOffset ModifiedAt);
