using System;
using WindowsCommander.Windows.Services;
using Xunit;

namespace WindowsCommander.Tests;

public class EnvironmentServiceTests
{
    private readonly EnvironmentService _environmentService;

    public EnvironmentServiceTests()
    {
        _environmentService = new EnvironmentService();
    }

    [Fact]
    public void GetEnvironmentVariable_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        string name = "";
        string scope = "process";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _environmentService.GetEnvironmentVariable(name, scope));
        Assert.Contains("Environment variable name must not be empty.", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void GetEnvironmentVariable_NullName_ThrowsArgumentException()
    {
        // Arrange
        string name = null!;
        string scope = "process";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _environmentService.GetEnvironmentVariable(name, scope));
        Assert.Contains("Environment variable name must not be empty.", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void GetEnvironmentVariable_InvalidScope_ThrowsArgumentException()
    {
        // Arrange
        string name = "TEST_VAR";
        string scope = "invalid_scope";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _environmentService.GetEnvironmentVariable(name, scope));
        Assert.Contains("Unsupported environment variable scope: invalid_scope", exception.Message);
    }

    [Fact]
    public void GetEnvironmentVariable_ValidProcessScope_ReturnsVariable()
    {
        // Arrange
        string name = "TEST_VAR_GET";
        string expectedValue = "TEST_VALUE_GET";
        string scope = "process";
        Environment.SetEnvironmentVariable(name, expectedValue, EnvironmentVariableTarget.Process);

        try
        {
            // Act
            var result = _environmentService.GetEnvironmentVariable(name, scope);

            // Assert
            Assert.Equal(expectedValue, result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void GetEnvironmentVariable_ValidProcessScopeDifferentCase_ReturnsVariable()
    {
        // Arrange
        string name = "TEST_VAR_GET_CASE";
        string expectedValue = "TEST_VALUE_GET_CASE";
        string scope = "ProCess";
        Environment.SetEnvironmentVariable(name, expectedValue, EnvironmentVariableTarget.Process);

        try
        {
            // Act
            var result = _environmentService.GetEnvironmentVariable(name, scope);

            // Assert
            Assert.Equal(expectedValue, result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void SetEnvironmentVariable_EmptyName_ThrowsArgumentException()
    {
        // Arrange
        string name = " ";
        string value = "value";
        string scope = "process";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _environmentService.SetEnvironmentVariable(name, value, scope));
        Assert.Contains("Environment variable name must not be empty.", exception.Message);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void SetEnvironmentVariable_InvalidScope_ThrowsArgumentException()
    {
        // Arrange
        string name = "TEST_VAR";
        string value = "value";
        string scope = "non_existent";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _environmentService.SetEnvironmentVariable(name, value, scope));
        Assert.Contains("Unsupported environment variable scope: non_existent", exception.Message);
    }

    [Fact]
    public void SetEnvironmentVariable_ValidProcessScope_SetsVariable()
    {
        // Arrange
        string name = "TEST_VAR_SET";
        string expectedValue = "TEST_VALUE_SET";
        string scope = "process";

        try
        {
            // Act
            _environmentService.SetEnvironmentVariable(name, expectedValue, scope);

            // Assert
            var result = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            Assert.Equal(expectedValue, result);
        }
        finally
        {
            // Cleanup
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
        }
    }
}
