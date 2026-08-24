namespace Sleepy.Discovery;

/// <summary>The bytes returned by a fetch, plus the content type the server declared.</summary>
public sealed record Fetched(byte[] Data, string ContentType);
