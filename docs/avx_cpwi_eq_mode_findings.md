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

Two independent windows in the log show the same thing. The build-phase
evidence is the stronger of the two — it predates any Park, so it can't be
attributed to post-Park state corruption — and is shown first.

**Build phase (22:19–22:31), no Park in the window:**

| Time     | Reference Coords (mount-reported) | Plate-solved (actual)        |
|----------|-----------------------------------|------------------------------|
| 22:19:04 | RA 09:43:43 / Dec +34°11'21"      | RA 11:21:38 / Dec +19°53'    |
| 22:19:53 | RA 09:43:43 / Dec +34°11'21"      | RA 11:21:37 / Dec +19°54'    |
| 22:20:57 | RA 11:41:53 / Dec +19°54'16"      | RA 11:21:36 / Dec +19°54'    |
| 22:22:06 | RA 11:41:53 / Dec +19°54'16"      | RA 10:33:03 / Dec +11°42'    |
| 22:23:10 | RA 11:41:53 / Dec +19°54'16"      | RA 08:56:13 / Dec +18°08'    |
| 22:28:28 | RA 11:41:53 / Dec +19°54'16"      | RA 19:27:01 / Dec +36°23'    |
| 22:30:38 | RA 11:41:53 / Dec +19°54'16"      | RA 19:27:04 / Dec +36°24'    |

The reference coordinates froze at `RA 11:41:53 / Dec +19°54'16"` from 22:20 to
22:30 — across six successful solves whose actual positions ranged from RA
08:56 to RA 19:27 (over half the sky, including a meridian crossing). Only
ASCOM-initiated motion changed the reference: the 22:19→22:20 transition is the
22:20:12 `Sync`; the subsequent frozen reference is what CPWI-UI navigation
between solves looked like.

**Test phase (23:02–23:16), after TPPA + Park:**

| Time     | Reference Coords (mount-reported) | Plate-solved (actual)         |
|----------|-----------------------------------|-------------------------------|
| 23:02:37 | RA 00:38:14 / Dec +89°51'41"      | RA 18:59:33 / Dec +32°41'     |
| 23:03:47 | RA 00:38:14 / Dec +89°51'41"      | RA 19:05:45 / Dec +13°54'     |
| 23:04:52 | RA 00:38:14 / Dec +89°51'41"      | RA 16:41:51 / Dec +31°35'     |
| 23:08:40 | RA 00:38:14 / Dec +89°51'41"      | RA 12:33:09 / Dec +28°21'     |
| 23:10:22 | RA 00:38:14 / Dec +89°51'41"      | RA 10:50:05 / Dec +20°58'     |
| 23:14:55 | RA 00:38:14 / Dec +89°51'41"      | RA 11:31:57 / Dec +05°48' ← W test |

In each window, the mount was demonstrably slewing to many different sky
positions via CPWI's UI, and NINA's view of the mount's RA/Dec did not move.
That is not latency; that is **the ASCOM-reported position not reflecting
CPWI-UI motion at all.**

**Independent corroborating signal: `SideOfPier` *does* update live.** Between
the two read-only `Dump Telescope Capabilities` runs in Session 3 (six minutes
apart, RA/Dec frozen at the same stale value), `SideOfPier` flipped from
`pierWest` to `pierEast` — i.e. the property changed even while the
position properties did not. That cleanly isolates the bug to the position
read path and is also why the pier-side logic in PR #6 can be trusted to use
the live driver value.

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

An adversarial review of this section found that **no independent published
report of this specific CPWI-UI → ASCOM propagation gap was located** — the
relevant Cloudy Nights, SharpCap, and APT forum pages return HTTP 403 to
automated fetches, and web-search snippets surface only *adjacent* CPWI/ASCOM
issues, not the exact bug we observed. The log evidence above stands on its
own; the items below are the most relevant adjacent reports plus the primary
spec citations that ground the design.

- **Celestron's own release notes** acknowledge the
  `Telescope:AddAlignmentReference` custom action exists — surfaced via web
  search of CPWI release notes referencing
  "Added ASCOM.Action(Telescope:AddAlignmentReference,ra:dec)". Establishes
  that the custom-action channel is real and Celestron-blessed.
- **[NexStar Communication Protocol](1154108406_nexstarcommprot.pdf)** (in
  `docs/`) defines `Sync` (`S`/`s` serial commands) as the documented
  single-anchor "you are pointing here" operation that future Get-Position
  results are computed relative to. Not corroboration of the bug; corroboration
  that `Sync` is the correct mechanism for refreshing reported position once
  it is stale.
