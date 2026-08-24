using System.Text;

namespace Sleepy;

public static class Slug
{
    /// <summary>
    /// Lowercases the input and replaces every run of non-alphanumeric
    /// characters with a single underscore, then trims leading/trailing
    /// underscores.
    /// </summary>
    public static string Make(string input)
    {
        var builder = new StringBuilder(input.Length);
        var previousUnderscore = false;

        foreach (var rune in input.EnumerateRunes())
        {
            var lower = Rune.ToLowerInvariant(rune);
            var isAlphanumeric = (lower.Value >= 'a' && lower.Value <= 'z')
                || (lower.Value >= '0' && lower.Value <= '9');

            if (isAlphanumeric)
            {
                builder.Append(lower);
                previousUnderscore = false;
            }
            else if (!previousUnderscore)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        return builder.ToString().Trim('_');
    }
}
