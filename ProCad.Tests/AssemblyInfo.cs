using Avalonia.Headless;
using ReactiveUI;
using ReactiveUI.Builder;
using System.Reactive.Concurrency;
using System.Runtime.CompilerServices;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(ProCad.Tests.TestApp))]
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
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
