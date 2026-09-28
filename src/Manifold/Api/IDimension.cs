using System.Collections.Generic;
using Vintagestory.API.Common;

namespace Manifold.Api;

/// <summary>
/// A dimension known to Manifold.
/// </summary>
/// <remarks>
/// Instances are immutable from the consumer's perspective.
/// While a dimension is registered, its <see cref="Code"/> maps to a fixed <see cref="InternalId"/>,
/// and a <see cref="DimensionLifetime.Persistent"/> dimension keeps its id across restarts. Ids are
/// released back to the allocator when a dimension is removed and may be reused by a different
/// code afterwards, so never cache <see cref="InternalId"/> across a <c>Destroyed</c> event.
/// </remarks>
public interface IDimension
{
    /// <summary>Stable consumer-facing identifier (e.g. <c>mymod:nether</c>).</summary>
    AssetLocation Code { get; }

    /// <summary>
    /// VS engine dimension id (0..1023). Built-in overworld is 0;
    /// mod-allocated values lie in 10..1023. Rarely useful to consumers - prefer <see cref="Code"/>.
    /// </summary>
    int InternalId { get; }

    /// <summary><c>true</c> for the engine's overworld (<c>manifold:overworld</c>, id 0).</summary>
    bool IsBuiltIn { get; }

    /// <summary>Lifetime category - determines persistence behaviour.</summary>
    DimensionLifetime Lifetime { get; }

    /// <summary>Mod id of the consumer that originally registered this dimension.</summary>
    string OwnerModId { get; }

    /// <summary>Current runtime state - controls eligibility for transit and worldgen.</summary>
    DimensionState State { get; }

    /// <summary>
    /// Read-only metadata attached to this dimension at registration time. Owning mods populate
    /// it via <c>IDimensionBuilder.WithMetadata</c>; consumers query it directly or through the
    /// typed <c>GetMetadata&lt;T&gt;</c> extension. Replicated to client mirrors; not persisted
    /// across server restarts (re-declare in your boot path).
    /// </summary>
    /// <remarks>
    /// Supported value types are primitives, <c>string</c>, <c>enum</c>, and <c>byte[]</c>; passing
    /// other types to <c>WithMetadata</c> throws. Empty for the built-in overworld and for
    /// dimensions reloaded from the manifest.
    /// On a client mirror an enum value is resolved back to its original enum type by searching the
    /// client's loaded assemblies for the owning mod's assembly; when that assembly cannot be
    /// resolved client-side (for example the owning mod is not installed on the client), the value
    /// is instead the raw underlying value as a <c>long</c>.
    /// </remarks>
    IReadOnlyDictionary<string, object?> Metadata { get; }
}
