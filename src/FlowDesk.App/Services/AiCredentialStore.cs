using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

internal interface IAiCredentialBackend
{
    byte[]? Read(string target);
    void Write(string target, byte[] value);
    void Delete(string target);
}
internal sealed class AiCredentialStore(IAiCredentialBackend? backend = null) : IAiCredentialStore
{
    private readonly IAiCredentialBackend backend = backend ?? new NativeAiCredentials();
    internal static string Target(AiProviderKind provider) => Enum.IsDefined(provider) ? "FlowDesk/ai/" + provider : throw new ArgumentException("Unknown AI provider.");
    public string? Read(AiProviderKind provider)
    {
        byte[]? bytes = backend.Read(Target(provider));
        if (bytes is null) return null;
        try
        {
            if ((bytes.Length & 1) != 0 || bytes.Length > 2560) throw new AiAnswerException("The saved key is invalid. Save it again.");
            return Encoding.Unicode.GetString(bytes);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(AiProviderKind provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1280 || key.Any(char.IsControl)) throw new AiAnswerException("Enter a valid API key (at most 1280 characters).");
        byte[] bytes = Encoding.Unicode.GetBytes(key);
        try { backend.Write(Target(provider), bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Remove(AiProviderKind provider) => backend.Delete(Target(provider));
}
internal sealed class NativeAiCredentials : IAiCredentialBackend
{
    public byte[]? Read(string target)
    {
        if (!CredReadW(target, 1, 0, out nint pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new AiAnswerException("Windows could not read the saved key.");
        }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            if (credential.Size > 2560 || (credential.Size > 0 && credential.Blob == 0)) throw new AiAnswerException("The saved key is invalid.");
            byte[] bytes = new byte[credential.Size];
            if (bytes.Length > 0) Marshal.Copy(credential.Blob, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { CredFree(pointer); }
    }
    public void Write(string target, byte[] value)
    {
        nint blob = Marshal.AllocHGlobal(value.Length);
        try
        {
            Marshal.Copy(value, 0, blob, value.Length);
            var credential = new Credential { Type = 1, Target = target, Size = (uint)value.Length, Blob = blob, Persist = 2, UserName = "FlowDesk" };
            if (!CredWriteW(ref credential, 0)) throw new AiAnswerException("Windows could not save the API key.");
        }
        finally { for (int i = 0; i < value.Length; i++) Marshal.WriteByte(blob, i, 0); Marshal.FreeHGlobal(blob); }
    }
    public void Delete(string target)
    { if (!CredDeleteW(target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new AiAnswerException("Windows could not remove the saved key."); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Credential
    {
        public uint Flags, Type; public string Target; public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint Size; public nint Blob; public uint Persist, AttributeCount; public nint Attributes;
        public string? Alias; public string? UserName;
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredReadW(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint credential);
}
