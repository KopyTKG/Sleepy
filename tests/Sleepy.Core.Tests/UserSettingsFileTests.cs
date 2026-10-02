using System.Text.Json.Nodes;

namespace Sleepy.Tests;

public class UserSettingsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"sleepy-settings-{Guid.NewGuid():N}");

    private string Path_ => Path.Combine(_directory, "settings.json");

    [Fact]
    public void ReadsBackWhatItWrote()
    {
        var settings = new UserSettings
        {
            Language = "cs",
            Format = "bruno",
            Layout = FolderLayout.Flat,
            RequestHeader = "Authorization: Bearer x",
            IgnoreCertificateErrors = true,
            UiScale = 1.25,
        };

        Assert.True(UserSettingsFile.Write(Path_, settings));

        Assert.Equal(settings, UserSettingsFile.Read(Path_));
    }

    [Fact]
    public void CreatesTheDirectoryItNeeds()
    {
        var nested = System.IO.Path.Combine(_directory, "a", "b", "settings.json");

        Assert.True(UserSettingsFile.Write(nested, new UserSettings { Format = "postman" }));
        Assert.Equal("postman", UserSettingsFile.Read(nested).Format);
    }

    [Fact]
    public void ReturnsDefaultsWhenThereIsNoFile()
    {
        var settings = UserSettingsFile.Read(System.IO.Path.Combine(_directory, "absent.json"));

        Assert.Equal(new UserSettings(), settings);
        Assert.Equal(FolderLayout.Nested, settings.Layout);
    }

    [Fact]
    public void ReturnsDefaultsForAMalformedFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, "{ this is not json");

        Assert.Equal(new UserSettings(), UserSettingsFile.Read(Path_));
    }

    [Fact]
    public void IgnoresUnknownAndMissingFields()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path_, """{"format":"postman","somethingElse":42}""");

        var settings = UserSettingsFile.Read(Path_);

        Assert.Equal("postman", settings.Format);
        Assert.Equal(string.Empty, settings.Language);
        Assert.Equal(FolderLayout.Nested, settings.Layout);
    }

    [Fact]
    public void FallsBackToTheDefaultLayoutForAnUnknownOne()
    {
        var settings = UserSettingsFile.FromJson(JsonNode.Parse("""{"layout":"sideways"}"""));

        Assert.Equal(FolderLayout.Nested, settings.Layout);
    }

    [Theory]
    [InlineData(FolderLayout.Nested, "nested")]
    [InlineData(FolderLayout.Flat, "flat")]
    [InlineData(FolderLayout.ModuleOnly, "moduleonly")]
    public void WritesLayoutsAsNamesRatherThanNumbers(FolderLayout layout, string expected)
    {
        var json = UserSettingsFile.ToJson(new UserSettings { Layout = layout });

        Assert.Equal(expected, json["layout"]?.GetValue<string>());
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("1", 1.0)]
    [InlineData("0", 0.0)]
    [InlineData("-2", 0.0)]
    [InlineData("0.1", UserSettings.MinUiScale)]
    [InlineData("12", UserSettings.MaxUiScale)]
    [InlineData("\"big\"", 0.0)]
    public void ReadsTheUiScaleClampedToWhatIsUsable(string json, double expected)
    {
        var settings = UserSettingsFile.FromJson(JsonNode.Parse($$"""{"uiScale":{{json}}}"""));

        Assert.Equal(expected, settings.UiScale);
    }

    [Theory]
    [InlineData("1.5", 1.5)]
    [InlineData("150", 1.5)]
    [InlineData("150%", 1.5)]
    [InlineData(" 125 % ", 1.25)]
    [InlineData("2", 2.0)]
    [InlineData("1000%", UserSettings.MaxUiScale)]
    [InlineData("big", 0.0)]
    [InlineData("", 0.0)]
    [InlineData(null, 0.0)]
    public void ParsesAScaleTheWayPeopleTypeIt(string? text, double expected)
        => Assert.Equal(expected, UserSettings.ParseUiScale(text));

    [Fact]
    public void FollowsTheDisplayWhenNoScaleIsStored()
        => Assert.Equal(0, UserSettingsFile.FromJson(JsonNode.Parse("{}")).UiScale);

    [Fact]
    public void NeverWritesACredential()
    {
        UserSettingsFile.Write(Path_, new UserSettings { Format = "insomnia" });

        var written = File.ReadAllText(Path_);

        Assert.DoesNotContain("bearer", written, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", written, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PutsTheFileWhereThePlatformKeepsConfiguration()
    {
        var path = UserSettingsFile.DefaultPath;

        Assert.EndsWith(System.IO.Path.Combine("sleepy", "settings.json"), path, StringComparison.Ordinal);

        // Absolute even where the environment reports no home directory, which is
        // the normal state of a build container: a relative path would scatter
        // settings files through whatever directory the app started in.
        Assert.True(
            System.IO.Path.IsPathRooted(path),
            $"the settings path must be absolute, but was '{path}'");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
