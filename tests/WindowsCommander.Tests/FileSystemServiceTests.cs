using WindowsCommander.Windows.Services;

namespace WindowsCommander.Tests;

public class FileSystemServiceTests
{
    [Fact]
    public async Task WriteReadAndProperties_RoundTripTextFile()
    {
        var service = new FileSystemService();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "windows-commander-tests", Guid.NewGuid().ToString("N"));
        var filePath = Path.Combine(tempDirectory, "sample.txt");

        try
        {
            var writeResult = await service.WriteFileAsync(filePath, "hello", "utf-8", overwrite: false, createDirectories: true, CancellationToken.None);
            var readResult = await service.ReadFileAsync(filePath, "utf-8", maxBytes: null, asBase64: false, CancellationToken.None);
            var properties = await service.GetFilePropertiesAsync(filePath, "SHA256", CancellationToken.None);

            Assert.True(writeResult.CreatedDirectory);
            Assert.Equal("hello", readResult.Content);
            Assert.Equal("file", properties.Type);
            Assert.Equal("SHA256", properties.HashAlgorithm);
            Assert.False(string.IsNullOrWhiteSpace(properties.Hash));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void ListDirectory_ReturnsCreatedFile()
    {
        var service = new FileSystemService();
        var tempDirectory = Path.Combine(Path.GetTempPath(), "windows-commander-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var filePath = Path.Combine(tempDirectory, "listed.txt");
        File.WriteAllText(filePath, "listed");

        try
        {
            var entries = service.ListDirectory(tempDirectory, recursive: false, includeHidden: false, pattern: "*.txt");

            Assert.Contains(entries, entry => entry.Path == filePath && entry.Type == "file");
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
