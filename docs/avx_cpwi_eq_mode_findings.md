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
> physically on-target, but the NINA logs show NINA's view of the mount's RA/Dec
> was frozen at a near-pole value for the entire test phase — not lag, frozen —
> while the user was slewing across the sky via CPWI's UI. Cause is now
> identified: **the CPWI ASCOM driver doesn't reflect CPWI-UI-initiated motion in
> its reported position properties**, only motion that comes through ASCOM itself.
> See the Session 2 section below for the log evidence and the Sync-per-push
> design that follows.

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
whether the model actually produces good pointing is unconfirmed**, because
NINA's pointing-error readout was unusable: the NINA logs show NINA's reported
mount position froze at a near-pole value for the entire test phase while the
camera was visibly slewing across the sky via CPWI's UI. The model points
themselves are correct (the plate-solve push doesn't depend on reported pose),
but the comparison metric NINA showed was meaningless. The cause is now
identified — see "Mount-reported position vs reality" — and a concrete plugin
mitigation falls out of it: see "Deconflicting the mixed-driver workflow".

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

## Mount-reported position vs reality (what the NINA logs revealed)

This was initially framed as "pose degraded after calibration." With the NINA
logs in hand, the picture is sharper and the cause is different. The session
workflow was: **user slews to each target via CPWI's own UI → triggers
`SolveAddToAlignmentModel` in NINA, which plate-solves and pushes the result.**
The plugin itself never issued a slew. That detail is what the log evidence
turns on.

### Smoking-gun log evidence

Across **fourteen minutes** of test plate-solves (23:02–23:16), every single
solve used **identical** "Reference Coordinates" — the near-pole value from
much earlier in the session — while the *plate-solved* (actual) positions
ranged across most of the visible sky:

| Time     | Reference Coords (mount-reported) | Plate-solved (actual)         |
|----------|-----------------------------------|-------------------------------|
| 23:02:37 | RA 00:38:14 / Dec +89°51'41"      | RA 18:59:33 / Dec +32°41'     |
| 23:03:47 | RA 00:38:14 / Dec +89°51'41"      | RA 19:05:45 / Dec +13°54'     |
| 23:04:52 | RA 00:38:14 / Dec +89°51'41"      | RA 16:41:51 / Dec +31°35'     |
| 23:08:40 | RA 00:38:14 / Dec +89°51'41"      | RA 12:33:09 / Dec +28°21'     |
| 23:10:22 | RA 00:38:14 / Dec +89°51'41"      | RA 10:50:05 / Dec +20°58'     |
| 23:14:55 | RA 00:38:14 / Dec +89°51'41"      | RA 11:31:57 / Dec +05°48' ← W test |

The mount was demonstrably slewing to eight different sky positions via CPWI's
UI, and NINA's view of the mount's RA/Dec did not move at all. That is not
latency; that is **the ASCOM-reported position not reflecting CPWI-UI motion
at all.**

### The actual bug

**CPWI's ASCOM driver does not update its reported `RightAscension` /
`Declination` properties for motion that originates from CPWI's own UI (sky
map, GoTo, manual nudges).** Only motion initiated *through ASCOM*
(`SlewToCoordinates`, `SyncToCoordinates`, `MoveAxis`) updates the reported
pose. The driver appears to cache the last ASCOM-commanded position rather
than query CPWI's live encoder/model state on each property read.

That accounts for everything we saw:
- Pre-cal "Error distance ~1°": NINA's last slew/sync target matched where the
  camera was, so reported pose was accurate.
- The 22:20:12 Sync correctly anchored reported pose to Dec +19°45'30".
- TPPA-era operations moved reported pose to near-pole (`Reference Coordinates
  RA 00:38 / Dec 89°51'` originated here).
- From then on, **every CPWI-UI slew was invisible to ASCOM**, and the
  reference froze at the near-pole value for the rest of the night.
- The 84.3°/60.9° test "errors" were exactly `90° − (actual Dec)` because they
  were computed against that frozen near-pole reference.

### External corroboration

Forum reports describe related CPWI/ASCOM behaviors that are consistent with
(and reinforce) what the logs show, even though none is a clean line-for-line
match of "CPWI-UI motion is invisible to ASCOM":

