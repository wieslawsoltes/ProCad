using System;
using Avalonia.Controls.Notifications;

namespace ProCad.Services;

public interface IAppNotificationService
{
    void SetManager(INotificationManager? manager);

    void Show(
        string title,
        string message,
        NotificationType type = NotificationType.Information,
        TimeSpan? expiration = null);

    void ShowError(string title, string message, TimeSpan? expiration = null);

    void ShowWarning(string title, string message, TimeSpan? expiration = null);
}
