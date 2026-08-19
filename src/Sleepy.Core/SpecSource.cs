
namespace Sleepy;

/// <summary>The bytes returned by a fetch, plus the content type the server declared.</summary>
public sealed record Fetched(byte[] Data, string ContentType);

/// <summary>
/// Reading documents from stdin, a file, or an http(s) URL.
/// </summary>
public sealed class SpecSource : IDisposable
{
    private readonly HttpClient _client;

    public SpecSource(IReadOnlyList<string> headers, bool allowInvalidCertificates)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
        };

        if (allowInvalidCertificates)
        {
            handler.ServerCertificateCustomValidationCallback =
                (_, _, _, _) => true;
        }

        _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _client.DefaultRequestHeaders.Add("User-Agent", SleepyInfo.UserAgent);
        _client.DefaultRequestHeaders.Add("Accept", "application/json, text/javascript, */*");

        foreach (var header in headers)
        {
            var separator = header.IndexOf(':');
            if (separator <= 0)
            {
                throw new SleepyException($"invalid --header value (expected \"Name: value\"): {header}");
            }

            var name = header[..separator].Trim();
            var value = header[(separator + 1)..].Trim();
            if (!_client.DefaultRequestHeaders.TryAddWithoutValidation(name, value))
            {
                throw new SleepyException($"invalid --header value: {header}");
            }
        }
    }

    public static bool IsUrl(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the origin (scheme://host[:port]) of a URL, e.g.
    /// <c>https://api.example.com/v1/openapi.json</c> becomes
    /// <c>https://api.example.com</c>.
    /// </summary>
    public static string Origin(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : url.TrimEnd('/');

    /// <summary>Resolves a possibly relative URL against the document it was found in.</summary>
    public static string ResolveUrl(string baseUrl, string reference)
        => Uri.TryCreate(new Uri(baseUrl, UriKind.Absolute), reference, out var resolved)
            ? resolved.ToString()
            : reference;

    public async Task<Fetched> FetchAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new SleepyException(
                $"fetching {url} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new Fetched(data, response.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }

    /// <summary>Like <see cref="FetchAsync"/> but returns null instead of throwing on any failure.</summary>
    public async Task<Fetched?> TryFetchAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await FetchAsync(url, cancellationToken).ConfigureAwait(false);
        }
        catch (SleepyException)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public static async Task<byte[]> ReadStdinAsync(CancellationToken cancellationToken)
    {
        await using var stdin = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        await stdin.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    public void Dispose() => _client.Dispose();
}

/// <summary>An error that should be reported to the user without a stack trace.</summary>
public sealed class SleepyException(string message) : Exception(message);
