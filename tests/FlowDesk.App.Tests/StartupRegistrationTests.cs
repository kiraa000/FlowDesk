using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

// The Run value must be one quoted path so a sign-in start works from any install folder.
// These tests only read the pure Command property; they never open or change the registry.
public sealed class StartupRegistrationTests
{
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
