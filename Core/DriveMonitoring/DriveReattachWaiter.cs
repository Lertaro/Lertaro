namespace Lertaro.Core.DriveMonitoring;

internal static class DriveReattachWaiter
{
    public static void Start(string drive, CancellationToken token, Action onReattached) => _ = Task.Run(async () =>
                                                                                                 {
                                                                                                     while (!token.IsCancellationRequested)
                                                                                                     {
                                                                                                         try
                                                                                                         {
                                                                                                             await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                                                                                                             if (VolumeHelper.DetectIndexableLocalDrives().Contains(drive, StringComparer.OrdinalIgnoreCase))
                                                                                                             {
                                                                                                                 onReattached();
                                                                                                                 return;
                                                                                                             }
                                                                                                         }
                                                                                                         catch (OperationCanceledException)
                                                                                                         {
                                                                                                             return;
                                                                                                         }
                                                                                                         catch (Exception ex)
                                                                                                         {
                                                                                                             Logger.Log($"[DeviceNotification] Failed while waiting for drive {drive} to return: {ex.Message}", LogLevel.Warn);
                                                                                                         }
                                                                                                     }
                                                                                                 }, token);
}
