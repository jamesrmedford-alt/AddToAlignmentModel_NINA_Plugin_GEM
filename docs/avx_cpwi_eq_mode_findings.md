# AVX / CPWI hardware findings — German-equatorial (EQ) mode

Hardware verification of the behaviors the German-equatorial (GEM) support work
depends on. Captured from a **Celestron AVX** connected through **CPWI's ASCOM
driver** to NINA, using the plugin's `Dump Telescope Capabilities (diagnostic)`
sequence item. This document is a primary deliverable: it records what the mount
actually reports, so the GEM behavior changes can be designed against verified
facts rather than assumptions.

## Test conditions and caveats

- **Date (UTC):** 2026-05-27, ~01:37–01:53.
- **Mount / driver:** AVX via `CPWI_ASCOM_Telescope`, `AlignmentMode = GermanPolar`.
- **Site:** lat 39.2347°, lon −76.8359°.
- **Mount state:** powered on but **not initialized / not at index** — could not be
  slewed. All probes were **read-only**; no slews and no alignment references were
  written.
- **Source files:** `Dump Telescope Capabilities` output, `TelescopeCapabilities_20260527_013752.txt`
  and `..._015331.txt` (NINA Logs folder).

Because the mount was uninitialized, treat the **Pose** and the live `SideOfPier`
reading as unreliable (the reported Dec ≈ 90° / Az = 0° is CPWI's default, not a
real pointing). The **driver/capability** facts below are not position-dependent
and are trustworthy; the one item flagged "predicted" still needs a physical
confirmation (see Outstanding).

## Findings

### 1. `Telescope:AddAlignmentReference` is available in EQ mode  — VERIFIED (gating)

This was the gating unknown (Celestron-undocumented; unverified in EQ). Both signals confirm it:

- It appears in the driver's `SupportedActions` (the only custom action listed):
  `Telescope:AddAlignmentReference`.
- Probing it with a deliberately invalid payload returned
  `ASCOM.DriverException: "Index was outside the bounds of the array"` — **not**
  `ActionNotImplementedException`. So the action exists and the driver actively
  *parsed* the argument (it split the string and indexed out of bounds).

The parse failure on a delimiter-less payload tells us the expected format is the
colon-delimited `RA:Dec` the plugin already sends.

> Maps to CLAUDE.md GEM finding #4. The plugin's core mechanism works in EQ mode.
> Not yet confirmed: that a *valid* push measurably improves pointing (Phase 2).

### 2. Coordinate epoch is JNOW  — VERIFIED

`EquatorialSystem = JNOW`. The plugin already `Transform(Epoch.JNOW)`s solved
coordinates before pushing, so the payload epoch is correct for EQ with no change.

### 3. Pier side is computable and the driver agrees  — VERIFIED (prediction)

`DestinationSideOfPier` is an ASCOM **prediction** (no movement) and NINA exposes
it on `ITelescopeMediator`. Sampled across hour angle at Dec = 45° (LST 13.0787 h),
CPWI returned a clean, deterministic mapping:

| Hour angle | Side of meridian | CPWI `DestinationSideOfPier` |
|-----------:|:-----------------|:-----------------------------|
| −6.0 h     | East (rising)    | `pierWest`                   |
| −4.0 h     | East             | `pierWest`                   |
| −2.0 h     | East             | `pierWest`                   |
| −0.5 h     | East             | `pierWest`                   |
| +0.5 h     | West (setting)   | `pierEast`                   |
| +2.0 h     | West             | `pierEast`                   |
| +4.0 h     | West             | `pierEast`                   |
| +6.0 h     | West             | `pierEast`                   |

Key points:
- The mapping is **deterministic** and follows the standard ASCOM GEM convention:
  **east of the meridian (HA < 0) → `pierWest`; west (HA ≥ 0) → `pierEast`**, with
  the flip at the meridian (HA = 0).
