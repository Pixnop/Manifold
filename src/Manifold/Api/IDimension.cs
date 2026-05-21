using Vintagestory.API.Common;

namespace Manifold.Api;

/// <summary>
/// A dimension known to Manifold.
/// </summary>
/// <remarks>
/// Instances are immutable from the consumer's perspective.
/// The same <see cref="Code"/> always maps to the same <see cref="InternalId"/>
/// within a single savegame (idempotent boot).
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
}
