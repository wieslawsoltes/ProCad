using ProCad.Diagnostics;
using ProCad.ViewModels;
using Xunit;

namespace ProCad.Tests.ViewModels;

public sealed class CadIoOptionsViewModelTests
{
    [Fact]
    public void Constructor_CreatesIndependentColumnDefinitionsForReadAndWriteGrids()
    {
        var viewModel = new CadIoOptionsViewModel(new FastPathDiagnosticsService());

        Assert.NotSame(viewModel.ReadColumnDefinitions, viewModel.WriteColumnDefinitions);
        Assert.Equal(viewModel.ReadColumnDefinitions.Count, viewModel.WriteColumnDefinitions.Count);
        Assert.NotSame(viewModel.ReadColumnDefinitions[0], viewModel.WriteColumnDefinitions[0]);
    }
}