- It is **computed geometrically** — it worked while the mount was uninitialized,
  so it is usable for *planning* a grid before the mount is aligned.
- The driver's prediction **agrees exactly with the hour-angle sign**, giving two
  consistent sources of pier side: `DestinationSideOfPier(target)` (authoritative)
  and `HA = LST − RA` (driver-independent fallback).
- `CanSetPierSide = False`: we cannot *command* a side, but we don't need to — the
  mount auto-flips and `DestinationSideOfPier` tells us where it will land.

> Maps to CLAUDE.md GEM finding #3. This is the basis for partitioning the
> alignment grid by pier side so the mount makes a single deliberate meridian flip.
> The pure pier-side logic derived from this is implemented and unit-tested in
> `ADP_Tools` (`HourAngle`, `SideOfMeridian`, `OrderByMeridianSide`).

### Capability summary (EQ mode)

| Property | Value |
|----------|-------|
| `AlignmentMode` | `GermanPolar` |
| `EquatorialSystem` | `JNOW` |
| `CanSlew` / `CanPark` / `CanSetPark` | True / True / True |
| `CanFindHome` | **False** |
| `CanSetPierSide` | **False** |
| `CanMovePrimaryAxis` / `CanMoveSecondaryAxis` | True / True |
| `CanPulseGuide` / `CanSetTrackingEnabled` | True / True |
| `CanSetDeclinationRate` / `CanSetRightAscensionRate` | False / False |
| `TrackingModes` | Sidereal, Lunar, Solar, Stopped |
| Axis rates (primary / secondary) | 0–4 °/s |
| Guide rate (RA / Dec) | ~7.52 arcsec/s |
| `HasUnknownEpoch` | False |
| `TimeToMeridianFlip` | 24 h (degenerate — reported at the pole; not meaningful here) |

Notes: in `GermanPolar`, **Sidereal** is the operative EQ tracking mode (there is
no separate "EQ" mode in the list). `TimeToMeridianFlip` was not meaningful from
the uninitialized pole position.

## Design implications for the GEM work

- **Reference push (finding #4):** keep the existing `Action("Telescope:AddAlignmentReference", "RA:Dec")`
  with JNOW coordinates — confirmed correct for EQ.
- **Meridian / pier handling (finding #3):** partition the alignment grid by
  predicted pier side and order points so the mount flips at most once. Use
  `DestinationSideOfPier` as the authoritative source, with `HA = LST − RA` as the
  equivalent fallback. Do **not** rely on commanding pier side (`CanSetPierSide = False`).
- **Validation (finding #1):** `GermanPolar` is reported as expected; the
  `ValidateConnections` block on non-AltAz (currently bypassed only by the
  `EnableEquatorialMounts` flag) can be softened once a warning channel is decided.
- **Home pre-position (finding #2):** a polar-aligned GEM at home points near the
  celestial pole, so `ReadyToStart`'s horizon/due-north assumption must branch on
  mount mode. (Not yet implemented.)

## Outstanding verification (needs a live, initialized mount)

1. **Phase 2 — empirical reference push:** with `EnableEquatorialMounts` set, run one
   `SolveAddToAlignmentModel` on a real star in EQ mode and confirm pointing
   accuracy measurably improves. This is the only remaining thing that could
   surprise us about `AddAlignmentReference` in EQ.
2. **Physical pier-side confirmation:** slew to a target east of the meridian, then
   west, and confirm the *actual* `SideOfPier` matches what `DestinationSideOfPier`
   predicted (above was a prediction from an uninitialized mount).

## How to reproduce

Build/install the plugin, connect the AVX via CPWI in EQ mode, then run
**Dump Telescope Capabilities (diagnostic)** from the sequencer (category
"Add To CPWI Alignment Model"). Output is written to the NINA log and a
timestamped `TelescopeCapabilities_*.txt` under `%LOCALAPPDATA%\NINA\Logs\`.
The `DestinationSideOfPier` prediction map is read-only and needs no slewing.
