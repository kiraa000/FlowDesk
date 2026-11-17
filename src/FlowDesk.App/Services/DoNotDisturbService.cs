using System.Runtime.InteropServices;
using FlowDesk.Core.Contexts;

namespace FlowDesk.App.Services;

/// <summary>Undocumented QuietHours contract; no registry/WNF fallback. Read-only until SetAsync.</summary>
public sealed class DoNotDisturbService : IDoNotDisturb
{
    private const string Off = "Microsoft.QuietHoursProfile.Unrestricted";
    private const string On = "Microsoft.QuietHoursProfile.PriorityOnly";
    private readonly ContextNativeCall calls;
    private readonly object probeGate = new();
    private Task<bool>? probe;
    private readonly Action<Exception> log;
    public DoNotDisturbService(Action<Exception>? log = null)
    {
        this.log = log ?? (_ => { }); calls = new(this.log);
    }
    private Task<bool> ProbeAsync() { lock (probeGate) return probe ??= ProbeCoreAsync(); }
    private async Task<bool> ProbeCoreAsync()
    {
        try
        {
            await calls.RunAsync(token => WithSettings(s =>
            {
                Check(s.GetUserSelectedProfile(out var id));
                // Validate both profile identifiers against Windows, without selecting either one.
                foreach (string profileId in new[] { Off, On })
                {
                    token.ThrowIfCancellationRequested();
                    object? profile = null;
                    try { Check(s.GetProfile(profileId, out profile)); if (profile is null) throw new COMException("Missing QuietHours profile"); }
                    finally { ContextNativeCall.Release(profile); }
                }
                return id;
            }));
            return true;
        }
        catch (Exception ex) { log(ex); return false; }
    }
    public async Task<DoNotDisturbState> ReadAsync()
    {
        if (!await ProbeAsync().ConfigureAwait(false)) return new(false, null);
        try
        {
            string id = await calls.RunAsync(_ => WithSettings(s => { Check(s.GetUserSelectedProfile(out var p)); return p; })).ConfigureAwait(false);
            return new(true, id == Off ? false : id == On || id == "Microsoft.QuietHoursProfile.AlarmsOnly" ? true : null, id);
        }
        catch (Exception ex) { log(ex); return new(false, null); }
    }
    public async Task<bool> SetAsync(bool on)
    {
        if (!await ProbeAsync().ConfigureAwait(false)) return false;
        try
        {
            return await calls.RunAsync(token => WithSettings(s =>
            {
                string wanted = on ? On : Off;
                token.ThrowIfCancellationRequested(); Check(s.SetUserSelectedProfile(wanted));
                token.ThrowIfCancellationRequested(); Check(s.GetUserSelectedProfile(out var actual));
                if (actual != wanted) throw new COMException("QuietHours read-back mismatch", unchecked((int)0x80004005));
                return true;
            })).ConfigureAwait(false);
        }
        catch (Exception ex) { log(ex); return false; }
    }
    private static T WithSettings<T>(Func<IQuietHoursSettings, T> work)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new("F53321FA-34F8-4B7F-B9A3-361877CB94CF"), true)!);
            return work((IQuietHoursSettings)instance!);
        }
        finally { ContextNativeCall.Release(instance); }
    }
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    // Rivera's MIT IDL: IUnknown slots 0..2; UserSelectedProfile get=3, put=4.
    // GetProfile=5 validates the profile IDs without changing Windows. No later slots are used.
    [ComImport, Guid("6BFF4732-81EC-4FFB-AE67-B6C1BC29631F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQuietHoursSettings
    {
        [PreserveSig] int GetUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] out string profile);
        [PreserveSig] int SetUserSelectedProfile([MarshalAs(UnmanagedType.LPWStr)] string profile);
        [PreserveSig] int GetProfile([MarshalAs(UnmanagedType.LPWStr)] string profile, [MarshalAs(UnmanagedType.Interface)] out object? settings);
    }
}
