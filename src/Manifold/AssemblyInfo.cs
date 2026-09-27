using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Manifold.Pure.Tests")]

// The integration scenarios decode Manifold's network packets through Atlas's client observations,
// which deserialize into the scenario's own copy of the (internal) packet types.
[assembly: InternalsVisibleTo("Manifold.Scenarios")]

// Castle DynamicProxy (used by NSubstitute) needs access to internal interfaces
// it must subclass at runtime. Without this, internal interface substitution fails.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
