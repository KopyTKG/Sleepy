namespace Sleepy.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Users", "users")]
    [InlineData("create user!!", "create_user")]
    [InlineData("  spaced  out  ", "spaced_out")]
    [InlineData("get_/api/admin/users", "get_api_admin_users")]
    [InlineData("---", "")]
    public void MakesStableSlugs(string input, string expected)
        => Assert.Equal(expected, Slug.Make(input));

    [Fact]
    public void CollapsesRunsOfNonAlphanumerics()
        => Assert.Equal("a_b", Slug.Make("a????b"));
}
