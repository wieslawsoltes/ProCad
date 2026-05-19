using System;
using Avalonia;
using Avalonia.Controls;
using ProCad.Diagnostics;

namespace ProCad.Behaviors;

public sealed class DataGridFastPathDiagnostics
{
    public static readonly AttachedProperty<bool> UseDataContextProperty =
        AvaloniaProperty.RegisterAttached<DataGridFastPathDiagnostics, DataGrid, bool>("UseDataContext");

    private static readonly AttachedProperty<DataContextSubscription?> SubscriptionProperty =
        AvaloniaProperty.RegisterAttached<DataGridFastPathDiagnostics, DataGrid, DataContextSubscription?>("Subscription");

    static DataGridFastPathDiagnostics()
    {
        UseDataContextProperty.Changed.AddClassHandler<DataGrid>(OnUseDataContextChanged);
    }

    public static bool GetUseDataContext(AvaloniaObject element) => element.GetValue(UseDataContextProperty);

    public static void SetUseDataContext(AvaloniaObject element, bool value) => element.SetValue(UseDataContextProperty, value);

    private static void OnUseDataContextChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is bool enabled && enabled)
        {
            var subscription = new DataContextSubscription(grid);
            grid.SetValue(SubscriptionProperty, subscription);
            subscription.Attach();
        }
        else
        {
            var subscription = grid.GetValue(SubscriptionProperty);
            subscription?.Dispose();
            grid.ClearValue(SubscriptionProperty);
        }
    }

    private sealed class DataContextSubscription : IDisposable
    {
        private readonly DataGrid _grid;
        private FastPathSubscription? _fastPathSubscription;

        public DataContextSubscription(DataGrid grid)
        {
            _grid = grid;
        }

        public void Attach()
        {
            _grid.DataContextChanged += OnDataContextChanged;
            UpdateFromContext();
        }

        public void Dispose()
        {
            _grid.DataContextChanged -= OnDataContextChanged;
            _fastPathSubscription?.Dispose();
            _fastPathSubscription = null;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            UpdateFromContext();
        }

        private void UpdateFromContext()
        {
            if (_grid.DataContext is not IFastPathDiagnosticsSource source)
            {
                _fastPathSubscription?.Dispose();
                _fastPathSubscription = null;
                return;
            }

            var service = source.FastPathDiagnostics;
            if (_fastPathSubscription?.Service == service)
            {
                return;
            }

            _fastPathSubscription?.Dispose();
            _fastPathSubscription = FastPathSubscription.Create(_grid, service);
        }
    }

    private sealed class FastPathSubscription : IDisposable
    {
        private readonly DataGridFastPathOptions _options;
        private readonly EventHandler<DataGridFastPathMissingAccessorEventArgs> _handler;

        public FastPathDiagnosticsService Service { get; }

        private FastPathSubscription(
            DataGridFastPathOptions options,
            FastPathDiagnosticsService service,
            EventHandler<DataGridFastPathMissingAccessorEventArgs> handler)
        {
            _options = options;
            Service = service;
            _handler = handler;
        }

        public static FastPathSubscription Create(DataGrid grid, FastPathDiagnosticsService service)
        {
            var options = grid.FastPathOptions ?? new DataGridFastPathOptions();
            grid.FastPathOptions = options;

            EventHandler<DataGridFastPathMissingAccessorEventArgs> handler =
                (_, args) => service.ReportMissingAccessor(args, grid);

            options.MissingAccessor += handler;
            return new FastPathSubscription(options, service, handler);
        }

        public void Dispose()
        {
            _options.MissingAccessor -= _handler;
        }
    }
}
