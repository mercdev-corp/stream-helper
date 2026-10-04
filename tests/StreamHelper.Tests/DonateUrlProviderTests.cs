using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
public sealed class DonateUrlProviderTests
{
    [TestMethod]
    public void GetDonateUrl_ReturnsExpectedUrlFromEmbeddedResource()
    {
        var url = DonateUrlProvider.GetDonateUrl();

        Assert.IsFalse(string.IsNullOrWhiteSpace(url));
        Assert.AreEqual(DonateUrlProvider.DefaultDonateUrl, url);
        Assert.IsTrue(DonateUrlProvider.IsValidUrl(url));
    }

    [TestMethod]
    public void ParseAndValidateUrl_TrimsWhitespaceAndNewlines()
    {
        var rawWithWhitespace = "\r\n  https://ko-fi.com/mercdev_pro  \t\n";
        var result = DonateUrlProvider.ParseAndValidateUrl(rawWithWhitespace);

        Assert.AreEqual("https://ko-fi.com/mercdev_pro", result);
    }

    [TestMethod]
    public void ParseAndValidateUrl_InvalidOrEmptyUrl_ReturnsFallback()
    {
        string?[] invalidInputs =
        {
            null,
            "",
            "   \t\r\n",
            "not-a-valid-url",
            "ftp://example.com/donate",
            "file:///C:/donate.txt",
            "javascript:alert('xss')"
        };

        foreach (var input in invalidInputs)
        {
            var fallback = "https://fallback.example.com";
            var result = DonateUrlProvider.ParseAndValidateUrl(input, fallback);
            Assert.AreEqual(fallback, result, $"Failed for input: '{input}'");
        }
    }

    [TestMethod]
    public void ParseAndValidateUrl_ValidHttpAndHttps_ReturnsTrimmedUrl()
    {
        Assert.AreEqual("https://example.com/donate", DonateUrlProvider.ParseAndValidateUrl("  https://example.com/donate  "));
        Assert.AreEqual("http://example.com/donate", DonateUrlProvider.ParseAndValidateUrl("http://example.com/donate"));
    }

    [TestMethod]
    public void IsValidUrl_ValidHttpAndHttps_ReturnsTrue()
    {
        Assert.IsTrue(DonateUrlProvider.IsValidUrl("https://ko-fi.com/mercdev_pro"));
        Assert.IsTrue(DonateUrlProvider.IsValidUrl("http://example.com/page"));
        Assert.IsTrue(DonateUrlProvider.IsValidUrl("  https://example.com/page  "));
    }

    [TestMethod]
    public void IsValidUrl_InvalidInputs_ReturnsFalse()
    {
        Assert.IsFalse(DonateUrlProvider.IsValidUrl(null));
        Assert.IsFalse(DonateUrlProvider.IsValidUrl(string.Empty));
        Assert.IsFalse(DonateUrlProvider.IsValidUrl("   "));
        Assert.IsFalse(DonateUrlProvider.IsValidUrl("not_a_url"));
        Assert.IsFalse(DonateUrlProvider.IsValidUrl("ftp://example.com"));
        Assert.IsFalse(DonateUrlProvider.IsValidUrl("mailto:user@example.com"));
    }

    [TestMethod]
    public void ResolveDonateUrl_WithCustomContent_ReturnsValidatedOrFallback()
    {
        Assert.AreEqual("https://custom.org/fund", DonateUrlProvider.ResolveDonateUrl(" https://custom.org/fund "));
        Assert.AreEqual(DonateUrlProvider.DefaultDonateUrl, DonateUrlProvider.ResolveDonateUrl(" invalid "));
        Assert.AreEqual(DonateUrlProvider.DefaultDonateUrl, DonateUrlProvider.ResolveDonateUrl(null));
    }
}
