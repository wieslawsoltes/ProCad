using ReactiveUI.Avalonia;
using ACadInspector.ViewModels;

namespace ACadInspector.Views;

public partial class MainView : ReactiveUserControl<ShellViewModel>
{
    public MainView()
    {
        InitializeComponent();
    }
}
