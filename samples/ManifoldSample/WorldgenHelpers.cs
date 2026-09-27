using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ManifoldSample;

/// <summary>Tiny helpers shared by this sample's worldgen strategies.</summary>
internal static class WorldgenHelpers
{
    /// <summary>Chunk column size (in blocks) every strategy in this sample generates against.</summary>
    internal const int ChunkSize = 32;

    /// <summary>Returns the id of the first of <paramref name="codes"/> that resolves to a real block, or 0 if none do.</summary>
    internal static int ResolveFirst(ICoreServerAPI api, params string[] codes)
    {
        foreach (var code in codes)
        {
            var block = api.World.GetBlock(new AssetLocation(code));
            if (block is not null && block.Id != 0)
            {
                return block.Id;
            }
        }

        return 0;
    }
}
