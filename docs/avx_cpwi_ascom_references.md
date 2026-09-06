# AVX / CPWI / ASCOM / NINA Reference Index

Authoritative references for expanding the `AddToAlignmentModel` NINA plugin to support German equatorial mounts (specifically the Celestron AVX). Organized by the architectural layer each reference applies to.

The plugin sits at the top of a four-layer stack:

```
NINA plugin code  →  NINA mediators  →  ASCOM ITelescopeV3 driver  →  CPWI  →  NexStar protocol  →  AVX mount
```

---

## Layer 1: Mount-level — NexStar Communication Protocol

What the AVX motor controller and hand controller speak over RS-232/USB. CPWI and any ASCOM driver translate to and from this protocol. The plugin does not call this layer directly, but it defines the primitives every layer above is ultimately limited by.

### NexStar Communication Protocol PDF (Celestron, official)
- URL: https://s3.amazonaws.com/celestron-site-support-files/support_files/1154108406_nexstarcommprot.pdf
- Applies to GPS, iSeries, SE, GT, CPC, SLT, AdvancedGT, and CGE mounts. AVX uses the NexStar+ command set and is compatible.
- Covers: Get/GOTO in RA/Dec and AzAlt; Sync; tracking mode and rate; time/location; GPS; RTC commands.
- Mechanical details: 9600 8-N-1 serial; positions returned as hex fractions of a revolution (16- or 24-bit); drivers should wait up to 3.5s for hand-control responses; `P`-prefix commands are pass-through to specific telescope devices.

### Celestron reference page
- URL: https://www.celestron.com/pages/support-files/nexstar-communication-protocol-v-1-2

### Community programming guide
- URL: https://www.nexstarsite.com/PCControl/ProgrammingNexStar.htm
- Curated resources for writing software against NexStar-controlled telescopes. Useful when the official PDF leaves gaps.

---

## Layer 2: ASCOM ITelescope (V3 in NINA, V4 in current ASCOM spec) — the standard contract NINA uses

Every standard ASCOM call the plugin makes — `SlewToCoordinatesAsync`, `SyncToCoordinates`, `SideOfPier`, `DestinationSideOfPier`, `Action`, `SupportedActions` — is defined here. This is the right layer to consult for GEM-aware pier-side handling and meridian-flip logic.

> Note (2026-05): ASCOM restructured their documentation site in early 2026 and the old `/Help/Platform/html/...` and `/Help/Developer/html/...V3...` URLs now 404. The current interface is **ITelescopeV4** (ASCOM Platform 7, 7.0.0-rc.0), but the semantics of every member used by this plugin are unchanged from V3. Two equivalent sources are available; the Sphinx-format "ASCOM Master Interfaces" page is by far the easier read.

### Primary reference (recommended)
**ASCOM Master Interfaces — Telescope (single-page, all members):**
- URL: https://ascom-standards.org/newdocs/telescope.html
- Contains the definitive specification for `SideOfPier`, `DestinationSideOfPier`, `SlewToCoordinatesAsync`, `SyncToCoordinates`, `SupportedActions`, and `Action`. Includes asynchronous-operation notes that matter for GEM flip handling.

### Fetchable fallback — interface source
The published Help pages are generated from the XML doc comments in the ASCOM Platform source, which can be read directly when the website cannot:
- https://raw.githubusercontent.com/ASCOMInitiative/ASCOMPlatform/master/ASCOM.DeviceInterface/ITelescopeV3.cs
- Key passages: `SideOfPier` is defined by mechanical Dec (`pierEast` = |mech Dec| ≤ 90°), "side of pier is, in general, not a useful term"; `DestinationSideOfPier` is the side "if a slew … is performed at the current instant of time"; `SyncToCoordinates` "should only be relied on to improve pointing for positions close to the position at which the sync is done".

