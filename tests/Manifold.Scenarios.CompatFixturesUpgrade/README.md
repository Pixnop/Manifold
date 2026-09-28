# Manifold.Scenarios.CompatFixturesUpgrade

Builds `fixtures/upgrade-from-0.5.1.vcdbs` (played entirely with the published Manifold 0.5.1
release), the savegame fixture `tests/Manifold.Scenarios.Compat` loads. The sibling project
`tests/Manifold.Scenarios.CompatFixtures` builds the matching downgrade-direction fixture and
is the canonical doc for this whole fixture-generation story (why two builder projects instead
of one, why savegame fixtures at all, the shared fixture mod, and how to run both generators);
see it first. Not a test suite that runs in CI: a one-time, re-runnable generator, same as its
sibling.
