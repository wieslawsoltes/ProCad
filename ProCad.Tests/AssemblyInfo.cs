using Avalonia.Headless;
using ReactiveUI;
using ReactiveUI.Builder;
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProCad.Tests.TestApp))]
// Avalonia 12 does not dispose headless resources in per-assembly mode.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerTest)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ProCad.Tests;

internal static class ReactiveUiTestBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        RxSchedulers.MainThreadScheduler = CurrentThreadScheduler.Instance;
    }
}