- *"CPWI once it creates the initial alignment model, it will not accept Sync
  information to improve the initial alignment model."* — community report
  surfaced via the [Cloudy Nights CPWI threads](https://www.cloudynights.com/forums/topic/972801-cpwi-ascom-driver-problems/).
  Critically, this is consistent with the logs: ASCOM `Sync` still updated
  *the reported position* (the 22:20:12 and 23:22:34 syncs both took), but it
  appears it does **not** feed CPWI's alignment model after the model exists —
  which is the right division of labor for our purposes (`AddAlignmentReference`
  is the channel for model contributions; `Sync` is the channel for reporting).
- *"CPWI doesn't respond to manually slewing N or E, but does respond to W and
  S when using other ASCOM apps."* — same community thread; a separate
  direction-specific quirk, but evidence the driver has known fidelity issues
  in propagating between CPWI and ASCOM clients.
- NINA's own troubleshooting page notes ASCOM drivers sometimes [fail to
  receive updates from CPWI](https://nighttime-imaging.eu/docs/master/site/troubleshooting/ascom_connection_issues/),
  and recommends the ASCOM Device Hub as a workaround/bridge for reliability.
- Independent reports of CPWI's ASCOM driver having sync timeout and other
  state-propagation issues are catalogued across the [SharpCap CPWI sync
  threads](https://forums.sharpcap.co.uk/viewtopic.php?t=6432) and
  [APT/CPWI position threads](https://aptforum.com/phpbb/viewtopic.php?t=3929).

None of these is a published Celestron acknowledgement; they're a coherent body
of user-observed CPWI/ASCOM behaviors that align with what the log evidence
proves directly.

### Design consequences

- **Pointing quality must be judged by plate-solved-coords vs intended-target**,
  not the mount-reported "Error distance". Holds regardless of which sub-cause
  is at play; on CPWI this is now an architectural certainty rather than a
  cautionary note.
- The plugin's **`GetCurrentLocation` path (`telescopeMediator.GetCurrentPosition()`)
  is unreliable on CPWI** whenever the user has driven the scope from CPWI's
  UI. The plate-solve push paths are unaffected (they use solved coordinates).
- The mixed-driver workflow the maintainer actually wants — **CPWI's UI for
  navigation + plugin for model contribution** — is otherwise fully supported,
  but it requires the plugin to compensate for the driver's missing CPWI-UI →
  ASCOM propagation. Without a fix, every CPWI-UI slew leaves NINA's view of
  the mount stale until the next ASCOM-initiated `Sync`, `Slew`, or `MoveAxis`.

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

## Deconflicting the mixed-driver workflow (CPWI UI + plugin)

The maintainer's intended workflow is: **use CPWI's UI to navigate the scope
(its sky map, GoTo, etc.), and use the plugin to contribute alignment points to
CPWI's model as observations accumulate.** Both clients writing to the mount's
state in parallel is the design goal, not a misuse. The constraint that comes
out of the log analysis above is: **NINA's view of the mount must be refreshed
through ASCOM whenever the user has driven the scope from CPWI's UI**, because
the driver doesn't do that propagation for us.

There is exactly one cheap mechanism for that refresh, and it's already in
ASCOM and NexStar: **`Sync`**. Per the [NexStar Communication Protocol](1154108406_nexstarcommprot.pdf)
and the ASCOM ITelescope spec, `Sync` is documented as a single-anchor
operation that "centers a known object … and improves pointing accuracy" — i.e.
it tells the mount "you are pointing here," and from then on the reported
RA/Dec is computed relative to that anchor. The community report that *CPWI
does not let `Sync` modify an existing alignment model* (see corroboration
above) doesn't conflict with using it here: it still updates the
reported-position layer, which is the layer we need to refresh.

### Recommended plugin behavior: Sync + AddAlignmentReference per push

For each `SolveAddToAlignmentModel`:

1. Plate-solve the image (truth).
2. `Action("Telescope:AddAlignmentReference", "RA:Dec")` with the JNOW-transformed
   solved coordinates → contributes to CPWI's PointXP model.
3. `telescopeMediator.Sync(solvedCoordinates)` with the same JNOW coordinates →
   refreshes the ASCOM-reported position so NINA's view of the mount matches the
   camera's actual position.

The two operations are *complementary*, not competing. `AddAlignmentReference`
is the model-contribution channel (it's literally what the custom action exists
for). `Sync` is the reported-position channel (forum reports suggest it no
longer feeds CPWI's model once one exists, which is exactly what we want — no
double-counting). Together they restore the invariant that NINA's
`GetCurrentPosition()` reflects reality after every push, even if the user
slews between pushes via CPWI's UI.

Cost: one extra ASCOM call per push (~milliseconds). No change to the model
building behavior. No change to how the user navigates the scope.

### Remaining open questions (to settle next session, before merging the code change)

The Sync-per-push design is supported by the log evidence and by the available
external corroboration, but two clean empirical confirmations are worth
collecting before locking it in:

1. **The 30-second driver test** — note NINA's RA/Dec, slew significantly via
   CPWI's UI, wait ~5 s, re-check NINA. Predicted result: **unchanged**. If so,
   the driver-doesn't-propagate-CPWI-UI-motion conclusion is confirmed directly,
   not just inferred from the log. If NINA's view does update, then the
   propagation works in some cases and we need to characterize when.
2. **Sync recovers a stuck pose** — when the pose is observed stale, issue an
   ASCOM `Sync` to known coordinates (NINA's "Slew and Center with sync," or
   Device Hub manually), and confirm the reported pose updates. The 23:22:34
   Sync in the b66ad4b1 log already shows this works in principle (it updated
   from a Dec +89°58' "from" to the user-supplied target), but a deliberate
   isolated test makes it unambiguous.

If both confirm, the Sync-per-push change is unambiguously the right fix and
ready to implement. If either surprises us, the deeper ASCOM tracing path below
narrows it down further.

### Deeper ASCOM-layer debugging (only if the above surprises)

If for any reason the Sync+Add pattern doesn't restore NINA's view of the
mount, instrument the layers independently to localize the failure:

1. **ASCOM driver trace log.** In the CPWI telescope driver's Setup dialog
   (ASCOM chooser → Properties), enable Trace/Logging. Output goes to
   `Documents\ASCOM\Logs\` (or `%LOCALAPPDATA%\ASCOM\Logs\`). With it on, push
   one reference, Sync, and re-poll RA/Dec; the trace shows exactly what
   `get_RightAscension`/`get_Declination` returned each call and whether `Sync`
   was actually applied.
2. **ASCOM Device Hub as a spy.** Connect NINA to `ASCOM.DeviceHub.Telescope`,
   and Device Hub to the CPWI driver. Device Hub displays live property values
   and lets you invoke individual ASCOM calls — `Action`, `Sync`,
   `SlewToCoordinates` — in isolation, without the plugin in the loop.
3. **CPWI application log** under `%LOCALAPPDATA%\Celestron\CPWI\` — useful if
   the driver trace shows CPWI feeding the driver wrong values, vs the driver
   inventing them.
4. **ASCOM Conform Tool** — validates the driver against the ITelescope spec;
   reserve for cases where position-reporting isn't the only oddity.
