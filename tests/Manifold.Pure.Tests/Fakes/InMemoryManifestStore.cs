using System.Collections.Generic;
using Manifold.Internal;

namespace Manifold.Pure.Tests.Fakes;

internal sealed class InMemoryManifestStore : IManifestStore
{
    private readonly Dictionary<string, byte[]> _data = new();

    public byte[]? Read(string key) => _data.TryGetValue(key, out var v) ? v : null;

    public void Write(string key, byte[] data) => _data[key] = data;
}
