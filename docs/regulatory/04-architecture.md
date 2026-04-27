# Chapter 04 — Architecture Description

| Field | Value |
|---|---|
| Document | `04-architecture.md` |
| Version | 0.2-draft |
| Status | Draft — Under Review |
| Last updated | 2026-04-27 |
| Author | Risto Hirvilammi |
| Reviewer | TBD |
| Approver | TBD |

---

## 4.1 Scope of this chapter

This chapter describes the structural organisation of EQD2 Viewer, the responsibility
boundaries of each component, the data flow for the principal use cases, and the
external dependencies. It is written against **software version 0.9.x** and matches the
source tree at the time of the corresponding git tag.

Detailed algorithm specifications are in chapter 06; verification is in chapter 08.

---

## 4.2 Architectural principles

The code is organised under two principles:

1. **Clean Architecture layering.** Domain logic (calculations, types, invariants) has
   no dependency on user interface (WPF) or external systems (Varian ESAPI). The
   outermost layers depend inward; the innermost layer (`Core`) is pure C# with no
   third-party API dependencies beyond .NET.
2. **Offline reproducibility.** Every algorithm is exercisable without the Varian TPS.
   Clinical data can be exported once to JSON "fixtures" and then analysed or tested on
   any developer workstation. This allows the algorithmic core to be verified and
   validated without repeatedly accessing patient-specific TPS sessions.

---

## 4.3 Project layout

The solution is organised into the following .NET projects. All target .NET Framework
4.8, x64.

```
EQD2Viewer.sln
├── EQD2Viewer.Core                 Domain types, EQD2 math, DVH, rendering math
├── EQD2Viewer.Services             Service-layer orchestration (DVH, summation)
├── EQD2Viewer.App                  WPF UI, view models, composition root
├── EQD2Viewer.Esapi                Varian ESAPI adapter + entry script
├── EQD2Viewer.DevRunner            Standalone offline WPF host (JSON fixtures)
├── EQD2Viewer.FixtureGenerator     ESAPI tool that exports patient snapshots to JSON
├── EQD2Viewer.Fixtures             JSON fixture schema and loader
├── EQD2Viewer.Stubs                Empty ESAPI stubs for CI builds without Eclipse
└── EQD2Viewer.Tests                Unit and integration tests
```

---

## 4.4 Layer responsibilities

### 4.4.1 Domain core — `EQD2Viewer.Core`

Contains the pure-C# domain of the application. Has **no dependency** on WPF or
Varian ESAPI.

Key folders:

- `Core/Data/` — value types: `Vec3`, `VolumeGeometry`, `VolumeData`, clinical types
  (`PlanData`, `CourseData`, `StructureContour`).
- `Core/Calculations/` — pure numerical routines:
  - `EQD2Calculator` — BED / EQD2 transformation (see chapter 06 § 6.2).
  - `StructureRasterizer` — polygon-to-mask rasterisation using marching-squares-based
    interior filling.
  - `MarchingSquares` — isocontour extraction for isodose display.
  - `MatrixMath` — 3×3 / 4×4 affine matrix operations, axis-aligned resampling.
  - `ImageUtils`, `ColorMaps`, `HotspotFinder` — image-domain helpers.
- `Core/Models/` — report types consumed by the UI and by tests: `DVHSummary`,
  `RenderResult`, `EQD2Settings`, etc.
- `Core/Interfaces/` — abstract contracts that the outer layers implement
  (`IDVHCalculation`, `IClinicalDataSource`, `ISummationService`, etc.).
- `Core/Logging/` — `SimpleLogger` — a lock-serialised file/`Debug` logger.
- `Core/Serialization/` — JSON fixture serializer.

No element of `Core` can reach UI code. This is enforced by project-reference direction
only — there is no downward reference from `Core`.

### 4.4.2 Services — `EQD2Viewer.Services`

Orchestration layer. Contains implementations of `Core` interfaces that combine multiple
domain calls:

- `DVHService` — takes structures, dose grid and parameters, produces `DVHSummary` per
  structure. Caches structure masks between calls.
