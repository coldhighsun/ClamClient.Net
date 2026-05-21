#if !NET5_0_OR_GREATER
// Enables C# 9+ `record` and `init` on pre-net5 TFMs.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
