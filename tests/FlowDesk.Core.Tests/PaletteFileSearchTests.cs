using System.Buffers.Binary;
using System.Text;
using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteFileSearchTests
{
    [Theory]
    [InlineData("f ")]
    [InlineData("F ")]
    public void FilesPrefixIsListedAndParsed(string text) => Assert.Equal(PaletteMode.Files, PalettePrefixes.Match(text + "notes")?.Mode);
    [Fact] public void EverythingQueryUsesDwordWindowHandleAndBoundedUnicodeLiteral()
    {
        var bytes = EverythingIpcWire.Query(0xABCDEF12, 77, "hello | file:", 9999);
        Assert.Equal(0xABCDEF12, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
        Assert.Equal(77u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
        Assert.Equal(80u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)));
        Assert.StartsWith("\"hello | file:\"\0", Encoding.Unicode.GetString(bytes, 20, bytes.Length - 20));
        Assert.Equal("\"a b\"", FileSearchQuery.Everything("a\"b"));
    }
    [Fact] public void WindowsSearchScopesProfileLimitsRowsAndEscapesSqlAndWildcardInput()
    {
        string sql = FileSearchQuery.WindowsSearch("Bob's 100%_ [notes]", @"D:\Users\Bob", 1000);
        Assert.Contains("SELECT TOP 80", sql); Assert.Contains("SCOPE = 'file:///D:/Users/Bob/'", sql);
        Assert.Contains("Bob''s 100[%][_] [[]notes]", sql);
    }
    [Fact] public void ReplyMapsFilesAndFoldersFromCheckedRelativeOffsets()
    {
        byte[] data = Reply();
        var file = Assert.Single(EverythingIpcWire.Results(data, 10));
        Assert.Equal(@"D:\Docs\note.txt", file.Path); Assert.False(file.IsFolder);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
        Assert.True(Assert.Single(EverythingIpcWire.Results(data, 1)).IsFolder);
    }
    [Fact] public void WindowsSearchUsesInvariantDirectoryAttributeNotLocalizedTypeNames()
    {
        Assert.True(PaletteFile.FromWindowsSearch(@"D:\Docs", 0x11).IsFolder);
        Assert.False(PaletteFile.FromWindowsSearch(@"D:\note.txt", 0x20).IsFolder);
    }
    [Fact] public void InvalidOffsetsCountsAndUnterminatedStringsAreRejected()
    {
        Assert.Throws<FormatException>(() => EverythingIpcWire.Results([], 10));
        var data = Reply(); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1000);
        Assert.Throws<FormatException>(() => EverythingIpcWire.Results(data, 10));
        data = Reply(); BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), uint.MaxValue);
        Assert.Throws<FormatException>(() => EverythingIpcWire.Results(data, 10));
        data = Reply(); data[^1] = 1; data[^2] = 1;
        Assert.Throws<FormatException>(() => EverythingIpcWire.Results(data, 10));
    }
    [Theory]
    [InlineData("tool.EXE", true)] [InlineData("tool.cmd", true)] [InlineData("tool.bat", true)]
    [InlineData("notes.txt", false)] [InlineData("script.ps1", false)] [InlineData("fake.exe.txt", false)]
    public void ElevationIsRestrictedToExecutableExtensions(string path, bool expected) => Assert.Equal(expected, FileSearchQuery.CanElevate(path));
    [Fact] public void MiddleEllipsisPreservesBothEndsOfLongPaths()
    {
        string path = "D:\\" + new string('a', 100) + "\\Documents";
        string shortPath = FileSearchQuery.MiddleEllipsis(path);
        Assert.Equal(76, shortPath.Length); Assert.StartsWith("D:\\", shortPath); Assert.EndsWith("Documents", shortPath); Assert.Contains("…", shortPath);
    }
    private static byte[] Reply()
    {
        byte[] name = Encoding.Unicode.GetBytes("note.txt\0"), folder = Encoding.Unicode.GetBytes("D:\\Docs\0");
        byte[] data = new byte[40 + name.Length + folder.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(32), 40);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(36), (uint)(40 + name.Length));
        name.CopyTo(data, 40); folder.CopyTo(data, 40 + name.Length); return data;
    }
}
