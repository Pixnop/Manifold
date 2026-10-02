using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Notices that a player who died has respawned, and hands them to <see cref="TransitService.RespawnPlayer"/>
/// once the engine has finished its own respawn. The engine's respawn event fires while the player may
/// still be dead: its handler moves the player and revives them only when the destination column is
/// loaded, which is immediate when it already is and some ticks later when it is not. So the request
/// is answered by watching the player until they are alive, and only then moving them.
/// </summary>
/// <remarks>
/// The respawn event also fires for a respawn request from a player who is alive (the engine ignores
/// it but still raises the event), so only players seen dying are handled: through
/// <c>PlayerDeath</c>, or already dead when they joined (they died in an earlier session).
/// Main thread only.
/// </remarks>
internal sealed class RespawnWatcher
{
    /// <summary>How long the watcher waits between two looks at a player the engine has not revived yet.</summary>
    internal const int PollIntervalMs = 50;

    /// <summary>How many looks it takes before giving up on a player the engine never revives (about a minute).</summary>
    internal const int MaxPolls = 1200;

    private readonly System.Func<IServerPlayer, bool> _respawn;
    private readonly Action<Action, int> _schedule;
    private readonly ILogger? _logger;
    private readonly HashSet<string> _dead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IServerPlayer> _waiting = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="RespawnWatcher"/> class.</summary>
    /// <param name="respawn">Moves a revived player out of the dimension they died in.</param>
    /// <param name="schedule">Runs an action once after the given number of milliseconds, on the main thread.</param>
    /// <param name="logger">Logger, or <c>null</c> for none.</param>
    public RespawnWatcher(System.Func<IServerPlayer, bool> respawn, Action<Action, int> schedule, ILogger? logger)
    {
        _respawn = respawn ?? throw new ArgumentNullException(nameof(respawn));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _logger = logger;
    }

    /// <summary>Subscribes the watcher to the game events it follows: death, respawn request, join and disconnect.</summary>
    /// <param name="events">The server event API.</param>
    public void Attach(IServerEventAPI events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.PlayerDeath += (player, _) => OnDeath(player);
        events.PlayerRespawn += OnRespawnRequested;
        events.PlayerNowPlaying += OnNowPlaying;
        events.PlayerDisconnect += OnDisconnect;
    }

    /// <summary>Records that a player died.</summary>
    /// <param name="player">The player.</param>
    public void OnDeath(IServerPlayer player) => _dead.Add(player.PlayerUID);

    /// <summary>Records a player who joined already dead (they died before the server restarted or before they logged out).</summary>
    /// <param name="player">The player.</param>
    public void OnNowPlaying(IServerPlayer player)
    {
        if (player.Entity is { Alive: false })
        {
            _dead.Add(player.PlayerUID);
        }
    }

    /// <summary>Forgets a player who left: nothing is pending for them, and a death is re-detected when they come back.</summary>
    /// <param name="player">The player.</param>
    public void OnDisconnect(IServerPlayer player)
    {
        _dead.Remove(player.PlayerUID);
        _waiting.Remove(player.PlayerUID);
    }

    /// <summary>The engine's respawn event: starts watching a dead player until the engine has revived them.</summary>
    /// <param name="player">The player who asked to respawn.</param>
    public void OnRespawnRequested(IServerPlayer player)
    {
        string uid = player.PlayerUID;
        if (_dead.Contains(uid) && _waiting.TryAdd(uid, player))
        {
            Poll(player, 0);
        }
    }

    private void Poll(IServerPlayer player, int attempt)
    {
        string uid = player.PlayerUID;
        if (!_waiting.TryGetValue(uid, out var watched) || !ReferenceEquals(watched, player))
        {
            return; // they left (and maybe came back as someone else's connection): this watch is stale
        }

        if (player.Entity is not { } entity)
        {
            _waiting.Remove(uid);
            return;
        }

        if (entity.Alive)
        {
            _waiting.Remove(uid);
            _dead.Remove(uid);
            Respawn(player);
            return;
        }

        if (attempt >= MaxPolls)
        {
            _waiting.Remove(uid);
            _logger?.Warning(
                "[Manifold] {0} asked to respawn but the game never revived them; not moving them out of their dimension.",
                player.PlayerName);
            return;
        }

        _schedule(() => Poll(player, attempt + 1), PollIntervalMs);
    }

    private void Respawn(IServerPlayer player)
    {
        try
        {
            _respawn(player);
        }
        catch (Exception ex)
        {
            _logger?.Error("[Manifold] Moving {0} out of their dimension after a respawn failed: {1}", player.PlayerName, ex);
        }
    }
}
