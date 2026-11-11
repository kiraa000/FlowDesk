#if DEBUG
using System.Text.Json;
using FlowDesk.Core.Model;
using Microsoft.Windows.AppNotifications;

namespace FlowDesk.App.Services;

// Explicit, local developer opt-in only. Not compiled into Release. No production IPC surface.
// Commands cannot supply paths/rules/executables: the only test session is a one-minute notepad rule.
internal sealed class Task002Diagnostics
{
    internal static bool Enabled => Environment.GetEnvironmentVariable("FLOWDESK_TASK002_DIAGNOSTICS") == "1";
    private readonly string folder;
    private readonly AppServices services;
    private readonly FocusNotifications notifications;
    private readonly Func<Task> refresh;
    private string lastRequest = "";
    private readonly List<NotificationEvidence> evidence = [];
    private bool busy;
    private bool initialPoll = true;
    private sealed record Request(string Id, string Action, string? Argument);
    private sealed record NotificationEvidence(string Payload, uint Id);
    public Task002Diagnostics(AppServices services, FocusNotifications notifications, Func<Task> refresh)
    {
        this.services = services; this.notifications = notifications; this.refresh = refresh;
        folder = Path.Combine(services.Store.Folder, "task002-debug");
        notifications.DiagnosticNotification = (payload, id) =>
        {
            if (evidence.Count >= 32) evidence.RemoveAt(0);
            evidence.Add(new(payload, id));
        };
    }
    public async Task PollAsync()
    {
        if (busy) return;
        busy = true;
        try
        {
            var request = await Task.Run(async () =>
            {
                var file = Path.Combine(folder, "request.json");
                if (!File.Exists(file)) return null;
                try { return JsonSerializer.Deserialize<Request>(await File.ReadAllTextAsync(file)); }
                catch (JsonException) { return null; } // Writer may be between opening and finishing.
            });
            // Never replay a command left on disk by a previous diagnostics process.
            if (initialPoll) { initialPoll = false; lastRequest = request?.Id ?? ""; return; }
            if (request is null || request.Id == lastRequest) return;
            lastRequest = request.Id;
            string? error = null;
            try
            {
                switch (request.Action)
                {
                    case "StartShortNormal":
                    case "StartShortStrict":
                        if (await Task.Run(() => services.Focus.State is not null)) throw new InvalidOperationException("End the active session first.");
                        notifications.ClearSession(); evidence.Clear();
                        var settings = new FocusSettings { DurationMinutes = 1,
                            Strictness = request.Action == "StartShortNormal" ? FocusStrictness.Normal : FocusStrictness.Strict,
                            Blocked = [new() { Name = "Notepad", ExeName = "notepad" }] };
                        await Task.Run(() => services.Focus.Start(settings));
                        break;
                    case "Activate":
                        if (request.Argument is not { Length: <= 64 } argument || !argument.StartsWith("allow=", StringComparison.Ordinal))
                            throw new InvalidOperationException("Expected a logged temporary-allowance argument.");
                        await notifications.HandleActivationAsync(argument);
                        break;
                    case "Snapshot": break;
                    case "ClearNotifications": await AppNotificationManager.Default.RemoveAllAsync(); evidence.Clear(); break;
                    default: throw new InvalidOperationException("Unknown diagnostic command.");
                }
                await refresh();
            }
            catch (Exception ex) { error = ex.Message; }
            var state = await Task.Run(() => services.Focus.State);
            var remaining = await Task.Run(() => services.Focus.Remaining.TotalSeconds);
            uint[] platformIds = [];
            try { platformIds = (await AppNotificationManager.Default.GetAllAsync()).Select(n => n.Id).ToArray(); }
            catch (Exception ex) { error ??= "Notification platform query failed: " + ex.Message; }
            var response = JsonSerializer.Serialize(new { request.Id, Error = error, Running = state is not null,
                Strictness = state?.Strictness.ToString(), Paused = state?.Paused, Remaining = remaining,
                Notifications = evidence.ToArray(), PlatformNotificationIds = platformIds });
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(folder);
                var temporary = Path.Combine(folder, "response.tmp");
                await File.WriteAllTextAsync(temporary, response);
                File.Move(temporary, Path.Combine(folder, "response.json"), overwrite: true);
            });
        }
        catch (IOException) { /* Next timer tick retries; never interfere with focus enforcement. */ }
        catch (UnauthorizedAccessException) { }
        finally { busy = false; }
    }
}
#endif
