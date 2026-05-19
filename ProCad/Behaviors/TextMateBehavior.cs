using System;
using Avalonia;
using Avalonia.Xaml.Interactivity;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace ProCad.Behaviors;

public sealed class TextMateBehavior : Behavior<TextEditor>
{
    public static readonly StyledProperty<string?> GrammarExtensionProperty =
        AvaloniaProperty.Register<TextMateBehavior, string?>(nameof(GrammarExtension), ".txt");

    public static readonly StyledProperty<ThemeName> ThemeProperty =
        AvaloniaProperty.Register<TextMateBehavior, ThemeName>(nameof(Theme), ThemeName.Dark);

    private TextMate.Installation? _installation;

    public string? GrammarExtension
    {
        get => GetValue(GrammarExtensionProperty);
        set => SetValue(GrammarExtensionProperty, value);
    }

    public ThemeName Theme
    {
        get => GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (AssociatedObject is null)
        {
            return;
        }

        var registryOptions = new RegistryOptions(Theme);
        _installation = AssociatedObject.InstallTextMate(registryOptions);

        var extension = GrammarExtension ?? ".txt";
        var scope = registryOptions.GetScopeByExtension(extension) ?? registryOptions.GetScopeByExtension(".txt");
        if (!string.IsNullOrWhiteSpace(scope))
        {
            _installation.SetGrammar(scope);
        }
    }

    protected override void OnDetaching()
    {
        _installation?.Dispose();
        _installation = null;
        base.OnDetaching();
    }
}
