using System.Runtime.InteropServices;
using FlowDesk.Core.Contexts;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

/// <summary>Documented MMDevice reads, guarded undocumented PolicyConfig writes. All RCWs stay on MTA.</summary>
public sealed class AudioOutputService : IAudioOutput
{
    private readonly ContextNativeCall calls;
    private readonly Action<Exception> log;
    private bool? canSet;
    public AudioOutputService(Action<Exception>? log = null) { this.log = log ?? (_ => { }); calls = new(this.log); }
    public async Task<AudioOutputState> ReadAsync()
    {
        try { return await calls.RunAsync(Read).ConfigureAwait(false); }
        catch (Exception ex) { log(ex); return new(false, [], null); }
    }
    private AudioOutputState Read(CancellationToken token)
    {
        object? policy = null; IMMDeviceEnumerator? enumerator = null; IMMDeviceCollection? collection = null;
        try
        {
            if (canSet is null)
            {
                try { policy = Create("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"); _ = (IPolicyConfig)policy; canSet = true; }
                catch (Exception ex) { log(ex); canSet = false; }
            }
            token.ThrowIfCancellationRequested();
            enumerator = (IMMDeviceEnumerator)Create("BCDE0395-E52F-467C-8E3D-C4579291692E");
            Check(enumerator.EnumAudioEndpoints(0, 1, out collection));
            Check(collection.GetCount(out var count));
            List<ContextAudioDevice> devices = [];
            for (uint i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                IMMDevice? device = null; IPropertyStore? properties = null;
                try
                {
                    Check(collection.Item(i, out device)); Check(device.GetId(out var id));
                    Check(device.OpenPropertyStore(0, out properties));
                    devices.Add(new() { EndpointId = id,
                        Name = Property(properties, new(new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14)) ?? "Sound output",
                        StableId = Property(properties, new(new("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), 12)) });
                }
                finally { ContextNativeCall.Release(properties); ContextNativeCall.Release(device); }
            }
            return new(canSet == true, devices, DefaultId(enumerator, 0));
        }
        finally { ContextNativeCall.Release(collection); ContextNativeCall.Release(enumerator); ContextNativeCall.Release(policy); }
    }
    public async Task<bool> SetDefaultAsync(string endpointId)
    {
        try
        {
            return await calls.RunAsync(token =>
            {
                object? policy = null; IMMDeviceEnumerator? enumerator = null;
                try
                {
                    if (canSet == false) return false;
                    policy = Create("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");
                    enumerator = (IMMDeviceEnumerator)Create("BCDE0395-E52F-467C-8E3D-C4579291692E");
                    for (int role = 0; role < 3; role++)
                    {
                        token.ThrowIfCancellationRequested();
                        Check(((IPolicyConfig)policy).SetDefaultEndpoint(endpointId, role));
                    }
                    for (int role = 0; role < 3; role++)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!string.Equals(DefaultId(enumerator, role), endpointId, StringComparison.OrdinalIgnoreCase))
                            throw new COMException("Audio read-back mismatch", unchecked((int)0x80004005));
                    }
                    return true;
                }
                finally { ContextNativeCall.Release(enumerator); ContextNativeCall.Release(policy); }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { log(ex); return false; }
    }
    private static object Create(string clsid) => Activator.CreateInstance(Type.GetTypeFromCLSID(new(clsid), true)!)!;
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static string? DefaultId(IMMDeviceEnumerator enumerator, int role)
    {
        IMMDevice? device = null;
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            if (hr == unchecked((int)0x80070490)) return null; // no active default
            Check(hr); Check(device.GetId(out var id)); return id;
        }
        finally { ContextNativeCall.Release(device); }
    }
    private static string? Property(IPropertyStore store, PropertyKey key)
    {
        PropVariant value = default;
        try { return store.GetValue(ref key, out value) >= 0 && value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey(Guid format, uint id) { public Guid Format = format; public uint Id = id; }
    // PROPVARIANT is 24 bytes on x64 (8-byte header + 16-byte union).
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
    { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    { [PreserveSig] int GetCount(out uint count); [PreserveSig] int Item(uint index, out IMMDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint parameters, out nint result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }
    // AudioDeviceCmdlets IPolicyConfig: ten methods before SetDefaultEndpoint (IUnknown slot 13).
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(nint id, nint format);
        [PreserveSig] int GetDeviceFormat(nint id, int defaults, nint format);
        [PreserveSig] int ResetDeviceFormat(nint id);
        [PreserveSig] int SetDeviceFormat(nint id, nint endpoint, nint mix);
        [PreserveSig] int GetProcessingPeriod(nint id, int defaults, nint period, nint minimum);
        [PreserveSig] int SetProcessingPeriod(nint id, nint period);
        [PreserveSig] int GetShareMode(nint id, nint mode);
        [PreserveSig] int SetShareMode(nint id, nint mode);
        [PreserveSig] int GetPropertyValue(nint id, int fx, nint key, nint value);
        [PreserveSig] int SetPropertyValue(nint id, int fx, nint key, nint value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}
