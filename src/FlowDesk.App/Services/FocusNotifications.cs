using System.Security;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Windows;
using Microsoft.Windows.AppNotifications;

namespace FlowDesk.App.Services;

// Random one-session tokens, never executable paths or window handles in activation arguments.
public sealed class FocusNotifications : IDisposable
{
    private readonly AppNotificationManager manager = AppNotificationManager.Default;
    private readonly Action<Action> enqueue;
    private readonly Action<string> error;
    private readonly Func<WindowInfo, Task> allow;
    private readonly Action activate;
    private readonly Dictionary<string, WindowInfo> allowances = [];
    private bool registered;
#if DEBUG
    internal Action<string, uint>? DiagnosticNotification { get; set; }
#endif
    public FocusNotifications(Action<Action> enqueue, Action<string> error, Func<WindowInfo, Task> allow, Action activate)
    { this.enqueue = enqueue; this.error = error; this.allow = allow; this.activate = activate; }
    public void Initialize()
    {
        try { manager.NotificationInvoked += Invoked; manager.Register(); registered = true; }
        catch (Exception) { error("Windows notifications are unavailable. Focus reminders still appear in FlowDesk."); }
    }
    public void ClearSession() => allowances.Clear();
    public void Blocked(AppBlockedEvent e)
    {
        string message = e.Action == BlockAction.Warned ? $"{e.AppName} is on your blocked list" : $"{e.AppName} was closed for this focus session";
        string actions = "";
        if (e.CanAllowTemporarily)
        {
            // Bound storage for repeated launches during a long session.
            if (allowances.Count >= 128) allowances.Remove(allowances.Keys.First());
            var token = Guid.NewGuid().ToString("N"); allowances[token] = e.Window;
            actions = $"<actions><action content=\"Allow 5 minutes\" arguments=\"allow={token}\" activationType=\"foreground\"/></actions>";
        }
        Show(message, actions);
    }
    public void Complete() { ClearSession(); Show("Focus session complete"); }

    /// <summary>Short app notification for non-focus state changes (window shortcuts pause toggle).</summary>
    public void ShowMessage(string message) => Show(message);
    private void Show(string message, string actions = "")
    {
        if (!registered) return;
        try
        {
            var xml = $"<toast duration=\"long\"><visual><binding template=\"ToastGeneric\"><text>FlowDesk</text><text>{SecurityElement.Escape(message)}</text></binding></visual>{actions}</toast>";
            var notification = new AppNotification(xml);
            manager.Show(notification);
#if DEBUG
            DiagnosticNotification?.Invoke(xml, notification.Id);
#endif
        }
        catch (Exception) { error("Windows could not show the notification. Open FlowDesk for focus status."); }
    }
    private void Invoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var argument = args.Argument;
        enqueue(async () => await HandleActivationAsync(argument));
    }
    // Shared by real Windows activation and the opt-in Debug verification harness.
    internal async Task HandleActivationAsync(string argument)
    {
        activate();
        if (argument.StartsWith("allow=", StringComparison.Ordinal) && allowances.Remove(argument[6..], out var window))
            await allow(window);
    }
    public void Dispose()
    {
        manager.NotificationInvoked -= Invoked;
        if (registered) manager.Unregister();
        allowances.Clear();
    }
}
