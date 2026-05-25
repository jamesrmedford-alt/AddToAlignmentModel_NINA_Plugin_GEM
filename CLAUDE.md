# AddToAlignmentModel — NINA plugin (AVX / German equatorial support fork)

## What this project is

A NINA (Nighttime Imaging 'N' Astronomy) plugin that builds a CPWI alignment
model for Celestron mounts by slewing to a grid of points, plate-solving each,
and feeding the solved RA/Dec back to CPWI via the ASCOM custom action
`Telescope:AddAlignmentReference`. Upstream author: Dale Page (ADPUK). License:
MPL-2.0.

The upstream plugin is tested only on **Alt-Az** mounts. This fork exists to add
**German equatorial (GEM)** support, specifically the **Celestron AVX** run
through CPWI. The maintainer has agreed to accept this contribution.

## Tech stack (verify, don't assume)

- Language: **C#**. This is NOT a JavaScript/TypeScript project — do not use npm,
  vitest, jest, or any JS tooling.
- Framework: **.NET** with **WPF** UI. NINA 3.x targets **.NET 8**; confirm the
  exact `<TargetFramework>` in the plugin `.csproj` (expected `net8.0-windows`)
  before configuring anything.
- Plugin model: NINA loads plugins via **MEF** (`[Export]` / `[ImportingConstructor]`
  attributes). Scaffolded from `isbeorn/nina.plugin.template`.
- Key namespaces/classes (read these before touching anything):
  - `ADPUK.NINA.AddToAlignmentModel` — root namespace
  - `ADP_Tools` — static helpers: connection validation, pre-alignment checks,
    coordinate logic. **Most testable code lives here.**
  - `ADPClasses/ModelCreation.cs` — per-point acquisition + solve + reference push
  - `CreateAlignmentModelVM` — WPF view model; `ExecuteCreate` is the grid loop
  - `AddToAlignmentModelSequenceItems/` — sequencer items
  - `AddToAlignmentModelImageTab/` — view models for the image tab
  - `AddToAlignmentModel.cs` — plugin entry point + settings (incl. the existing
    `EnableEquatorialMounts` flag)

## Collaboration model — read this carefully

- The human driving this (the repo owner of the fork) has **the AVX hardware and
  a CPWI setup**, but **zero C# expertise**. They cannot write or deeply review
  C#. You (Claude Code) write all code; they run it, test against hardware, and
  review diffs at the design level.
- Because of that: **explain what each change does and why, in plain language,
  in PR descriptions and commit messages.** Favor small, legible diffs over
  clever ones. If a change is subtle, say so explicitly.
- The maintainer is a volunteer hobbyist. Contributions go back as **small,
  focused PRs**, each with a clear description and (where relevant) hardware test
  results. Match existing code style; respect `.editorconfig`.

## Reference material

- `docs/avx_cpwi_ascom_references.md` — curated index of NexStar protocol, ASCOM
  ITelescope (SideOfPier / DestinationSideOfPier / SupportedActions / Action),
  and CPWI documentation, organized by architectural layer. **Read this for any
  question about how the mount/driver/CPWI layers interact.**
- `docs/1154108406_nexstarcommprot.pdf` — NexStar serial protocol (read on demand
  with `pdftotext`; not auto-loaded).
- `docs/CPWI_Software_Manual_ENG_06222020.pdf` — CPWI user manual (same).

## Immediate task: build a regression safety net (additive only)

Goal: before changing any behavior, establish a test + CI harness that fails
loudly if existing **Alt-Az** behavior regresses. This protects against
AI-generated GEM changes silently breaking the base plugin.

**This first phase must not modify any existing source file.** It adds a test
project and CI config only. No refactoring of plugin code yet.

### Toolchain

- Test framework: **xUnit** (`Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`)
- Mocking: **Moq** — required to stand in for NINA interfaces (`ITelescopeMediator`,
  `ICameraMediator`, `IPlateSolverFactory`, etc.) that can't be instantiated for real
- Coverage: **Coverlet** (`coverlet.collector`) + **ReportGenerator** for HTML
- CI: **GitHub Actions** on `windows-latest` (WPF assemblies are Windows-only),
  running `dotnet restore` / `dotnet build` / `dotnet test --collect:"XPlat Code Coverage"`,
  then a coverage threshold gate

