using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Dispatching;
using FlowDesk.App.Services;
using System.Runtime.InteropServices;

namespace FlowDesk.App;
public partial class App : Application
{
    public static ViewModels.AppViewModels ViewModels { get; internal set; } = null!;
    private MainWindow? window;
    private AppInstance? instance;
    private ActivationRouter? activation;
    private readonly CrashLog crashLog = new();

    public App()
    {
        // Install before XAML initialization. Logging never marks an unhandled exception as handled
        // and never writes its Message/Data (which can name other apps, documents or credentials).
        UnhandledException += (_, e) => crashLog.TryWrite(CrashSource.UiThread, e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => crashLog.TryWrite(CrashSource.UnobservedTask, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            crashLog.TryWrite(CrashSource.AppDomain, e.ExceptionObject as Exception, e.IsTerminating);
        InitializeComponent();
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            activation = new ActivationRouter(action => dispatcher.TryEnqueue(() => action()));
            instance = AppInstance.GetCurrent();
            // Subscribe before registering the key: another process can redirect immediately after
            // FindOrRegister returns, before our MainWindow exists. The router retains that request.
            instance.Activated += Instance_Activated;
            var owner = AppInstance.FindOrRegisterForKey("FlowDesk");
            if (!owner.IsCurrent)
            {
                instance.Activated -= Instance_Activated;
                activation.Dispose();
                try
                {
                    // The user-started secondary owns foreground permission; transfer it before
                    // the background primary reveals/restores its (possibly tray-hidden) window.
                    AllowSetForegroundWindow((uint)owner.ProcessId);
                    var activationArgs = instance.GetActivatedEventArgs()
                        ?? throw new InvalidOperationException("Unsupported activation kind");
                    await owner.RedirectActivationToAsync(activationArgs);
                }
                catch (Exception ex) { crashLog.TryWrite(CrashSource.Activation, ex); }
                finally { Exit(); }
                return; // never create a duplicate window or initialize app services in a secondary process
            }
            window = new MainWindow();
            window.Closed += (_, _) =>
            {
                activation.Dispose();
                instance.Activated -= Instance_Activated;
                window = null;
            };
            activation.SetTarget(window.BringForward); // Show + Restore + Activate, including a tray-hidden window
            window.Activate();
            await window.InitializeAsync();
        }
        catch (Exception ex)
        {
            crashLog.TryWrite(CrashSource.Launch, ex, terminating: true);
            activation?.Dispose();
            if (instance is not null) instance.Activated -= Instance_Activated;
            Exit();
        }
    }

    private void Instance_Activated(object? sender, AppActivationArguments args)
    {
        try { activation?.Request(); }
        catch (Exception ex) { crashLog.TryWrite(CrashSource.Activation, ex); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
