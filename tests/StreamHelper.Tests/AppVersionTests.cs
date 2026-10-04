using System.Reflection;
using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
public sealed class AppVersionTests
{
    [TestMethod]
    public void AppVersion_Properties_ReturnExpectedValues()
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(AppVersion.RawVersion));
        Assert.IsTrue(Version.TryParse(AppVersion.RawVersion, out _));
        Assert.AreEqual($"v{AppVersion.RawVersion}", AppVersion.DisplayVersion);
    }

    [TestMethod]
    public void NormalizeRawVersion_StripsMetadataAndLeadingV()
    {
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("0.1.0"));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("v0.1.0"));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("V0.1.0"));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("0.1.0+abc1234"));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("  v0.1.0+commit-hash  "));
        Assert.AreEqual("1.2.3.4", AppVersion.NormalizeRawVersion("1.2.3.4"));
    }

    [TestMethod]
    public void NormalizeRawVersion_HandlesNullOrEmptyFallback()
    {
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion(null));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion(""));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("   "));
        Assert.AreEqual("0.1.0", AppVersion.NormalizeRawVersion("v"));
    }

    [TestMethod]
    public void FormatDisplayVersion_FormatsWithVPrefix()
    {
        Assert.AreEqual("v0.1.0", AppVersion.FormatDisplayVersion("0.1.0"));
        Assert.AreEqual("v0.1.0", AppVersion.FormatDisplayVersion("v0.1.0"));
        Assert.AreEqual("v0.1.0", AppVersion.FormatDisplayVersion("0.1.0+abc1234"));
        Assert.AreEqual("v0.1.0", AppVersion.FormatDisplayVersion(null));
        Assert.AreEqual("v2.5.0", AppVersion.FormatDisplayVersion("2.5.0"));
    }

    [TestMethod]
    public void ExtractVersion_FromSharedAssembly_ReturnsValidVersion()
    {
        string version = AppVersion.ExtractVersion(typeof(AppVersion).Assembly);
        Assert.AreEqual(AppVersion.RawVersion, version);
    }

    [TestMethod]
    public void ExtractVersion_NullAssembly_ReturnsFallback()
    {
        string version = AppVersion.ExtractVersion(null);
        Assert.AreEqual("0.1.0", version);
    }
}
