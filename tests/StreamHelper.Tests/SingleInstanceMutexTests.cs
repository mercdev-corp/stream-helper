using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
public sealed class SingleInstanceMutexTests
{
    [TestMethod]
    public void ComputePathHash_DeterministicAndCaseInsensitive()
    {
        var path1 = @"C:\Games\StreamHelper";
        var path2 = @"c:\games\StreamHelper\";

        var hash1 = SingleInstanceMutex.ComputePathHash(path1);
        var hash2 = SingleInstanceMutex.ComputePathHash(path2);

        Assert.AreEqual(hash1, hash2);
        Assert.AreEqual(16, hash1.Length);
    }

    [TestMethod]
    public void TryAcquire_SameFolder_BlocksSecondInstance()
    {
        var tempFolder = TestDirectory.Create("StreamHelperTest1");

        try
        {
            using var instance1 = SingleInstanceMutex.TryAcquire("StreamHelperServer", tempFolder);
            Assert.IsNotNull(instance1);
            Assert.IsTrue(instance1.IsAcquired);

            // Mutex ownership is thread-affine in Windows; verify a different thread cannot acquire it
            SingleInstanceMutex? instance2 = null;
            var thread = new Thread(() =>
            {
                instance2 = SingleInstanceMutex.TryAcquire("StreamHelperServer", tempFolder);
            });
            thread.Start();
            thread.Join();

            using (instance2)
            {
                Assert.IsNull(instance2);
            }
        }
        finally
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [TestMethod]
    public void TryAcquire_DifferentFolders_AllowsConcurrentInstances()
    {
        var tempFolder1 = TestDirectory.Create("StreamHelperTest2A");
        var tempFolder2 = TestDirectory.Create("StreamHelperTest2B");

        try
        {
            using var instance1 = SingleInstanceMutex.TryAcquire("StreamHelperServer", tempFolder1);
            Assert.IsNotNull(instance1);

            using var instance2 = SingleInstanceMutex.TryAcquire("StreamHelperServer", tempFolder2);
            Assert.IsNotNull(instance2);
        }
        finally
        {
            if (Directory.Exists(tempFolder1)) Directory.Delete(tempFolder1, true);
            if (Directory.Exists(tempFolder2)) Directory.Delete(tempFolder2, true);
        }
    }

    [TestMethod]
    public void TryAcquire_AfterDispose_AllowsReacquisition()
    {
        var tempFolder = TestDirectory.Create("StreamHelperTest3");

        try
        {
            var instance1 = SingleInstanceMutex.TryAcquire("StreamHelperClient", tempFolder);
            Assert.IsNotNull(instance1);
            instance1.Dispose();

            using var instance2 = SingleInstanceMutex.TryAcquire("StreamHelperClient", tempFolder);
            Assert.IsNotNull(instance2);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }
}
