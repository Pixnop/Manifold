using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Manifold.Internal;

/// <summary>
/// Notices that a player who died has respawned, and hands them to <see cref="TransitService.RespawnPlayer"/>
/// once the engine has finished its own respawn. The engine's respawn event fires while the player may
/// still be dead: its handler moves the player and revives them only when the destination column is
/// loaded, which is immediate when it already is and some ticks later when it is not. The watcher
/// follows the revive itself, through the player entity's <c>entityDead</c> watched attribute, so it
/// waits exactly as long as the engine does and never gives up on a player who is still connected.
/// </summary>
/// <remarks>
/// The same attribute tells a respawn from any other revive. Another player can revive a dead player
/// on the spot (a healing item), which raises no respawn event: a revive that no respawn request
/// claims in the same tick clears the player's dead state, so a later respawn request from a living
/// player (the engine ignores it but still raises its event) cannot move them. The attribute is read
/// rather than <c>Entity.Alive</c>: the engine's setter writes it, and so runs the listener, before it
/// assigns the field. Main thread only.
/// </remarks>
internal sealed class RespawnWatcher
{
    /// <summary>Delay, in milliseconds, before the deferred work that must run after the engine's revive has finished.</summary>
    internal const int DeferMs = 1;

    /// <summary>How long to wait before trying again to move a player the transit service failed to move.</summary>
    internal const int RetryMs = 50;

    /// <summary>How many times in all the move is attempted before the failure is left in the log.</summary>
    internal const int MaxAttempts = 3;

    private const string DeadAttribute = "entityDead";

    private readonly System.Func<IServerPlayer, bool> _respawn;
    private readonly Action<Action, int> _schedule;
    private readonly ILogger? _logger;
    private readonly Dictionary<string, IServerPlayer> _players = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dead = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requested = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revived = new(StringComparer.Ordinal);

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

    /// <summary>Subscribes the watcher to the game events it follows: join, respawn request and disconnect.</summary>
    /// <param name="events">The server event API.</param>
    public void Attach(IServerEventAPI events)
    {
        ArgumentNullException.ThrowIfNull(events);
        events.PlayerNowPlaying += OnNowPlaying;
        events.PlayerRespawn += OnRespawnRequested;
        events.PlayerDisconnect += OnDisconnect;
    }

    /// <summary>
    /// Starts following a player who has just joined: their deaths and revives from now on, and their
    /// death right now if they come back dead (they died before they logged out or the server restarted).
    /// </summary>
    /// <param name="player">The player.</param>
    public void OnNowPlaying(IServerPlayer player)
    {
        if (player.Entity is not { } entity || (_players.TryGetValue(player.PlayerUID, out var known) && ReferenceEquals(known, player)))
        {
            return;
        }

        _players[player.PlayerUID] = player;
        entity.WatchedAttributes.RegisterModifiedListener(DeadAttribute, () => OnDeadAttributeChanged(player));
        if (IsDead(entity))
        {
            _dead.Add(player.PlayerUID);
        }
    }

    /// <summary>Forgets a player who left: nothing is pending for them, and a death is re-detected when they come back.</summary>
    /// <param name="player">The player.</param>
    public void OnDisconnect(IServerPlayer player)
    {
        string uid = player.PlayerUID;
        _players.Remove(uid);
        _dead.Remove(uid);
        _requested.Remove(uid);
        _revived.Remove(uid);
    }

    /// <summary>
    /// The engine's respawn event. It has already run the engine's own handler, so the player is
    /// either alive again (the spawn column was loaded) or still waiting for it.
    /// </summary>
    /// <param name="player">The player who asked to respawn.</param>
    public void OnRespawnRequested(IServerPlayer player)
    {
        string uid = player.PlayerUID;
        if (!IsCurrent(player) || !_dead.Contains(uid))
        {
            return;
        }

        if (_revived.Remove(uid))
        {
            _dead.Remove(uid);
            Move(player, 1);
        }
        else if (IsDead(player.Entity))
        {
            _requested.Add(uid);
        }
        else
        {
            _dead.Remove(uid); // alive and no longer marked as just revived: a stale death
        }
    }

    private static bool IsDead(EntityPlayer entity) => entity.WatchedAttributes.GetInt(DeadAttribute) != 0;

    private bool IsCurrent(IServerPlayer player) =>
        _players.TryGetValue(player.PlayerUID, out var known) && ReferenceEquals(known, player) && player.Entity is not null;

    /// <summary>The listener on the player's <c>entityDead</c> attribute: runs inside the engine's death and revive.</summary>
    private void OnDeadAttributeChanged(IServerPlayer player)
    {
        if (!IsCurrent(player))
        {
            return;
        }

        string uid = player.PlayerUID;
        if (IsDead(player.Entity))
        {
            _dead.Add(uid);
            _requested.Remove(uid);
            _revived.Remove(uid);
        }
        else if (_requested.Remove(uid))
        {
            // The engine's pending respawn teleport has applied and revived them. The revive is not
            // finished inside this listener (the entity's alive flag is set after the attribute).
            _dead.Remove(uid);
            _schedule(() => Move(player, 1), DeferMs);
        }
        else if (_dead.Contains(uid) && _revived.Add(uid))
        {
            // Revived with no request yet: either the engine's respawn, whose request event follows in
            // this same call, or a revive in place. Whatever no request has claimed by then is the latter.
            _schedule(() => ForgetInPlaceRevive(uid), DeferMs);
        }
    }

    private void ForgetInPlaceRevive(string uid)
    {
        if (_revived.Remove(uid))
        {
            _dead.Remove(uid);
        }
    }

    private void Move(IServerPlayer player, int attempt)
    {
        if (!IsCurrent(player) || IsDead(player.Entity))
        {
            return;
        }

        try
        {
            _respawn(player);
        }
        catch (Exception ex)
        {
            _logger?.Error("[Manifold] Moving {0} out of their dimension after a respawn failed (attempt {1}): {2}", player.PlayerName, attempt, ex);
            if (attempt < MaxAttempts)
            {
                _schedule(() => Move(player, attempt + 1), RetryMs);
            }
        }
    }
}
