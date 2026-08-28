using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class AppSingleInstanceTests
{
    [TestMethod]
    public void AcquireMutex_TakesOwnershipOfAbandonedMutex()
    {
        var mutexName = $@"Local\Lertaro.Tests.{Guid.NewGuid():N}";
        Mutex? abandonedMutex = null;
        Exception? workerException = null;

        var ownerThread = new Thread(() =>
        {
            try
            {
                abandonedMutex = new Mutex(true, mutexName, out var createdNew);
                if (!createdNew)
                    throw new InvalidOperationException("The test mutex was unexpectedly reused.");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
        });
        ownerThread.Start();
        ownerThread.Join();

        try
        {
            Assert.IsNull(workerException, workerException?.ToString());
            Assert.IsNotNull(abandonedMutex);

            using var acquiredMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);

            Assert.IsTrue(createdNew, "An abandoned mutex should be treated as owned by this instance.");
            acquiredMutex.ReleaseMutex();
        }
        finally
        {
            abandonedMutex?.Dispose();
        }
    }

    [TestMethod]
    public void AcquireMutex_DoesNotClaimMutexHeldByLiveInstance()
    {
        var mutexName = $@"Local\Lertaro.Tests.{Guid.NewGuid():N}";
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Mutex? liveMutex = null;
        Exception? workerException = null;
        var ownerThread = new Thread(() =>
        {
            try
            {
                liveMutex = new Mutex(true, mutexName, out var createdNew);
                if (!createdNew)
                    throw new InvalidOperationException("The test mutex was unexpectedly reused.");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
            finally
            {
                ready.Set();
            }

            release.Wait();
            if (workerException == null)
                liveMutex!.ReleaseMutex();
        });
        ownerThread.Start();
        ready.Wait();

        try
        {
            Assert.IsNull(workerException, workerException?.ToString());
            using var secondMutex = AppSingleInstance.AcquireMutex(mutexName, out var createdNew);

            Assert.IsFalse(createdNew, "A live instance must remain the sole mutex owner.");
        }
        finally
        {
            release.Set();
            ownerThread.Join();
            liveMutex?.Dispose();
        }
    }
}
