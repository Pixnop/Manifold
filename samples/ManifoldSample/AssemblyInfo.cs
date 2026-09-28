using System.Runtime.CompilerServices;

// Lets Manifold.Pure.Tests call ManifoldSampleModSystem's internal command handlers directly,
// without wiring real chat commands.
[assembly: InternalsVisibleTo("Manifold.Pure.Tests")]
