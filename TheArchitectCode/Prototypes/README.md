# Corrupted Challenger prototypes

## Corruption effect lab (unselected)

Run from the repository root:

```sh
python3 -m http.server 8873 --bind 127.0.0.1 --directory TheArchitectCode/Prototypes
```

Open <http://127.0.0.1:8873/corruption-effects.html?variant=A>.
This throwaway WebGL study compares three material directions against the
existing `(0.70, 0.40, 0.90)` tint, or against original sprite colors:

- **A / Voidfire:** irregular silhouette flame, bright contour, rising embers.
- **B / Fractured Echo:** displaced spectral copies and a slowly moving slice.
- **C / Architect's Binding:** back/front orbiting seals and a ground sigil.

Use the bottom switcher or left/right arrow keys outside controls. Each direction
has the same five original character stand-ins, character selection, intensity,
speed, size, lighting, facing, and pause controls. Reduced-motion preferences
start the animation paused. A transparent PNG/WebP can be loaded as a sixth
character for the main comparison; the five-character gallery stays unchanged.
Images remain in browser memory and are not transmitted or persisted.
Live state and Godot implementation considerations are in the expandable panel.

These are real fragment shaders over flattened RGBA sprites, not actual game
assets or Spine animations. They do not establish in-game rendering cost or
compatibility with arbitrary character rigs. Silhouette effects would likely
need a padded composed-body render target to avoid Spine attachment seams;
Binding can instead use body-relative back/front effect nodes. Keep health,
intents, orbs and pets outside the body effect.

No direction has been approved. Production `CorruptedChallenger` is unchanged.
Keep this study on `philip-scott-corrupted-effect-redesign`; only integrate a
chosen treatment after evaluating it on animated game characters.

## Corrupted Challenger telegraph prototype

Approved by the human on 2026-09-07 for
[Prototype the Corrupted Challenger telegraph](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/5),
under [Build the Architect Act 4 vertical slice](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/2).

## Artifact

Open [challenger-telegraph.html](challenger-telegraph.html) directly, or serve this
directory with `python3 -m http.server 8765 --bind 127.0.0.1`.
Choose `?variant=C` for the approved presentation. A and B remain alternatives
on this throwaway branch; the original iterations are preserved in git.
There is no game integration or production UI/combat code.

The recreated arena follows the supplied rough mockup: player on the left,
the saved character's normal combat model with generic corruption in the middle,
and the waiting Architect on the right. All artwork and card faces here are
placeholders; authored effects are not real game card definitions.

## Settled presentation

- Use C's compact, horizontally scrollable miniature-card row above the Challenger.
  Cards always play **right to left**. The rightmost playable card is numbered 1.
  Overflow initially exposes the right end; scroll left for later cards. Keep the
  ordered full-card inspection affordance rather than widening over the arena.
- Preserve drawn-card positions. Cards that will not play remain grayed out,
  without play-order numbers or active intent icons. Number only playable cards.
  Do not move unplayed cards to a separate trailing group.
- Put each card's attack number and relevant intent icons directly beneath that
  card. **No aggregate summary**, including in full-turn inspection. Multi-hit
  attacks show damage per hit and hit count; mixed-effect cards show every relevant
  icon under the same card. Block and buffs refer to the Challenger; offensive
  debuffs refer to the player in these examples.
- X uses the Challenger's remaining energy at the time that card acts. A first
  playable X-cost card with 3 energy therefore previews X=3. Card-specific effects
  determine what that value does; X is not universally a hit count.
- Hover/focus shows only the enlarged card and its ordinary keyword tooltips.
  No custom action explanation, unplayable-reason paragraph, or simulation
  breakdown. Click pins inspection; Escape or Clear dismisses it.
- Unsupported mechanics remain visible as explicit neutral-fallback slots, with
  a question-mark/no-effect intent rather than their unsupported original effect.
  Distinguish these from gray cards that will not play.
- The Architect has **no health bar during Phase 1**. Show its health bar only
  when Phase 2 begins. Preserve the Challenger's normal lower health/status area.
- Per-card intent values reflect earlier planned modifiers; the enlarged card
  retains its card text. This prototype illustrates that distinction with
  Strength before a multi-hit attack.

## Human review

The human selected C over the local-peek and side-dossier layouts, then removed
the aggregate summary and Architect's Phase 1 health bar. They correctly read
the mixed example as Sapping Cut dealing 7 and applying 1 Weak, Defend giving
the Challenger 5 block, and Twin Cut dealing two hits of 4.

The X-cost example prompted the remaining-energy rule and gray unplayed cards.
The human chose to retain original drawn positions, then specified right-to-left
play and standard card/keyword-only hover. After those revisions they selected
"Approve this presentation" in response to confirmation covering ordering, hover,
overflow, and gray unplayed cards.

The artifact includes baseline, multi-hit, preceding buff, non-attack, mixed,
X/unplayed, unsupported, and nine-card overflow turns, plus arena-width controls
at 1280, 960, and 720 pixels. Approval is a presentation decision, not evidence
of in-game testing or a guarantee for arbitrary UI scales or character models.

## Remaining simulation and integration questions

Carry these into
[Define the Corrupted Challenger simulation](https://github.com/Philip-Scott/slay-the-spire-the-architect/issues/9);
this prototype does not resolve them:

- How selection decides which drawn cards are playable while preserving the
  approved right-to-left presentation; the X fixture is authored, not a planner.
- Exact energy cost and timing of neutral fallbacks, and X=0 behavior.
- Generated cards, changing eligibility, and when plans/intent values refresh.

Implementation should use native card previews and keyword tooltips, not port
this HTML. Arbitrary character bounds, UI scaling, and Phase 2 visibility wiring
still require in-game implementation and acceptance coverage.
