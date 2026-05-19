using ReactiveUI.Avalonia;
using ProCad.ViewModels;

namespace ProCad.Views;

public partial class MainView : ReactiveUserControl<ShellViewModel>
{
    public MainView()
    {
        InitializeComponent();
    }
}
