using FlowDesk.App.Services;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.Tests;

// The Run value must be one quoted path so a sign-in start works from any install folder.
// These tests only read the pure Command property; they never open or change the registry.
public sealed class StartupRegistrationTests
{
    [Fact]
    public void CalmDisabledStartupIsReflectedAndReenablePreservesTimestamp()
    {
        var registry = new FakeRegistry();
        var run = new RegistryAddress(StartupCatalog.Run, "FlowDesk");
        var approval = new RegistryAddress(StartupCatalog.Approved + "Run", "FlowDesk");
        registry.Write(run, new(1, "\"FlowDesk.exe\""));
        Assert.True(StartupRegistration.IsEnabled(registry));
        byte[] bytes = [3, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8];
        registry.Write(approval, RegistryValue.Binary(bytes));
        Assert.False(StartupRegistration.IsEnabled(registry));
        Assert.True(StartupRegistration.SetEnabled(true, registry, "FlowDesk.exe"));
        Assert.True(StartupRegistration.IsEnabled(registry));
        Assert.Equal(bytes[4..], registry.Read(approval)!.AsBinary()![4..]);
        Assert.True(StartupRegistration.SetEnabled(false, registry, "FlowDesk.exe"));
        Assert.Null(registry.Read(run));
    }
    private sealed class FakeRegistry : IRegistryAccess
    {
        private readonly Dictionary<RegistryAddress, RegistryValue> values = [];
        public RegistryValue? Read(RegistryAddress address) => values.GetValueOrDefault(address);
        public void Write(RegistryAddress address, RegistryValue value) => values[address] = value;
        public void Delete(RegistryAddress address) => values.Remove(address);
        public IReadOnlyList<string> ValueNames(string path) => values.Keys.Where(k => k.Path == path).Select(k => k.Name).ToArray();
    }
    [Fact]
    public void Command_IsTheQuotedRunningExecutable()
    {
        string expected = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "FlowDesk.App.exe");

        Assert.Equal($"\"{expected}\"", StartupRegistration.Command);
    }

    [Fact]
    public void Command_IsOneQuotedArgument()
    {
        string command = StartupRegistration.Command;

        Assert.StartsWith("\"", command);
        Assert.EndsWith("\"", command);
        Assert.Equal(2, command.Count(c => c == '"'));
    }
}
