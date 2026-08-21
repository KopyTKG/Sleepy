using System.Reflection;

namespace Sleepy.Tests;

public class SleepyInfoTests
{
    [Fact]
    public void ReportsTheVersionTheAssemblyWasBuiltWith()
    {
        var informational = typeof(SleepyInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        Assert.Equal(informational, SleepyInfo.FullVersion);
        Assert.NotEmpty(SleepyInfo.Version);
    }

    [Fact]
    public void SplitsBuildMetadataOffTheVersion()
    {
        Assert.DoesNotContain('+', SleepyInfo.Version);
        Assert.StartsWith(SleepyInfo.Version, SleepyInfo.FullVersion, StringComparison.Ordinal);
    }

    [Fact]
    public void ExposesTheCommitWhenTheBuildStampedOne()
    {
        if (SleepyInfo.Commit is null)
        {
            // A build with no git information: the version carries no metadata.
            Assert.Equal(SleepyInfo.Version, SleepyInfo.FullVersion);
            return;
        }

        Assert.NotEmpty(SleepyInfo.Commit);
        Assert.Equal($"{SleepyInfo.Version}+{SleepyInfo.Commit}", SleepyInfo.FullVersion);
    }

    [Fact]
    public void BuildIdentityCarriesTheFullVersion()
        => Assert.StartsWith(SleepyInfo.FullVersion, SleepyInfo.BuildIdentity, StringComparison.Ordinal);

    [Fact]
    public void UserAgentNamesTheToolAndVersion()
        => Assert.Equal($"sleepy/{SleepyInfo.Version}", SleepyInfo.UserAgent);

    [Fact]
    public void EmbedsTheLicenceAndNotices()
    {
        Assert.Contains("BSD 3-Clause License", SleepyInfo.License, StringComparison.Ordinal);
        Assert.Contains("Third-party notices", SleepyInfo.ThirdPartyNotices, StringComparison.Ordinal);
    }
}
