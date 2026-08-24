namespace Sleepy;

/// <summary>An error that should be reported to the user without a stack trace.</summary>
public sealed class SleepyException(string message) : Exception(message);
