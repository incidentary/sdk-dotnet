using Xunit;

namespace Incidentary.Sdk.Tests;

public sealed class SanityTests
{
    [Fact]
    public void SdkVersionIsSet()
    {
        Assert.Equal("0.2.0", SdkVersion.Current);
    }
}
