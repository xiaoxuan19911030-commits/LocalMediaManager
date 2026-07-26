using System.Net;
using LocalMediaManager.Bridge;
using Xunit;

namespace LocalMediaManager.Bridge.Tests;

public sealed class ImageDownloadFailureEvidenceTests
{
    [Fact]
    public void FailureEvidenceIncludesResourceContextWithoutFullUrl()
    {
        var failure = new ImageDownloadFailure(
            "Poster",
            "MetaTube/FANZA",
            "https://cdn.example.test/private/poster.jpg?token=secret",
            true,
            new HttpRequestException("Response status code does not indicate success: 403.", null, HttpStatusCode.Forbidden));

        string evidence = ImageDownloadService.FormatFailureEvidence(failure);

        Assert.Contains("Type=Poster", evidence);
        Assert.Contains("Provider=MetaTube/FANZA", evidence);
        Assert.Contains("Host=cdn.example.test", evidence);
        Assert.Contains("HTTP=403", evidence);
        Assert.Contains("Referer=Applied", evidence);
        Assert.DoesNotContain("private/poster", evidence);
        Assert.DoesNotContain("secret", evidence);
    }
}
