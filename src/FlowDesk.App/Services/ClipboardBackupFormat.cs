namespace FlowDesk.App.Services;

internal static class ClipboardBackupFormat
{
    // OLE only special-cases these three GDI formats. Display equivalents need the same
    // duplication semantics; enhanced metafiles use CopyEnhMetaFile rather than OLE.
    internal static uint OleFormat(uint format) => format switch { 0x82 => 2, 0x83 => 3, _ => format };
    internal static bool EnhancedMetafile(uint format) => format is 14 or 0x8E;
}
