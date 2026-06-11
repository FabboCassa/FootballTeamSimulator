// Polyfills so modern C# language features compile against netstandard2.1.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>Enables init-only setters and records on netstandard2.1.</summary>
    internal static class IsExternalInit { }
}
