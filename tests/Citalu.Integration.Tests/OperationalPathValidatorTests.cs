using Citalu.Web.Services;
using Xunit;

namespace Citalu.Integration.Tests;

public sealed class OperationalPathValidatorTests
{
    [Fact]
    public void CapturePathOutsidePublishedWebRootIsAccepted()
    {
        var siteRoot = Path.Combine(Path.GetTempPath(), "CitaluPathValidation", Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(siteRoot, "wwwroot");
        var privateCapturePath = Path.Combine(siteRoot, "private", "mail-capture");

        Assert.False(OperationalPathValidator.IsInsideWebRoot(webRoot, privateCapturePath));
    }

    [Fact]
    public void CapturePathInsidePublishedWebRootIsRejected()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "CitaluPathValidation", Guid.NewGuid().ToString("N"), "wwwroot");
        var capturePath = Path.Combine(webRoot, "mail-capture");

        Assert.True(OperationalPathValidator.IsInsideWebRoot(webRoot, capturePath));
        Assert.True(OperationalPathValidator.IsInsideWebRoot(webRoot, webRoot));
    }
}