- **ASCOM `ITelescopeV3.SideOfPier` spec** defines `pierEast` as the *normal*
  pointing state (mount on east side of pier, OTA pointing west, target
  west of meridian) and `pierWest` as the *through-the-pole* state (target
  east of meridian). The mapping we observed on CPWI matches this exactly, so
  any standards-compliant client can consume it directly. (ASCOM Help URL
  cannot be fetched here; cited as the published spec.)
- **Plausible adjacent CPWI/ASCOM issues** surfaced by general web search but
  not directly verified in this session: reports of `Sync` timeouts on CPWI's
  ASCOM driver, of `SyncToAltAz` not being implemented, of CPWI's mini-slew
  window sometimes needing to be active for Stellarium slews to work. These
  paint a coherent picture of a driver with known state-propagation flakiness
  but none specifically demonstrates the CPWI-UI → ASCOM propagation gap.

Worth noting why this gap might be under-reported: the standard NINA + Celestron
workflow has the user *align in CPWI first, then drive everything from NINA*.
Under that pattern, all motion originates in ASCOM and the bug never surfaces.
The mixed-driver workflow this plugin is designed for (CPWI UI for navigation +
plugin for model contribution) appears to be unusual enough that the gap has
not been catalogued.

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
for). `Sync` is the reported-position channel. The NexStar protocol documents
`Sync` as "future GOTO or Get Position commands … use coordinates relative to
the Sync'd position," not as a model contribution; so the two channels are
expected to be independent. Together they restore the invariant that NINA's
`GetCurrentPosition()` reflects reality after every push, even if the user
slews between pushes via CPWI's UI.

Cost: one extra ASCOM call per push (~milliseconds). No change to model-building
behavior. No change to how the user navigates the scope.

**Sync is implemented as best-effort.** Adversarial review of the syncs in the
session log surfaced a caveat (see below) and broader community reports note
CPWI's ASCOM driver can time out on `Sync`. A `Sync` failure must therefore
*never* roll back or mask the successful `AddAlignmentReference` that preceded
it — the reference is already in CPWI's model regardless. PR #11 wraps each
sync call in `TrySync`, which logs and swallows exceptions.

### Caveat from the session log: Sync's effect on reported RA is imprecise

A second careful pass over the session log surfaced a subtlety worth recording.
The Dec value before each subsequent sync matches the previous sync's target
exactly, suggesting CPWI honors `Sync` precisely in Dec. RA does not behave as
cleanly: across the four `Syncing scope from … to …` events in the log, the
RA delta between consecutive syncs is inconsistent with simple sidereal
advancement of the prior target (one interval shows nearly no advancement in
23 minutes; another shows ~2 minutes of excess advancement in 18 minutes).
Without ASCOM driver trace logging we can't tell whether this reflects how
CPWI tracks RA internally, the operator nudging in CPWI, or a quirk of which
value gets reported in the `from` field. **The practical implication is that
Sync-per-push restores reported pose approximately, not precisely — much
better than frozen-at-home, but the next-session protocol should read back
RA/Dec immediately after a Sync and compare to the synced values to
characterize this.**

### Remaining open questions (to settle next session, before merging the code change)

The Sync-per-push design is supported by the log evidence and by the available
external corroboration, but two clean empirical confirmations are worth
collecting before locking it in:

1. **The 30-second driver test** — note NINA's RA/Dec, slew significantly via
   CPWI's UI, wait ~5 s, re-check NINA. Predicted result: **unchanged**. If so,
   the driver-doesn't-propagate-CPWI-UI-motion conclusion is confirmed directly,
   not just inferred from the log. If NINA's view does update, then the
   propagation works in some cases and we need to characterize when.
2. **Sync recovers a stuck pose, and how precisely** — when the pose is
   observed stale, issue an ASCOM `Sync` to known coordinates (NINA's "Slew
   and Center with sync," or Device Hub manually), then **immediately read back
   `RightAscension` / `Declination` and compare to the synced values**. The
   23:22:34 Sync in the b66ad4b1 log shows the call being *issued*, but the
   log doesn't include a post-sync read-back, so we don't know if or how
   precisely it actually updated the reported pose — that's what this test
   isolates. Pair with the RA caveat noted above.

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
