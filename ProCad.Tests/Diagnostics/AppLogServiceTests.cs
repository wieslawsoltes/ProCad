using System;
using System.IO;
using System.Threading;
using ProCad.Diagnostics;
using Xunit;

namespace ProCad.Tests.Diagnostics;

public sealed class AppLogServiceTests
{
    [Fact]
    public void Log_AddsEntriesAndWritesFile()
    {
        var logPath = CreateTempLogPath();
        try
        {
            var service = CreateService(logPath, maxEntries: 16);

            service.Log(AppLogLevel.Information, "Tests", "hello world");

            WaitFor(() => service.Entries.Count == 1);
            var entry = Assert.Single(service.Entries);
            Assert.Equal(AppLogLevel.Information, entry.Level);
            Assert.Equal("Tests", entry.Category);
            Assert.Equal("hello world", entry.Message);

            var fileContents = File.ReadAllText(logPath);
            Assert.Contains("hello world", fileContents, StringComparison.Ordinal);
            Assert.Contains("[INFORMATION]", fileContents, StringComparison.Ordinal);
        }
        finally
        {
            DeleteIfExists(logPath);
        }
    }

    [Fact]
    public void Log_RespectsMaxEntries()
    {
        var logPath = CreateTempLogPath();
        try
        {
            var service = CreateService(logPath, maxEntries: 2);

            service.Log(AppLogLevel.Information, "Tests", "one");
            service.Log(AppLogLevel.Information, "Tests", "two");
            service.Log(AppLogLevel.Information, "Tests", "three");

            WaitFor(() => service.Entries.Count == 2);
            Assert.Collection(
                service.Entries,
                entry => Assert.Equal("two", entry.Message),
                entry => Assert.Equal("three", entry.Message));
        }
        finally
        {
            DeleteIfExists(logPath);
        }
    }

    private static void WaitFor(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            Thread.Sleep(10);
        }

        Assert.True(predicate(), "Timed out waiting for condition.");
    }

    private static string CreateTempLogPath()
    {
        return Path.Combine(Path.GetTempPath(), $"procad-log-{Guid.NewGuid():N}.log");
    }

    private static AppLogService CreateService(string logPath, int maxEntries)
    {
        return new AppLogService(logPath, maxEntries, static action => action());
    }

    private static void DeleteIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        File.Delete(path);
    }
}
