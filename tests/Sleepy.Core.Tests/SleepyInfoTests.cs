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
    public void EmbedsTheLicence()
        => Assert.Contains("BSD 3-Clause License", SleepyInfo.License, StringComparison.Ordinal);

    [Fact]
    public void ReadsTheThirdPartyComponentsAsData()
    {
        var components = SleepyInfo.ThirdPartyComponents;

        Assert.NotEmpty(components);
        Assert.All(components, component =>
        {
            Assert.False(string.IsNullOrWhiteSpace(component.Name));
            Assert.False(string.IsNullOrWhiteSpace(component.License));
            Assert.False(string.IsNullOrWhiteSpace(component.UsedBy));
            Assert.StartsWith("https://", component.Url, StringComparison.Ordinal);
        });
        Assert.Contains(components, component => component.Name == "YamlDotNet");
        Assert.NotEmpty(SleepyInfo.ThirdPartyNotes);
    }

    [Fact]
    public void RendersTheNoticesAsAlignedText()
    {
        var text = SleepyInfo.ThirdPartyNotices;

        Assert.Contains("Third-party components", text, StringComparison.Ordinal);
        Assert.All(SleepyInfo.ThirdPartyComponents, component =>
            Assert.Contains(component.Name, text, StringComparison.Ordinal));

        // Aligned means every component row starts at the same column.
        var rows = text.Split('\n')
            .Where(line => line.StartsWith("  ", StringComparison.Ordinal) && line.Trim().Length > 0)
            .ToList();
        Assert.All(rows, row => Assert.StartsWith("  ", row, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRepositoryNoticesMatchTheEmbeddedOnes()
    {
        // The markdown file is what a reader of the repository sees; the embedded
        // data is what a user of a binary sees. They have to say the same thing.
        var markdown = File.ReadAllText(FindRepositoryFile("THIRD-PARTY-NOTICES.md"));

        Assert.All(SleepyInfo.ThirdPartyComponents, component =>
        {
            Assert.Contains(component.Name, markdown, StringComparison.Ordinal);
            Assert.Contains(component.License, markdown, StringComparison.Ordinal);
        });
    }

    private static string FindRepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{name} not found above {AppContext.BaseDirectory}");
    }
}
