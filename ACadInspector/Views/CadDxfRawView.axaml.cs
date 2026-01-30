using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ACadInspector.Views;

public partial class CadDxfRawView : UserControl
{
    public CadDxfRawView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
