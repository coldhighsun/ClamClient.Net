#if NETSTANDARD2_0
// Enables C# 9+ `record` and `init` on pre-net5 TFMs.
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// This class is used by the C# compiler to support `init` properties and `record` types in versions of .NET that do not have it built-in (pre-.NET 5).
    /// </summary>
    internal static class IsExternalInit { }
}
#endif
