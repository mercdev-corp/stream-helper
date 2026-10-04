[assembly: Parallelize(Workers = 4, Scope = ExecutionScope.MethodLevel)]

namespace StreamHelper.Tests;


[TestClass]
[DeploymentItem("StreamHelper.Tests.dll")]
public class DeploymentSuppressionTests
{
    [TestMethod]
    public void VerifyDeploymentSuppressed()
    {
        // Anchors [DeploymentItem] so MSTest sees hasDeploymentItems == true,
        // which combined with <DeploymentEnabled>False</DeploymentEnabled> in .runsettings
        // causes TestDeployment.Deploy to return early without calling CreateDeploymentDirectories.
        Assert.IsNotNull(AppDomain.CurrentDomain.BaseDirectory);
    }
}

public static class TestDirectory
{
    private static readonly string BaseDirectory = ResolveBaseDirectory();

    private static string ResolveBaseDirectory()
    {
        // 1. Explicit environment variable override
        var envTemp = Environment.GetEnvironmentVariable("STREAMHELPER_TEST_TEMP");
        if (!string.IsNullOrEmpty(envTemp))
        {
            try
            {
                Directory.CreateDirectory(envTemp);
                return envTemp;
            }
            catch { }
        }

        // 2. Try AppContext.BaseDirectory/.test_tmp (local to test binary on workspace drive, bypasses Windows Defender CFA)
        try
        {
            var localTemp = Path.Combine(AppContext.BaseDirectory, ".test_tmp");
            Directory.CreateDirectory(localTemp);
            var probe = Path.Combine(localTemp, $"probe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return localTemp;
        }
        catch { }

        // 3. System temp fallback
        try
        {
            var sysTemp = Path.GetTempPath();
            var probe = Path.Combine(sysTemp, $"probe_{Guid.NewGuid():N}");
            Directory.CreateDirectory(probe);
            Directory.Delete(probe);
            return sysTemp;
        }
        catch { }

        // 4. Working directory fallback
        var cwdTemp = Path.Combine(Directory.GetCurrentDirectory(), ".test_tmp");
        Directory.CreateDirectory(cwdTemp);
        return cwdTemp;
    }

    public static string Create(string prefix)
    {
        var baseDir = BaseDirectory;
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var dir = Path.Combine(baseDir, $"{prefix}_{Guid.NewGuid():N}");
                Directory.CreateDirectory(dir);
                return dir;
            }
            catch (Exception) when (i < 4)
            {
                Thread.Sleep(50);
            }
        }

        // Fallback: try AppContext.BaseDirectory directly if the primary directory failed
        var fallbackBase = Path.Combine(AppContext.BaseDirectory, ".test_tmp");
        try { Directory.CreateDirectory(fallbackBase); } catch { }
        var fallbackDir = Path.Combine(fallbackBase, $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallbackDir);
        return fallbackDir;
    }
}