### Coverage strategy — tiered, not a flat 95%

A flat 95% across a WPF + hardware plugin is impractical and low-value. Instead:

- **Tier 1 — target ≥95%:** pure logic with no I/O or WPF dependency. Coordinate
  transforms, grid-point generation, validation predicates in `ADP_Tools`, and any
  similar input→output methods. This is where GEM changes will land, so this is
  what must be locked down.
- **Tier 2 — best effort:** orchestration that can be exercised with Moq'd
  mediators (e.g. parts of `ExecuteCreate`, `ModelCreation`). Cover what's
  reachable without contortion.
- **Tier 3 — excluded from the gate:** `.xaml` code-behind, MEF/plugin bootstrap,
  raw hardware-I/O wrappers, generated resources. Add these to coverage-exclusion
  config rather than writing brittle tests.

Set the CI gate threshold against the **Tier 1 assembly/namespace**, not the whole
project. Document the exclusions explicitly in the coverage config so the number
is honest.

### Characterization-test priority

Coverage percentage is a proxy. The actual protection is **characterization tests**
that pin current behavior: feed known inputs to the existing Alt-Az logic and
assert the current outputs, so any later change that alters those outputs fails CI.
Write these for `ADP_Tools` validation/coordinate logic and the grid-generation
path first — that's the behavior we must not break.

### Suggested layout

```
/AddToAlignmentModel.Tests/        ← new xUnit project, references the plugin project
  ADP_Tools.Tests.cs
  ModelCreation.Tests.cs
  GridGeneration.Tests.cs
  TestHelpers/                     ← Moq factories for NINA mediators
/.github/workflows/ci.yml          ← build + test + coverage gate on windows-latest
/coverlet.runsettings              ← coverage include/exclude config
```

### Lock-down checklist (fork hygiene)

1. All work on feature branches; never commit directly to the fork's `main`.
2. Enable branch protection on the fork's `main`: require the CI status check to
   pass before merge.
3. Phase-1 PR (this task) touches only new files. If you find yourself needing to
   edit an existing `.cs` file to make something testable, **stop** — that's a
   Tier-2 refactor, a separate later step, and may need the maintainer's sign-off
   since it alters base code.

## Known GEM compatibility findings (where behavior changes will eventually go)

These are the areas the actual GEM work will touch, *after* the safety net is in
place. Listed here so test coverage can be aimed correctly — not to be implemented
yet.

1. `ADP_Tools.ValidateConnections` — blocks non-AltAz modes unless
   `EnableEquatorialMounts` is set. Will be softened to a warning.
2. `ADP_Tools.ReadyToStart` — assumes horizon/due-north pre-position; wrong for a
   GEM at home (points at the celestial pole). Will branch on mount mode.
3. `CreateAlignmentModelVM.ExecuteCreate` — marches azimuth monotonically through
   360°, which crosses the meridian without a flip on a GEM. Will be reworked to
   partition the grid by pier side using `SideOfPier` / `DestinationSideOfPier`.
4. The `Telescope:AddAlignmentReference` action — undocumented by Celestron;
   unverified in EQ mode. Hardware verification (via the diagnostic sequence item)
   is the gating prerequisite for all of the above.

## Conventions

- Follow `.editorconfig` for style (indentation, brace placement, naming).
- Preserve existing MPL-2.0 file headers; don't add headers to files that lack them.
- New files: match the project's namespace and the existing sequence-item /
  view-model patterns (`[ImportingConstructor]`, `Clone(this)` copy pattern,
  resource strings in `.resx`).
- Keep commits small and messages descriptive of *why*, not just *what*.

## Things NOT to do

- Do not use any JavaScript tooling (npm/vitest/jest). This is C#/.NET.
- Do not modify existing source files during the safety-net phase.
- Do not chase a flat 95% across WPF/bootstrap/hardware code.
- Do not implement GEM behavior changes until the safety net is merged and the
  hardware verification of `AddAlignmentReference` in EQ mode has passed.
