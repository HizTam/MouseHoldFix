using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MouseHoldFix.Localization;

namespace MouseHoldFix.Diagnostics;

internal static class LogFolderRecycler
{
    internal static void Recycle(string directory)
    {
        Validate(directory);
        if (!Environment.UserInteractive || new DriveInfo(Path.GetPathRoot(directory)!).DriveType != DriveType.Fixed)
            throw new IOException(Texts.Get("RecyclingUnavailable"));
        Exception? failure = null;
        // IFileOperation requires STA; the log writer and input hook remain separate.
        var thread = new Thread(() =>
        {
            try { RecycleOnShellThread(directory); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true, Name = "MouseHoldFix log recycling" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RecycleOnShellThread(string directory)
    {
        IShellItem? item = null;
        IFileOperation? operation = null;
        try
        {
            Validate(directory);
            var itemId = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(directory, 0, ref itemId, out item));
            operation = (IFileOperation)new FileOperation();
            // RECYCLEONDELETE requests recycling instead of permanent deletion.
            // Suppress Shell UI; errors and aborted operations are reported in Diagnostics.
            const uint flags = 0x00080000 | 0x20000000 | 0x00100000 | 0x00000400 | 0x00000010 | 0x00000004;
            operation.SetOperationFlags(flags);
            operation.DeleteItem(item, 0);
            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);
            if (aborted || Directory.Exists(directory)) throw new IOException(Texts.Get("RecyclingUnavailable"));
        }
        finally
        {
            if (operation is not null) Marshal.FinalReleaseComObject(operation);
            if (item is not null) Marshal.FinalReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid interfaceId, out IShellItem item);

    [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
    private class FileOperation { }

    // Only the interface identity is needed to pass the item; no IShellItem method is invoked.
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem { }

    // Keep the complete native vtable order, including operations this app never invokes.
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void MoveItems(nint items, IShellItem destination);
        void CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string name, nint sink);
        void CopyItems(nint items, IShellItem destination);
        void DeleteItem(IShellItem item, nint sink);
        void DeleteItems(nint items);
        void NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    internal static void Validate(string directory)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (!Path.GetFileName(target).Equals("Logs", StringComparison.Ordinal))
            throw new IOException(Texts.Get("InvalidLogFolder"));
        // Validate from the volume root down, before accessing any linked descendant.
        var ancestors = new Stack<string>();
        for (var ancestor = new DirectoryInfo(target); ancestor is not null; ancestor = ancestor.Parent)
            ancestors.Push(ancestor.FullName);
        while (ancestors.TryPop(out var ancestorPath)) RejectLink(ancestorPath);
        // Traverse one directory at a time so that directory links are never followed.
        var pending = new Stack<string>();
        pending.Push(target);
        while (pending.TryPop(out var current))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var attributes = RejectLink(entry);
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }
    }

    private static FileAttributes RejectLink(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException(Texts.Get("LinkedLogFolder"));
        return attributes;
    }
}
