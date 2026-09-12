# Corrupted Player performance issue drafts

Repository: `Philip-Scott/slay-the-spire-the-architect`
Baseline: `77f873c`

These drafts have not been published to GitHub because authenticated issue-write
access was unavailable. The report was laptop slowdown with three Corrupted
Players visible in multiplayer. Source-level costs are distinguished from measured
frame-time costs below.

## 1. Stop refreshing the entire Corrupted Player HUD every frame

**Finding:** `CorruptedPlayerTelegraph._Process` called
`NativeCorruptedPlayer.Show` every frame, allocating hand entries and updating
every native card visual and pile label even while combat was idle.

**Implemented:** use the same deferred `CombatStateTracker.CombatStateChanged`
event as the native hand UI, retain actor-specific change notifications, and only
resubmit changed pile counts. Unsubscribe on stop and tree exit.

**Acceptance:** idle hands and open hover cards do not refresh their contents.
Changes to damage, block, costs, piles, targets, powers, and forged cards still
refresh previews. Previewing preserves combat state and RNG.

## 2. Preserve empty-hand placeholder reuse

**Correction:** the original investigation misread the reuse condition. Existing
code already reuses the empty-hand placeholder; this is not an open performance
bug and should not be published as one.

**Implemented:** regression coverage for repeated empty plans retaining the same
node, and for transitioning back to a populated hand. No production fix needed.

## 3. Retain binding geometry instead of rebuilding every frame

**Finding:** the front and back bindings rebuilt rings, seals, and their glow
strokes on every frame, including for corrupted Osty.

**Implemented:** retain the drawing commands for ring and seal nodes. Animate
their transforms and opacity, rebuilding geometry only when bounds change.
Only the twelve moving floor ticks still submit drawing commands every frame.
Animation cadence and the body shader are unchanged.

**Acceptance:** idle animation does not rebuild stroke geometry. Front/back
ordering, inherited fades, scaling, bounds changes, pet visuals, and cleanup
remain correct. Drawing API submissions are not GPU draw-call measurements.

## 4. Cache compositor updates and investigate remaining GPU cost

**Finding:** every body repeatedly calculated bounds and submitted margins,
body-rectangle uniforms, strength, and modulation. CanvasGroup compositing and
the screen-texture shader are additional GPU suspects, not measured bottlenecks.

**Implemented:** cache bounds transforms and suppress unchanged geometry,
strength, and modulation submissions. Keep the elapsed-time uniform updating
so animation retains its existing pause and timing behavior.

**Acceptance:** unchanged geometry is not resubmitted; native bounds, fades,
scales, and death reparenting still work without changing original Spine
materials or capturing unrelated UI.

**Remaining:** profile the retained compositor on affected laptop GPUs. These
changes do not establish a GPU frame-time improvement or remove its backbuffer
and texture-sampling costs.

## 5. Share and invalidate multiplayer telegraph layout

**Finding:** every panel independently scanned enemies and resolved their nodes
every frame to calculate spacing.

**Implemented:** share a combat-scoped layout cache. Resolve membership on
changes, sample party positions once per frame, and recalculate spacing only when
positions or membership change. Reposition panels when their anchor, spacing, or
viewport changes. Release subscriptions and references when panels stop/exit.

**Acceptance:** idle panels perform no layout rebuilds; movement, viewport
changes, deaths, room cleanup, compact party panels, and hover placement remain
correct.
