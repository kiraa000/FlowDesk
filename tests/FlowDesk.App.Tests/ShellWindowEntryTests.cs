using System.Runtime.InteropServices;
using FlowDesk.App.Services;
using Microsoft.CSharp.RuntimeBinder;

namespace FlowDesk.App.Tests;

public sealed class ShellWindowEntryTests
{
    public sealed class Browser(long hwnd) { public long HWND => hwnd; public object? Document => null; }
    public sealed class BrokenBrowser(Exception error)
    {
        public long HWND => throw error;
        public object Document => throw error;
    }

    [Fact] public void NullAndMissingMemberEntriesAreSkippedBeforeHealthyOwner()
    {
        Func<object?>[] entries = [() => null, () => throw new RuntimeBinderException("stale Item index"),
            () => new object(), () => new BrokenBrowser(new RuntimeBinderException("stale HWND")), () => new Browser(42)];
        var owners = new List<nint>();
        foreach (var entry in entries)
            if (ShellWindowEntry.TryGetOwner(ShellWindowEntry.Read(entry), out var owner)) owners.Add(owner);
        Assert.Equal(new nint[] { 42 }, owners);
        Assert.False(ShellWindowEntry.TryGetOwner(new Browser(0), out _));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void StaleDispatchFailuresDoNotAbortEnumerationOrDocumentResolution(int kind)
    {
        Exception error = kind switch
        {
            0 => new RuntimeBinderException("missing dispatch member"),
            1 => new COMException("window closed"),
            2 => new InvalidComObjectException("released RCW"),
            _ => new InvalidCastException("unsupported dispatch interface")
        };
        Assert.False(ShellWindowEntry.TryGetOwner(new BrokenBrowser(error), out _));
        Assert.Null(ShellWindowEntry.Read(() => throw error)); // windows.Item / individual Document
        object stale = new BrokenBrowser(error);
        Assert.Null(ShellWindowEntry.Read(() => (object?)((dynamic)stale).Document));
    }

    [Fact] public void NullDesktopOrDocumentIsNotReplacedWithAnotherWindow()
    {
        Assert.Null(ShellWindowEntry.Read(() => null));
        object browser = new Browser(10);
        Assert.Null(ShellWindowEntry.Read(() => (object?)((dynamic)browser).Document));
    }

    [Fact] public void UnrelatedErrorsAndDeadlineExpiryStillPropagate()
    {
        Assert.Throws<TimeoutException>(() => ShellWindowEntry.Read(() => throw new TimeoutException()));
        Assert.Throws<InvalidOperationException>(() => ShellWindowEntry.TryGetOwner(new BrokenBrowser(new InvalidOperationException()), out _));
    }
}
