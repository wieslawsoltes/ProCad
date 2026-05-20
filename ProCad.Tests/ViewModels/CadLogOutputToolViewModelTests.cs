using System;
using System.IO;
using System.Reactive.Threading.Tasks;
using System.Threading;
using ProCad.Diagnostics;
using ProCad.ViewModels;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadLogOutputToolViewModelTests
{
    [Fact]
    public async Task ClearLogCommand_ClearsEntriesAndUpdatesStatus()
    {
        var logPath = CreateTempLogPath();
        try
        {
            var service = new AppLogService(logPath, maxEntries: 16);
            service.Log(AppLogLevel.Warning, "Tests", "before clear");
            WaitFor(() => service.Entries.Count == 1);
            var viewModel = new CadLogOutputToolViewModel(service);

            await viewModel.ClearLogCommand.Execute().ToTask();

            WaitFor(() => service.Entries.Count == 0);
            Assert.Contains("No log entries", viewModel.StatusText, StringComparison.Ordinal);
        }
        finally
        {
            DeleteIfExists(logPath);
        }
    }

    [Fact]
    public async Task ClearSearchAndFilterCommands_ResetTextState()
    {
        var logPath = CreateTempLogPath();
        try
        {
            var service = new AppLogService(logPath, maxEntries: 16);
            var viewModel = new CadLogOutputToolViewModel(service)
            {
                SearchText = "warn",
                FilterText = "editor"
            };

            await viewModel.ClearSearchCommand.Execute().ToTask();
            await viewModel.ClearFilterCommand.Execute().ToTask();

            Assert.Equal(string.Empty, viewModel.SearchText);
            Assert.Equal(string.Empty, viewModel.FilterText);
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
        return Path.Combine(Path.GetTempPath(), $"procad-log-vm-{Guid.NewGuid():N}.log");
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
