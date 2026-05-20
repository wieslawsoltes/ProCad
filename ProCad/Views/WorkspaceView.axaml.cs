using ReactiveUI.Avalonia;
using ProCad.ViewModels;

namespace ProCad.Views;

public partial class WorkspaceView : ReactiveUserControl<WorkspaceViewModel>
{
    public WorkspaceView()
    {
        InitializeComponent();
    }
}
