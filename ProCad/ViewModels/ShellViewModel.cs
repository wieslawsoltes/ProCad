using System;
using System.Reactive.Linq;
using ProCad.Diagnostics;
using ProCad.Services;
using ReactiveUI;

namespace ProCad.ViewModels;

public sealed class ShellViewModel : ViewModelBase, IScreen
{
    private readonly WorkspaceViewModelFactory _workspaceFactory;

    public RoutingState Router { get; } = new();

    public ShellViewModel(WorkspaceViewModelFactory workspaceFactory)
    {
        _workspaceFactory = workspaceFactory;
        AppLog.Write("ShellViewModel ctor start.");
        AppLog.Write("ShellViewModel navigate start.");
        var workspace = _workspaceFactory.Create(this);
        Router.Navigate.Execute(workspace).Subscribe();
        AppLog.Write("ShellViewModel navigate subscribed.");
    }
}