### Conceptual deep dives (linked from the primary page)
- **"What is the meaning of pointing state in the docs for SideOfPier"** — explains why `SideOfPier` is a misnomer (it's the pointing state, not a literal physical side), and the GEM-vs-fork distinction. Reachable from the Telescope page above; also installed locally by the ASCOM Developer Components installer as **"Pointing State and Side of Pier"**.
- **"What is DestinationSideOfPier and why would I want to use it?"** — the rationale for predicting pier side before a slew. Same source page.

### Per-member reference (V4 generated API docs)
These are the new URLs that replace the old V3 ones. The path is now `/help/developer/html/...V4_<member>.htm`.

**Interface index:**
- https://www.ascom-standards.org/help/developer/html/T_ASCOM_DeviceInterface_ITelescopeV4.htm

**SideOfPier** — pointing state of the mount. Writable to force a meridian flip on drivers that support it (`CanSetPierSide = true`); during a forced flip, `Slewing` must return True.
- https://www.ascom-standards.org/help/developer/html/P_ASCOM_DeviceInterface_ITelescopeV4_SideOfPier.htm

**DestinationSideOfPier** — predicts which pier side a given RA/Dec slew will end on. Use this to plan a grid so the run doesn't force an unintended meridian flip mid-loop.
- https://www.ascom-standards.org/help/developer/html/M_ASCOM_DeviceInterface_ITelescopeV4_DestinationSideOfPier.htm

**SlewToCoordinatesAsync** — async slew to RA/Dec. Already used by the plugin via `telescopeMediator.SlewToCoordinatesAsync`. Returns immediately with `Slewing=True`.
- https://www.ascom-standards.org/help/developer/html/M_ASCOM_DeviceInterface_ITelescopeV4_SlewToCoordinatesAsync.htm

**SyncToCoordinates** — sync the scope's equatorial coordinates to supplied RA/Dec. Per the plugin author's notes, CPWI's standard Sync does not update the alignment model — hence the plugin's use of the custom Action below.
- https://www.ascom-standards.org/help/developer/html/M_ASCOM_DeviceInterface_ITelescopeV4_SyncToCoordinates.htm

**SupportedActions** — discovery property listing custom Action names the driver exposes. Returns names spelled exactly as they must be passed to `Action()`. The canonical way to find out whether `Telescope:AddAlignmentReference` is available on the CPWI driver in EQ mode.
- https://www.ascom-standards.org/help/developer/html/P_ASCOM_DeviceInterface_ITelescopeV4_SupportedActions.htm

**Action method** — invoke a custom driver-specific command. Used by the plugin as `telescopeMediator.Action("Telescope:AddAlignmentReference", "{RA}:{Dec}")`. Per ASCOM, unrecognized actions throw `ASCOM.ActionNotImplementedException`; invalid parameter formats throw a different exception (typically `InvalidValueException` or `DriverException`). Action names are case-insensitive.
- https://www.ascom-standards.org/help/developer/html/M_ASCOM_DeviceInterface_ITelescopeV4_Action.htm

### ASCOM Basics (context on the Action / SupportedActions mechanic)
- https://ascom-standards.org/About/Basics.htm
- Notes that `Action()` plus `SupportedActions` is the official, free-form, non-standardized extension mechanic — exactly what CPWI uses for `Telescope:AddAlignmentReference`.

---

## Layer 3: CPWI and its ASCOM driver

CPWI is Celestron's PC control application. Its ASCOM driver is what NINA (and therefore the plugin) talks to.

### CPWI product page (Celestron)
- URL: https://www.celestron.com/pages/celestron-pwi-telescope-control-software
- Notes PointXP modeling supporting 100+ alignment points, All-Star Polar Alignment for German equatorial mounts and alt-az mounts with a wedge, and PEC training. Confirms CPWI is the intended controller for AVX.

### CPWI Software Manual (English, 2020-06-22 edition)
- URL: https://celestron-site-support-files.s3.amazonaws.com/support_files/CPWI%20Software%20Manual_ENG_06222020.pdf
- Documents features and UI. Does **not** document the ASCOM driver's custom Action set.

### The `Telescope:AddAlignmentReference` Action — documentation gap
- A CPWI-specific custom Action invoked through ASCOM's generic `Action(name, parameters)` method. Format observed in plugin: `Action("Telescope:AddAlignmentReference", "{RA}:{Dec}")`.
- Added in **CPWI 2.3.5 (August 2020)** — release-note line "Added ASCOM.Action (Telescope:AddAlignmentReference,ra:dec)"; no accompanying public documentation has ever been published by Celestron.
  - Release notes as republished by Celestron's UK distributor: https://www.dhinds.co.uk/blogs/news/new-version-release-of-celestron-pwi-software
  - Celestron's own release-notes page (blocks automated fetch; indexed for the same string): https://www.celestron.com/blogs/general/cpwi-latest-release-notes
- Origin discussion (Cloudy Nights, December 2020): https://www.cloudynights.com/forums/topic/727389-ascom-console-commands/
- The `ra:dec` argument must be **JNow** (topocentric), matching the driver's `EquatorialSystem`; CPWI's UI displays J2000, which is a known source of confusion: https://forums.sharpcap.co.uk/viewtopic.php?t=2102 , https://www.cloudynights.com/topic/745029-cpwi-j2000-coordinates-error/
- Other known users of the action (all Alt-Az oriented; no published EQ/GEM report exists): EAACtrl (https://github.com/Xio1996/EAACtrl), the "CPWI Plate Align" script (https://www.cloudynights.com/topic/864424-cpwi-plate-align/), and SharpCap Pro scripting one-liners.
- Authoritative verification path: query `SupportedActions` on the CPWI ASCOM driver with the AVX connected in EQ mode. If the string is present, the action is exposed. Whether it actually updates the alignment model in EQ mode must be verified empirically (slew → plate-solve → send action → observe pointing improvement).

### Community reports on the CPWI ASCOM driver (search-snippet level — the forums block automated fetches; verify in a browser before citing)
- **Sync timeouts** — driver hands the sync to CPWI and errors after ~2 s if CPWI does not answer (normal sync ~0.3 s); also quotes the literal "Method SyncToAltAz is not implemented in this driver": https://forums.sharpcap.co.uk/viewtopic.php?t=4749
- **Sync does not add to an existing model** — "CPWI, once it creates the initial alignment model, will not accept Sync information to improve the initial alignment model": https://forums.sharpcap.co.uk/viewtopic.php?t=8442
- **Mini-slew window must be active for Stellarium slews**: https://www.cloudynights.com/topic/788742-using-stellarium-with-cpwi/
- **Standard NINA + CPWI workflow** (align in CPWI, then drive from NINA): https://theastronoob.com/2021/12/29/celestron-cpwi-ascom-nina/ , https://www.cloudynights.com/topic/865683-cge-cpwi-nina-and-phd2/
- **Possibly relevant, unread** (may be first-party): https://www.cloudynights.com/forums/topic/972801-cpwi-ascom-driver-problems/ , https://www.cloudynights.com/forums/topic/972794-cpwi-ascom-driver-reporting-incorrect-pier-side/

### ASCOM Platform + diagnostic tools
- URL: https://ascom-standards.org/Downloads/Index.htm
- The ASCOM Platform installer's developer components include Conform and Diagnostics, which dump `SupportedActions` and exercise every interface method. The simplest tool to verify CPWI's Action set without writing code.

### ASCOM Device Hub (recommended connection topology)
- PrimaLuceLab writeup: https://www.primalucelab.com/blog/support/how-to-remotely-control-your-celestron-equatorial-or-alt-azi-mount-with-play/
- Practical recommendation: connect NINA through ASCOM Device Hub rather than direct-to-CPWI, so NINA and CPWI can share the underlying telescope connection. Relevant if testing involves both apps running simultaneously.

---

## Layer 4: NINA plugin development

The plugin runs inside NINA and uses NINA's mediator abstractions (`ITelescopeMediator`, `ICameraMediator`, `IPlateSolverFactory`, etc.) which wrap the underlying ASCOM drivers.

### NINA project / plugin developer resources
- NINA project site: https://nighttime-imaging.eu/
- NINA plugin manifest repo (referenced in the plugin's GitHub Action): https://github.com/isbeorn/nina.plugin.manifests
- Existing plugin source as practical reference: `ITelescopeMediator` usage patterns can be read directly from this repo's `CreateAlignmentModelVM.cs`, `ModelCreation.cs`, and `ADP_Tools.cs`.

---

## Verification checklist (before any code changes for AVX support)

1. Connect the AVX via CPWI in EQ (German polar) mode.
2. Dump `SupportedActions` on the CPWI ASCOM driver using ASCOM Conform or a minimal NINA sequence item. Confirm `Telescope:AddAlignmentReference` appears.
3. After a basic 2-star alignment in CPWI, slew somewhere, plate-solve, and invoke `Action("Telescope:AddAlignmentReference", "{RA}:{Dec}")` with the solved coordinates. Confirm no exception is thrown.
4. Observe whether CPWI's pointing accuracy at nearby points improves measurably. This is the only definitive test that the action updates the alignment model in EQ mode — not just AltAz.

Only after step 4 succeeds is it worth implementing pier-side-aware grid generation, meridian-flip handling, and a GEM-appropriate pre-alignment prompt.
