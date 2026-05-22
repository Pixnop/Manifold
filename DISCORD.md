# Discord presentation post - Manifold project

Single post presenting the three mods. Local helper (not committed). No emoji / no em-dash
(add emoji yourself for Discord if you want). Paste and adapt as needed.

---

## The Manifold project

A small family of mods for Vintage Story 1.21+ built around one idea: make **custom dimensions**
a first-class, easy thing for modders, instead of a fragile hack.

### Why the name "Manifold"

In mathematics, a **manifold** is a space made of many local pieces (dimensions) glued into one
coherent whole, and its structure is described by an **atlas of charts** (maps). The name also just
means "many and various". That is exactly what this project does: it manages many dimensions as one
system. (It is also a nod to a mechanical manifold, the part that routes a single flow out to
several outlets, like routing players out to several worlds.)

The math metaphor even names the companion map mod for us (see below).

---

## 1. Manifold - the library

**The problem:** Vintage Story has a dimension system, but driving it from a mod (creating a
dimension, generating its terrain, moving players in and out, keeping it across restarts) is
low-level and easy to get wrong. There was no clean, shared way to do it.

**Manifold** is a library mod that exposes a clean public API for exactly that. It adds no content
of its own; you install it because another mod depends on it.

What it gives mod authors:
- **Custom dimensions** - declare persistent or ephemeral dimensions, at boot or at runtime.
- **Worldgen** - implement a strategy; Manifold drives generation. Two modes per dimension:
  bounded (a fixed region around the arrival point) or opt-in **streaming** (chunks generate on
  demand as players move, no fixed edge).
- **Player transit** - one call moves a player between dimensions, with cancellable enter/leave
  events and opt-in helpers (a portal block base class, a chat-command builder).
- **Travel policy** - per dimension landing position, forced game mode, and more.
- **Persistence and safety** - dimensions, generated chunks and per-player positions survive
  restarts. A dimension whose owning mod is removed is quarantined: its chunks are kept, never
  corrupted.
- **Zero Harmony patches** - built entirely on the public API.

Links:
- [Mod DB](<https://mods.vintagestory.at/manifold>)
- [Source and issues](<https://github.com/Pixnop/Manifold>)
- [API docs](<https://pixnop.github.io/Manifold/>)
- NuGet (for developers): `Pixnop.Manifold` -> `dotnet add package Pixnop.Manifold`

Current version: 0.2.0.

---

## 2. Manifold Sample - the template

A small demo and template mod showing how to build custom dimensions with Manifold. **MIT
licensed - reuse the code freely** as a starting point for your own dimension mod.

It registers void, flat and streaming dimensions and the `/voiddim`, `/flatdim`, `/streamdim` and
`/overworlddim` commands, plus a simple portal block. Read it as a copy-paste starting point.

Links:
- [Mod DB](<https://mods.vintagestory.at/manifold>)
- [Source](<https://github.com/Pixnop/Manifold/tree/main/samples/ManifoldSample>)

Requires Manifold.

---

## 3. Chart - the map companion (planned)

Custom dimensions work, but the in-game map still shows the overworld no matter which dimension you
are in (the vanilla map system is hardcoded to one dimension and is closed-source). The plan is a
separate companion mod, **Chart**, that makes the map **dimension-aware**, rendering the dimension
you are actually in.

**Why "Chart":** in the math behind the name Manifold, a manifold is described by an *atlas of charts*,
and a chart is literally a map. So the dimension library is the manifold, and the map mod is one of
its charts.

Status: not started yet, it is a sizable piece (a custom map layer). Tracked at
[issue #7](<https://github.com/Pixnop/Manifold/issues/7>) - updates will be posted here.

---

## Support and feedback

Bug reports: a GitHub issue is best (with your server log), or post here and I will look. Feature
ideas and questions welcome. The whole thing is open source (MIT) on GitHub.
