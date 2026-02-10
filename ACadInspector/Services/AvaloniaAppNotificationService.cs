using System;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using ACadInspector.Diagnostics;

namespace ACadInspector.Services;

public sealed class AvaloniaAppNotificationService : IAppNotificationService
{
    private readonly object _sync = new();
    private INotificationManager? _manager;

    public void SetManager(INotificationManager? manager)
    {
        lock (_sync)
        {
            _manager = manager;
        }
    }

    public void Show(
        string title,
        string message,
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null)
    {
        var manager = GetManager();
        if (manager is null)
        {
            AppLog.Warn($"Notification skipped (manager unavailable): [{type}] {title}: {message}", category: nameof(AvaloniaAppNotificationService));
            return;
        }

        var notification = new Notification(
            title,
            message,
            type,
            expiration ?? TimeSpan.FromSeconds(6));

        if (Dispatcher.UIThread.CheckAccess())
        {
            manager.Show(notification);
            return;
        }

        Dispatcher.UIThread.Post(() => manager.Show(notification));
    }

    public void ShowError(string title, string message, TimeSpan? expiration = null)
    {
        Show(title, message, NotificationType.Error, expiration);
    }

    public void ShowWarning(string title, string message, TimeSpan? expiration = null)
    {
        Show(title, message, NotificationType.Warning, expiration);
    }

    private INotificationManager? GetManager()
    {
        lock (_sync)
        {
            return _manager;
        }
    }
}