- `SummationService` — sums two or more dose distributions on a reference CT grid,
  honouring EQD2 transformation and the affine spatial registrations supplied by the
  data loader.
- `SummationServiceFactory` — constructs the `SummationService` with the required
  dependencies.

Depends on `Core`. No UI or ESAPI dependency.

### 4.4.3 UI — `EQD2Viewer.App`

WPF user interface. Depends on `Core` and `Services`. Includes:

- `AppLauncher` — composition root. Wires up services, emits the research-prototype
  banner to the log, and hands control to the UI.
- `UI/Views/` — XAML views: `MainWindow`, `PlanSummationDialog`, `StructureSelectionDialog`.
- `UI/ViewModels/` — view models for MVVM; `MainViewModel` is partitioned (`.Rendering`,
  `.DVH`, `.Summation`, `.Properties`) for maintainability.
- `UI/Rendering/` — WPF-specific rendering services and image encoding.
- `UI/Controls/` — custom controls (`InteractiveImageViewer`).
- `UI/Styles/` — WPF resource dictionaries.

### 4.4.4 ESAPI adapter — `EQD2Viewer.Esapi`

The **only** project that references Varian ESAPI. Contains:

- `Script.cs` — the ESAPI entry point (`[ESAPIScript]` attribute).
- `Adapters/EsapiDataSource` — maps Varian ESAPI types to `ClinicalSnapshot` POCO tree.
- `Adapters/EsapiSummationDataLoader` — on-demand dose / structure loading for summation.

All other layers see only abstract `IClinicalDataSource` / `ISummationDataLoader` contracts.
After the initial snapshot load no further ESAPI calls are made.

### 4.4.5 Offline hosts — `EQD2Viewer.DevRunner`, `EQD2Viewer.FixtureGenerator`

- `DevRunner` — a standalone WPF application. Loads JSON fixtures from disk via
  `JsonDataSource`, hands them to `AppLauncher`. Enables end-to-end development and
  test of the UI without a TPS.
- `FixtureGenerator` — an ESAPI script that exports a patient snapshot to JSON. Runs
  inside Eclipse, writes a `.json` file the developer can then load offline. It is the
  bridge that enables the offline reproducibility principle.

### 4.4.6 Tests — `EQD2Viewer.Tests`

xUnit-based test project. Depends on all other projects except `EQD2Viewer.Esapi` (ESAPI
is stubbed through `EQD2Viewer.Stubs` during tests).

Test categories:
- **Unit tests** for each algorithm (EQD2 transform, DVH integration, rasterisation,
  marching squares, matrix math, colour maps, hotspot finder).
- **Integration tests** for the rendering pipeline, DVH pipeline, serialization
  round-trip, multi-plan summation.
- **Smoke tests** for the offline DevRunner (`DevRunnerSmokeTests`).

---

## 4.5 Build configurations

Two build configurations are supported:

| Configuration | Purpose |
|---|---|
| `Debug` | Local development; CI on Windows agents |
| `Release` | Deployment artefact (Eclipse plugin + DevRunner) |

The `Release` build organises deliverables under `BuildOutput/`:
- `01_Eclipse_ESAPI_Plugins/` — the bundled `.esapi.dll` files for Eclipse.
- `02_Standalone_Runner/` — the offline DevRunner with bundled fixtures.

---

## 4.6 Data flow: clinical session (ESAPI path)

```
Eclipse (TPS)
    │
    │  [physicist launches EQD2 Viewer script]
    ▼
EQD2Viewer.Esapi/Script.cs
    │   [ESAPIScript attribute, runs inside Eclipse process]
    ▼
EsapiDataSource.LoadSnapshot()
    │   [reads CT, dose, structures, plans, courses from ESAPI]
    │   → ClinicalSnapshot (POCO, all ESAPI dependencies resolved)
    ▼
AppLauncher.Launch(snapshot, summationLoader)
    │   [disclaimer banner logged, rendering + DVH services constructed]
    ▼
MainWindow (WPF)
    │   [user navigates EQD2 / DVH / plan summation features]
    ▼
User actions trigger MainViewModel methods
    │   → Core.Calculations (EQD2, DVH, ...)
    │   → Services (DVHService, SummationService)
    ▼
Outputs rendered in UI and written to log
```

