using System.ComponentModel;
using System.Diagnostics;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

internal static class CustomCommandExecutor
{
    public static Task RunAsync(CustomCommandLaunch command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(command.Target, command.Arguments)
            { UseShellExecute = true, Verb = command.RunAsAdmin ? "runas" : "", WindowStyle = ProcessWindowStyle.Normal });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        return Task.CompletedTask;
    }
}
