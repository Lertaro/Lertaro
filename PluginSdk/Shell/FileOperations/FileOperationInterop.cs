using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Shell.FileOperations;

// Minimal IFileOperation/IShellItem COM interop -- the same shell API Explorer itself uses for
// Delete, so a queued DeleteItem batch gets one native progress dialog and one native confirmation
// prompt (recycle-bin or permanent, depending on FOF_ALLOWUNDO) regardless of how many items are
// queued or which directories they came from.
internal static class FileOperationFlags
{
    public const uint FOF_ALLOWUNDO = 0x0040;
    public const uint FOF_WANTNUKEWARNING = 0x4000;
}

[ComImport]
[Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
internal class FileOperation
{
}

[ComImport]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFileOperation
{
    void Advise([MarshalAs(UnmanagedType.Interface)] object pfops, out uint pdwCookie);
    void Unadvise(uint dwCookie);
    void SetOperationFlags(uint dwOperationFlags);
    void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
    void SetProgressDialog(IntPtr popd);
    void SetProperties(IntPtr pproparray);
    void SetOwnerWindow(IntPtr hwndOwner);
    void ApplyPropertiesToItem(IShellItem psiItem);
    void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object punkItems);
    void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, [MarshalAs(UnmanagedType.Interface)] object? pfopsItem);
    void RenameItems([MarshalAs(UnmanagedType.Interface)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
    void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, [MarshalAs(UnmanagedType.Interface)] object? pfopsItem);
    void MoveItems([MarshalAs(UnmanagedType.Interface)] object punkItems, IShellItem psiDestinationFolder);
    void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, [MarshalAs(UnmanagedType.Interface)] object? pfopsItem);
    void CopyItems([MarshalAs(UnmanagedType.Interface)] object punkItems, IShellItem psiDestinationFolder);
    void DeleteItem(IShellItem psiItem, [MarshalAs(UnmanagedType.Interface)] object? pfopsItem);
    void DeleteItems([MarshalAs(UnmanagedType.Interface)] object punkItems);
    void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, [MarshalAs(UnmanagedType.Interface)] object? pfopsItem);
    void PerformOperations();
    [return: MarshalAs(UnmanagedType.Bool)]
    bool GetAnyOperationsAborted();
}

[ComImport]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(int sigdnName, out IntPtr ppszName);
    void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
    void Compare(IShellItem psi, uint hint, out int piOrder);
}

internal static class ShellItemInterop
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    public static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);
}
