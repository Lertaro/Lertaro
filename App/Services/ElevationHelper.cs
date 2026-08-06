using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Lertaro.App.Services;

public static class ElevationHelper
{
    private const int TokenLinkedToken = 19;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass, ref IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Check if current process is running with administrative privileges or has a linked admin token.
    /// </summary>
    public static bool IsUserAdmin()
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                {
                    return true;
                }
            }

            // If not running as admin, try to check if there is an elevated linked token (UAC)
            var hProcess = Process.GetCurrentProcess().Handle;
            if (OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken))
            {
                try
                {
                    var hLinkedToken = IntPtr.Zero;
                    var success = GetTokenInformation(hToken, TokenLinkedToken, ref hLinkedToken, IntPtr.Size, out var returnLength);

                    if (success && hLinkedToken != IntPtr.Zero)
                    {
                        try
                        {
                            using var linkedIdentity = new WindowsIdentity(hLinkedToken);
                            var linkedPrincipal = new WindowsPrincipal(linkedIdentity);
                            return linkedPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
                        }
                        finally
                        {
                            CloseHandle(hLinkedToken);
                        }
                    }
                }
                finally
                {
                    CloseHandle(hToken);
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}
