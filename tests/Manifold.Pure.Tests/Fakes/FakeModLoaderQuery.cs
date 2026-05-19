using System.Collections.Generic;
using Manifold.Internal;

namespace Manifold.Pure.Tests.Fakes;

internal sealed class FakeModLoaderQuery : IModLoaderQuery
{
    public HashSet<string> LoadedMods { get; } = new();

    public bool IsModLoaded(string modId) => LoadedMods.Contains(modId);
}
