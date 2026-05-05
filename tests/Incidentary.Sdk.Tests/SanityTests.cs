using Xunit;

namespace Incidentary.Sdk.Tests;

public sealed class SanityTests
{
    [Fact]
    public void SdkVersionIsSet()
    {
        Assert.Equal("1.0.0", SdkVersion.Current);
    }
}
