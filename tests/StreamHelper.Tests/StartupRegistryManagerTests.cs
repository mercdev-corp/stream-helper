using Microsoft.Win32;
using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
public sealed class StartupRegistryManagerTests
{
    [TestMethod]
    public void GetKeyName_DerivesFromExecutableDirectory()
    {
        var exe1 = @"C:\Games\Mic1\StreamHelper.Server.exe";
        var exe2 = @"C:\Games\Mic2\StreamHelper.Server.exe";

        var key1 = StartupRegistryManager.GetKeyName("StreamHelperServer", exe1);
        var key2 = StartupRegistryManager.GetKeyName("StreamHelperServer", exe2);

        Assert.AreNotEqual(key1, key2);
        Assert.StartsWith("StreamHelperServer_", key1);
        Assert.StartsWith("StreamHelperServer_", key2);
    }

    [TestMethod]
    public void SetStartupEnabled_AddAndRemove_WorksCorrectly()
    {
        var testKey = @"Software\StreamHelperTest_Run_" + Guid.NewGuid().ToString("N");
        try
        {
            var exe = @"C:\MicApp\StreamHelper.Server.exe";

            // Initially disabled
            Assert.IsFalse(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exe, testKey));

            // Enable
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", true, exe, testKey);
            Assert.IsTrue(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exe, testKey));

            // Disable
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", false, exe, testKey);
            Assert.IsFalse(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exe, testKey));
        }
        catch (UnauthorizedAccessException ex)
        {
            Assert.Inconclusive("Skipping test due to sandbox registry isolation: " + ex.Message);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false); } catch { }
        }
    }

    [TestMethod]
    public void SetStartupEnabled_MultipleFolders_AreIndependent()
    {
        var testKey = @"Software\StreamHelperTest_Run_" + Guid.NewGuid().ToString("N");
        try
        {
            var exeFolder1 = @"C:\Folder1\StreamHelper.Server.exe";
            var exeFolder2 = @"C:\Folder2\StreamHelper.Server.exe";

            // Enable both
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", true, exeFolder1, testKey);
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", true, exeFolder2, testKey);

            Assert.IsTrue(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exeFolder1, testKey));
            Assert.IsTrue(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exeFolder2, testKey));

            // Disable folder 1 only
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", false, exeFolder1, testKey);

            Assert.IsFalse(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exeFolder1, testKey));
            Assert.IsTrue(StartupRegistryManager.IsStartupEnabled("StreamHelperServer", exeFolder2, testKey));
        }
        catch (UnauthorizedAccessException ex)
        {
            Assert.Inconclusive("Skipping test due to sandbox registry isolation: " + ex.Message);
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testKey, throwOnMissingSubKey: false); } catch { }
        }
    }
}
