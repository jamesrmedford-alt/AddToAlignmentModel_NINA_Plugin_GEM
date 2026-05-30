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

> **Update (Session 2):** item 2 is **confirmed** — live `SideOfPier` matched the
> prediction (east → `pierWest`, west → `pierEast`). Item 1 is **not yet
> resolved**: an 8-point model built and converged internally and the scope looked
> physically on-target, but the reported pose degraded after calibration so no
> trustworthy pointing number was obtained — and it remains open whether the model
> is genuinely good (measurement-only fault) or genuinely bad. See the Session 2
> section below for both interpretations and the pose-independent test protocol.

## How to reproduce

Build/install the plugin, connect the AVX via CPWI in EQ mode, then run
**Dump Telescope Capabilities (diagnostic)** from the sequencer (category
"Add To CPWI Alignment Model"). Output is written to the NINA log and a
timestamped `TelescopeCapabilities_*.txt` under `%LOCALAPPDATA%\NINA\Logs\`.
The `DestinationSideOfPier` prediction map is read-only and needs no slewing.

---

# Session 2 — live alignment-build test (2026-05-30)

First end-to-end attempt to **build a CPWI pointing model from scratch** in EQ
mode using only the plugin's plate-solve pushes (no CPWI star alignment), on an
initialized, sky-tracking AVX.

## Headline outcome

`AddAlignmentReference` works as the alignment *mechanism*: repeated
`SolveAddToAlignmentModel` pushes built an 8-point CPWI/PointXP model with a
sub-arc-minute internal fit (RMS ~56", Sensitivity 85 → 12), and the operator
saw the scope physically landing approximately on the requested targets. **But
whether the model actually produces good pointing is unconfirmed.** After the
model was built, the mount's ASCOM-reported position degraded (see "Pose
reporting degraded after calibration" below), so NINA's pointing-error readout
became unusable and we captured no trustworthy before/after number. Two
interpretations remain open; next session resolves them with a pose-independent
measurement.

## Confirmed this session

1. **Build-from-scratch works, no star align needed.** Starting from an empty
   CPWI alignment (mount merely indexed + time/location), plate-solve pushes
   registered as cal points and built a usable model. CPWI's manual star
   alignment is not a prerequisite — the plugin is the alignment method.
2. **Pier-side convention confirmed live.** With the mount tracking, observed
   `SideOfPier` matched the Session-1 `DestinationSideOfPier` prediction
   exactly: east of meridian → `pierWest`, west → `pierEast`. (Closes the
   Session-1 outstanding item #2.)
3. **CPWI requires points on both pier sides.** A 3-east/1-west model fit its
   points but extrapolated wildly (tens of degrees) to the sparse side. A
   balanced 4-east/4-west set converged. The grid loop must populate both sides.
4. **The plate-solve push is robust to bad initial slews.** Early west-side
   slews (before that side was constrained) missed badly, but the solver still
   identified the true position and pushed correct coordinates, so each push
   added a valid cal point and the model converged anyway. The build is
   self-correcting.
5. **Polar-alignment quality is a practical prerequisite.** Rough polar align
   (~2.6° via TPPA) produced a steep, hard-to-model pointing gradient; tightening
   to ~8' made the build behave. Note PointXP *inferred* a much larger axis error
   (8–29°) than the true ~8' while the model was underdetermined — it absorbs
   unmodelled residuals into the polar term until enough well-distributed points
   are present. Trust the TPPA measurement, not PointXP's inferred axis error, at
   low point counts.

## Bug found and fixed

**Epoch mismatch in `SolveDirectToMount` (fixed, PR #9, merged).** The
single-point push path sent J2000 plate-solve coordinates to the JNOW mount
without the `Transform(Epoch.JNOW)` its sibling methods apply — every reference
point ~0.4° off. Installing the fix did not, by itself, produce a clean run, so
epoch was not the dominant factor; but the fix is necessary for an accurate
model. A regression test pins the JNOW transform. Note: ~0.4° of consistent
epoch error fed into the model builder could itself have contributed to the
poorly-conditioned fit in the pre-fix points — another reason the next run
should be built entirely with the fixed DLL.

## Pose reporting degraded after calibration (why we have no clean number yet)

The mount's ASCOM-reported position was **accurate before any cal points** and
**garbage after the model was built** — the degradation tracks the calibration,
it is not a constant driver fault:

- **Before calibration:** the baseline plate solve showed an error of **1°01'** at
  an actual Dec of +34°. For that to be ~1°, the reported pose was ~Dec +34° —
  accurate. (Operator confirms: error distance read sensibly at this stage.)
- **After the 8-point model:** the two test solves showed error = **exactly
  `90° − (actual Dec)`**, i.e. reported Dec had collapsed to ~90°:

  | Test | Plate-solved Dec | Reported "Dec error" | 90° − solved Dec |
  |------|-----------------:|---------------------:|-----------------:|
  | West | +5.8°            | +84.3°               | +84.2°           |
  | East | +29.1°           | +60.9°               | +60.9°           |

**Likely mechanism:** CPWI reports position *through* its alignment model
(encoder → model → RA/Dec). With no model you get the raw, roughly-accurate
encoder/index position; once a poorly-conditioned model is loaded (PointXP
inferred an 8–29° polar error vs a true ~8' from TPPA), the forward transform
returns nonsense that here lands near the pole.

**Two interpretations remain open — unresolved this session:**

- **(A) Measurement-only:** the model is fine and pointing improved, but CPWI's
  ASCOM *position reporting* breaks once references are added, so only NINA's
  error readout is wrong. Supports: the scope was visibly ~on target; the push
  path uses solved coords, not the reported pose.
- **(B) The model is genuinely bad:** the underdetermined/imbalanced fit is poor
  (the implausible inferred polar error is a red flag) and the broken pose is a
  symptom; pointing to a new target could be genuinely off. The "scope looked
  about right" observation weakly argues against this but is not conclusive.

> Design consequence (holds under either interpretation): pointing-quality must be
> judged by comparing **plate-solved coordinates to the intended target**, never to
> `telescopeMediator.GetCurrentPosition()` on CPWI. Independently, the
> plugin's `GetCurrentLocation` ("add current position") path *reads* the reported
> pose and would therefore be unreliable on CPWI after calibration — flagged for
> the GEM work. The plate-solve push path is unaffected (it uses solved coords).

## Next-session protocol (clean Phase-2 measurement)

1. Polar align to a few arc-minutes (TPPA).
2. Re-confirm mount home/index; clear the pointing model.
3. Push a balanced grid: both pier sides, Dec spread ~+20° to +60°, ≥8 points
   (more is better for separating polar from other model terms).
4. Measure pointing the correct way: slew to fresh, untouched stars **inside the
   cal-point envelope**, and compare each plate-solved position to the
   **intended target** coordinates (or use a SlewAndCenter workflow). Do not read
   the mount-reported "Error distance".
5. Baseline to beat: the ~1° pre-alignment pointing seen at session start.
