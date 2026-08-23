using System.Runtime.CompilerServices;

namespace Lertaro.Plugins.FlowLauncherBridge.Tests;

/// <summary>
/// WPF FrameworkElement construction throws unless the calling thread is STA.
/// Runs test method on a dedicated STA thread.
/// </summary>
public sealed class StaTestMethodAttribute(
    [CallerFilePath] string callerFilePath = "",
    [CallerLineNumber] int callerLineNumber = 0)
    : TestMethodAttribute(callerFilePath, callerLineNumber)
{
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            return await base.ExecuteAsync(testMethod);

        TestResult[]? results = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { results = base.ExecuteAsync(testMethod).GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null) throw error;
        return results!;
    }
}
