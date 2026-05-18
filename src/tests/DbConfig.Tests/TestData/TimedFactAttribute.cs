using System.Runtime.CompilerServices;

namespace DbConfig.Tests.TestData;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TimedFactAttribute : FactAttribute
{
    public TimedFactAttribute(
        int timeout = 10_000,
        [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Timeout = timeout;
    }
}
