using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace FlowDesk.App;
public partial class App : Application
{
    public static ViewModels.AppViewModels ViewModels { get; internal set; } = null!;
    private MainWindow? window;
    private AppInstance? instance;
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        instance = AppInstance.FindOrRegisterForKey("FlowDesk");
        if (!instance.IsCurrent)
        {
            await instance.RedirectActivationToAsync(AppInstance.GetCurrent().GetActivatedEventArgs());
            Exit(); return;
        }
        window = new MainWindow();
        instance.Activated += (_, _) => window.DispatcherQueue.TryEnqueue(() => window.BringForward());
        window.Activate(); await window.InitializeAsync();
    }
}
