using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Manifold.Pure.Tests")]

// Castle DynamicProxy (used by NSubstitute) needs access to internal interfaces
// it must subclass at runtime. Without this, internal interface substitution fails.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
