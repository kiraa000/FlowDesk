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
    private bool subscribed;
    internal const string TrayNotice = "FlowDesk is still running in the tray. Use Exit in the tray menu to close it.";
    internal bool Supported { get; private set; }
    internal bool Registered => registered;
    internal string? RegistrationFailure { get; private set; }
    internal string? LastFailure { get; private set; }
    internal uint LastNotificationId { get; private set; }
    internal string? LastSetting { get; private set; }
#if DEBUG
    internal Action<string, uint>? DiagnosticNotification { get; set; }
#endif
    public FocusNotifications(Action<Action> enqueue, Action<string> error, Func<WindowInfo, Task> allow, Action activate)
    { this.enqueue = enqueue; this.error = error; this.allow = allow; this.activate = activate; }
    public void Initialize()
    {
        if (registered) return;
        try
        {
            Supported = AppNotificationManager.IsSupported();
            if (!Supported) { RegistrationFailure = "Unsupported"; error("Windows notifications are unavailable. Focus reminders still appear in FlowDesk."); return; }
            if (!subscribed) { manager.NotificationInvoked += Invoked; subscribed = true; }
            manager.Register(); registered = true; RegistrationFailure = null;
        }
        catch (Exception ex)
        {
            // Keep only type/HRESULT, never machine paths or exception messages in diagnostic evidence.
            RegistrationFailure = Failure(ex);
            error("Windows notifications are unavailable. Focus reminders still appear in FlowDesk.");
        }
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
    public bool ShowMessage(string message) => Show(message);
    private bool Show(string message, string actions = "")
    {
        LastNotificationId = 0; LastFailure = null; LastSetting = null;
        if (!registered) { LastFailure = RegistrationFailure ?? "NotRegistered"; return false; }
        try
        {
            var setting = manager.Setting;
            LastSetting = setting.ToString();
            if (setting != AppNotificationSetting.Enabled) { LastFailure = "NotificationsNotEnabled"; return false; }
            var xml = $"<toast duration=\"long\"><visual><binding template=\"ToastGeneric\"><text>FlowDesk</text><text>{SecurityElement.Escape(message)}</text></binding></visual>{actions}</toast>";
            var notification = new AppNotification(xml);
            manager.Show(notification);
            LastNotificationId = notification.Id;
#if DEBUG
            DiagnosticNotification?.Invoke(xml, notification.Id);
#endif
            // Register/Show can silently no-op in unsupported contexts (notably elevation).
            if (notification.Id == 0) { LastFailure = "NoPlatformId"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            LastFailure = Failure(ex);
            error("Windows could not show the notification. Open FlowDesk for focus status.");
            return false;
        }
    }
    private static string Failure(Exception ex) => $"{ex.GetType().Name} (0x{ex.HResult:X8})";
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
        if (subscribed) manager.NotificationInvoked -= Invoked;
        subscribed = false;
        try { if (registered) manager.Unregister(); }
        catch (Exception) { /* Notification cleanup must not prevent the rest of application teardown. */ }
        registered = false;
        allowances.Clear();
    }
}
