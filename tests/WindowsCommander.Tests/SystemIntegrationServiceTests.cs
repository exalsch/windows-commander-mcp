using WindowsCommander.Windows.Services;

namespace WindowsCommander.Tests;

public class SystemIntegrationServiceTests
{
    [Fact]
    public void RegistryService_ReadsWindowsCurrentVersionKey()
    {
        var service = new RegistryService();

        var values = service.ReadRegistry("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");

        Assert.Single(values);
        Assert.Equal("ProductName", values[0].Name);
        Assert.NotNull(values[0].Value);
    }

    [Fact]
    public void ApplicationService_RejectsUnsupportedIdentifierType()
    {
        var service = new ApplicationService();

        var exception = Assert.Throws<ArgumentException>(() => service.LaunchApp("notepad", "invalid", null));

        Assert.Contains("Unsupported identifier type", exception.Message);
    }

    [Fact]
    public void WindowsServiceDiscoveryService_ListsAtLeastOneService()
    {
        var service = new WindowsServiceDiscoveryService();

        var services = service.ListServices(null, null);

        Assert.NotEmpty(services);
    }

    [Fact]
    public void ScreenService_ReturnsDisplayMetrics()
    {
        var service = new ScreenService();

        var metrics = service.GetDisplayMetrics();

        Assert.NotEmpty(metrics);
        Assert.All(metrics, metric => Assert.True(metric.VirtualCoordinates.Width > 0));
    }
}
