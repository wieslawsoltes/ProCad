using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Skia;
using ReactiveUI.Avalonia;

namespace ProCad.Tests;

public sealed class TestApp : Application
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHarfBuzz()
            .UseReactiveUI(_ => { })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
                FrameBufferFormat = PixelFormat.Bgra8888
            });
    }
}