No post-launch ESAPI calls are made from the UI layer; ESAPI is accessed only at snapshot
load time (and for lazy dose loading in plan summation, via `EsapiSummationDataLoader`).

---

## 4.7 Data flow: offline session (DevRunner path)

```
Developer / validator workstation
    │
    ▼
DevRunner.exe
    │
    ▼
JsonDataSource.LoadSnapshot(path-to-fixture.json)
    │   → ClinicalSnapshot (same POCO as ESAPI path)
    ▼
AppLauncher.Launch(snapshot)
    │
    ▼
MainWindow (WPF)   — same as ESAPI path from here on
```

Fixtures are produced by running the `FixtureGenerator` ESAPI script once per dataset.
The generated `.json` file captures the CT grid, dose grid, structures and plan metadata
in a stable schema (`EQD2Viewer.Fixtures.Models`).

This path is the backbone of:
- Developer inner loop (no TPS required after fixture generation);
- CI / test execution;
- Clinical validation dataset management (see chapter 09 when written).

---

## 4.8 Plan-summation flow

For re-irradiation assessment the typical flow is:

1. **Patient load** — user opens a patient in Eclipse with the reference plan; the
   EQD2 Viewer script is invoked.
2. **Plan selection** — user opens the **Dose Summation** dialog
   (`PlanSummationDialog`), selects the reference course/plan and one or more prior
   course/plan entries.
3. **Registration choice** — for each non-reference plan the user picks a Varian
   spatial registration (or accepts "same CT as reference") to map that plan onto the
   reference CT grid.
4. **Summation** — `SummationService` resamples each moving dose onto the reference
   grid via the chosen affine registration and accumulates in EQD2 space (or as
   physical dose, if Physical mode is selected).
5. **Review** — the resulting summed dose is available for EQD2 isodose and DVH
   inspection in the main window.

The deformable image registration (DIR) module that previously lived in this codebase
has been removed; only affine ESAPI registrations are supported. See the README version
history for the removal note.

---

## 4.9 External dependencies

| Dependency | Version | Licence | Role | Runtime or build only |
|---|---|---|---|---|
| .NET Framework | 4.8 | Microsoft Reference Source | Runtime platform | Both |
| WPF | 4.8 | Microsoft Reference Source | UI toolkit | Runtime |
| Varian ESAPI | 15.6 + | Varian (binary-only, not redistributed) | TPS data source | Clinical runtime, build of `EQD2Viewer.Esapi` |
| OxyPlot | (per `packages.config`) | MIT | DVH plotting | Runtime |
| Costura.Fody | (per `packages.config`) | MIT | ESAPI-script bundling | Build |
| xUnit, FluentAssertions, Moq | current | (respective MIT / Apache) | Test framework | Test runtime |

Full SOUP treatment (including security posture) is in chapter 07 when written.

---

## 4.10 Deployment topology

The device has no server-side component. A single build produces:

- `EQD2Viewer.App.esapi.dll` — the ESAPI-script binary, deployed to Eclipse's script
  folder on the clinical workstation.
- `EQD2Viewer.DevRunner.exe` — the standalone host for offline use.

All installation is file-copy based; there is no installer, no Windows service, no
auto-update. Operating-environment details are in chapter 13 when written.

---

## 4.11 Logging and observability

A process-wide logger (`SimpleLogger`) writes both to the .NET `Debug` channel and to a
desktop log file (`EQD2Viewer.log`). At startup the `AppLauncher` emits a banner declaring
the research-prototype status. During plan summation the log captures plan selection,
chosen registrations, voxel-grid dimensions, and per-plan summary statistics.

This logging is the primary mechanism for post-hoc quality review and for regulatory
traceability of individual summation runs.
