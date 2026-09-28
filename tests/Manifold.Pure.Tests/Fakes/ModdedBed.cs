namespace Manifold.Pure.Tests.Fakes;

/// <summary>
/// A modded block class inheriting from the vanilla bed stand-in, reproducing the two-linked-halves
/// pattern without sharing the exact vanilla FullName. Exercises the base-class-chain walk in
/// <c>MultiPositionBlockDetector</c>.
/// </summary>
internal sealed class ModdedBed : global::Vintagestory.GameContent.BlockBed
{
}
