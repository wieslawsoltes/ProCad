using ReactiveUI.Avalonia;
using ACadInspector.ViewModels;

namespace ACadInspector.Views;

public partial class WorkspaceView : ReactiveUserControl<WorkspaceViewModel>
{
    public WorkspaceView()
    {
        InitializeComponent();
    }
}
