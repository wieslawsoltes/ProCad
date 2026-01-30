using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Skia;

namespace ACadInspector.Tests;

public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                FrameBufferFormat = PixelFormat.Bgra8888
            });
    }
}
