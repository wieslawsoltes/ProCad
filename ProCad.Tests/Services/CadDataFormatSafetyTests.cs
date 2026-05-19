using ProCad.Services;
using Avalonia.Input.Platform;
using Xunit;

namespace ProCad.Tests.Services;

public sealed class CadDataFormatSafetyTests
{
    [Fact]
    public void BlockInsertDragDropFormats_StaticInitialization_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            _ = CadInsertDragDropFormats.BlockNameFormat;
            _ = CadInsertDragDropFormats.BlockNamePlatformFormat;
        });

        Assert.Null(exception);
    }

    [Fact]
    public void AvaloniaClipboardPlatformFacade_StaticFormats_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
        {
            _ = new AvaloniaClipboardPlatformFacade(new NullClipboardAccessor());
        });

        Assert.Null(exception);
    }

    private sealed class NullClipboardAccessor : IClipboardAccessor
    {
        public IClipboard? Clipboard => null;

        public void SetProvider(Func<IClipboard?> providerFactory)
        {
        }
    }
}
