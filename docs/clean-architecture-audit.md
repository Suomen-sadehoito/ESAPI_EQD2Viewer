# Clean Architecture Violation Audit

Read-only audit of `EQD2Viewer` (post-ITK-cleanup state, version 0.9.4-beta).
Verifies these dependency rules:

- `EQD2Viewer.Core` depends on nothing
- `EQD2Viewer.Services` depends only on Core
- `EQD2Viewer.App` depends on Core and Services
- `EQD2Viewer.Esapi` is the only project allowed to reference `VMS.TPS.*`
  types

> Note: the original brief mentioned a fourth dependency for `App` —
> "Registration interfaces". Those projects (`EQD2Viewer.Registration` and
> `EQD2Viewer.Registration.ITK`) were removed in version 0.9.4-beta. The
> rule is **stale** and can be dropped from architectural docs.

Severity legend: **violation** = real breach · **smell** = questionable but
defensible · **stale-rule** = rule no longer applies.

---

## Headline result

**Zero true violations.** The architecture is clean. Every search for the
five categories below returned either no matches or matches that are
explicitly permitted by the rules (composition root, defined I/O layers,
ESAPI adapter project).

---

## 1. Core / Services importing `VMS.TPS.*` or `System.Windows.*`

**Status:** clean.

Searched `^using\s+(VMS\.TPS|System\.Windows)` against
`EQD2Viewer.Core/**` and `EQD2Viewer.Services/**`. **No matches.**

Also searched for the more permissive WPF leak markers
(`Application.Current`, `DispatcherTimer`, `WriteableBitmap`,
`System.Windows.Media`) inside Core/Services. **No matches.**

---

## 2. ESAPI references outside `EQD2Viewer.Esapi/`

**Status:** clean.

Searched for `VMS.TPS`, `VVector`, `Patient`, `PlanSetup`, `Course`, `Dose`,
`StructureSet`, `DoseValue`, `[ESAPIScript]`, `ScriptContext`, `Eclipse`
across the whole tree.

| Project | ESAPI references? | Allowed? |
|---|---|---|
| `EQD2Viewer.Esapi` | yes — `Adapters/EsapiDataSource.cs:1-4`, `Adapters/EsapiSummationDataLoader.cs:1-5` | **yes (allowed home)** |
| `EQD2Viewer.Stubs` | yes — empty stub types under `VMS/TPS/Common/Model/...` | **yes (CI compile-time stand-in for real ESAPI DLLs)** |
| `EQD2Viewer.FixtureGenerator` | yes — `Script.cs`, `FixtureExporter.cs`, `SnapshotExporter.cs` | **yes (standalone ESAPI script tool, not a library layer)** |
| `EQD2Viewer.Core` | no | n/a |
| `EQD2Viewer.Services` | no | n/a |
| `EQD2Viewer.App` | no | n/a |
| `EQD2Viewer.DevRunner` | no | n/a |
| `EQD2Viewer.Fixtures` | no | n/a |
| `EQD2Viewer.Tests` | no | n/a |

The "only Esapi may reference VMS.TPS" rule is followed in spirit by the
two legitimate exceptions (`Stubs` is the binary-shaped placeholder of
ESAPI itself; `FixtureGenerator` is itself an ESAPI script). Worth
codifying in `Directory.Build.props` as a build-time check
(`<Reference Include="VMS.TPS.*">` only allowed in those three projects).

---

## 3. WPF types in `EQD2Viewer.Core/`

**Status:** clean (one comment-only mention of `INotifyPropertyChanged` and
`MediaColor`).

Searched for `Brush`, `SolidColorBrush`, `Color` (System.Windows.Media kind),
`MediaColor`, `WriteableBitmap`, `BitmapSource`, `Geometry`,
`StreamGeometry`, `DispatcherTimer`, `Application.Current`,
`RoutedEventArgs`, `INotifyPropertyChanged`, `Visibility`,
`BooleanToVisibilityConverter`, `DependencyObject`, `DataContext` against
`EQD2Viewer.Core/**`.

| Site | Symbol | Severity | Notes |
|---|---|---|---|
| `EQD2Viewer.Core/Data/IsodoseLevelData.cs:8` | `INotifyPropertyChanged`, `MediaColor` (in a doc comment) | **none** | Documentation only — explains that the WPF-bindable peer `IsodoseLevel` lives in the App layer. The DTO itself has no UI dependencies. |

The `IsodoseLevelData` (Core) / `IsodoseLevel` (App.UI.Rendering) split is
the textbook pattern: domain DTO in Core, WPF-bindable peer in the UI
project.

---

## 4. Reflection-based loads

**Status:** clean. Post-ITK-cleanup, no reflection loading remains.

Searched `Assembly\.Load`, `Activator\.CreateInstance`, `DllImport`
against the entire tree (excluding `BuildOutput/**`). **No matches.**

`Type.GetType(...)`, `GetMethod(`, `Invoke(`, `MethodInfo`, `BindingFlags`
also returned no production-code reflection — the only `Invoke(` matches
are normal event-delegate or `Dispatcher.BeginInvoke` calls (UI thread
hop), which are not type-safety bypasses.

The `AppLauncher.TryLoadItkService` + `SetDllDirectory` reflection path
that previously loaded `EQD2Viewer.Registration.ITK.dll` was removed with
the rest of the ITK module; verified by re-reading
`EQD2Viewer.App/AppLauncher.cs` end-to-end (currently 73 lines, pure
constructor-based DI).

---

## 5. Tight coupling

**Status:** clean.

### 5.1 Interface-based dependencies

Verified by re-reading `AppLauncher.cs` (composition root) and
`MainViewModel.cs` (top consumer). Every service is declared on the
interface side:

- `IImageRenderingService renderingService = new ImageRenderingService();`
  (`AppLauncher.cs:28`)
- `IDebugExportService debugService = new DebugExportService();`
  (`AppLauncher.cs:29`)
- `IDVHCalculation dvhService = new DVHService();` (`AppLauncher.cs:30`)
- `ISummationServiceFactory? _summationServiceFactory` field on
  `MainViewModel`

Concrete `new` of services only happens inside `AppLauncher.Launch` and
inside `SummationServiceFactory.Create` (the documented composition root
+ factory pair). This is the correct shape.

### 5.2 Project-level dependency graph

Verified by reading every `.csproj`:

```
EQD2Viewer.Core            ← (no project refs)
EQD2Viewer.Services        ← Core
EQD2Viewer.App             ← Core, Services
EQD2Viewer.Esapi           ← Core, Services, App, Stubs (Stubs only when no real ESAPI)
EQD2Viewer.Fixtures        ← Core
EQD2Viewer.DevRunner       ← Core, Services, App, Fixtures
EQD2Viewer.FixtureGenerator← Core, Esapi, Stubs (when no real ESAPI)
EQD2Viewer.Tests           ← Core, Services, App, Fixtures
```

Acyclic, unidirectional. No reverse dependency from Core/Services into App
or Esapi.

### 5.3 File I/O placement

Direct `File.*` / `Directory.*` use:

| File | Justified? |
|---|---|
| `EQD2Viewer.Core/Serialization/SnapshotSerializer.cs` | **yes** — explicit JSON archive read/write is its declared responsibility. |
| `EQD2Viewer.Core/Logging/SimpleLogger.cs` | **yes** — file-backed logger is the declared responsibility. |
| `EQD2Viewer.Services/**` | none — services do no direct file I/O. |

Both Core users are documented I/O modules with bounded scope; this is the
canonical pattern for "I/O at the boundary, not in algorithms".

### 5.4 `Application.Current` and statics

`Application.Current` only appears in
`EQD2Viewer.App/UI/ViewModels/MainViewModel.Rendering.cs` (UI layer) and
in `MainViewModel.Summation.cs` (UI layer). No leakage into Core/Services.

No singleton `static` instances of services found.

### 5.5 Cross-layer coupling smells

None found. Specifically:

- Core does not reference any Services type (`ISummationService`,
  `IDVHCalculation` etc. **are defined in** `EQD2Viewer.Core/Interfaces/`,
  which is correct — they are contracts, not implementations).
- Services does not reference any App type.
- Esapi does not leak ESAPI types into the domain — `EsapiDataSource` and
  `EsapiSummationDataLoader` return `Core.Data.*` POCOs.

---

## Stale rules

- The original brief listed "App depends on Core and Services (and
  Registration interfaces)". `EQD2Viewer.Registration` and
  `EQD2Viewer.Registration.ITK` were removed in 0.9.4-beta. Update the
  architectural docs (e.g. CLAUDE.md, this audit's rule list, the
  regulatory architecture doc) to drop the parenthetical.

---

## Summary

| Rule | Status | Findings |
|---|---|---|
| 1. Core depends on nothing | **PASS** | Zero `VMS.TPS` / `System.Windows` imports. |
| 2. Services depends only on Core | **PASS** | csproj + import grep both clean. |
| 3. App depends on Core + Services | **PASS** | csproj graph acyclic. |
| 4. ESAPI confined to `EQD2Viewer.Esapi` | **PASS** | Stubs and FixtureGenerator are documented exceptions. |
| 5. No reflection loading | **PASS** | Removed with ITK; no `Assembly.Load*` / `Activator.CreateInstance` / `DllImport` left. |
| 6. Interface-based DI | **PASS** | Composition only inside `AppLauncher` + factory. |
| 7. No bidirectional deps | **PASS** | Strictly inward-pointing. |
| 8. WPF in App only | **PASS** | One doc-comment mention in Core, no real usage. |
| 9. File I/O bounded | **PASS** | `SnapshotSerializer`, `SimpleLogger` only. |
| 10. No cross-layer coupling | **PASS** | Concrete `new` only in composition root. |

**Counts:** 0 violations · 0 smells · 1 stale rule (the "Registration
interfaces" parenthetical).

### Top issues

There are no architectural issues to address. Two optional follow-ups:

1. **Drop the stale "Registration interfaces" rule** from any project
   docs / CLAUDE.md / `docs/regulatory/04-architecture.md` references.
2. **Codify rule 4 as a build-time check.** Add a `Directory.Build.props`
   guard so any future project trying to add a `VMS.TPS.*` reference
   outside `EQD2Viewer.Esapi`, `EQD2Viewer.Stubs`,
   `EQD2Viewer.FixtureGenerator` fails the build. Cheap insurance against
   future drift.

### Positive observations

- `AppLauncher.cs` is a textbook composition root: one place where
  concrete services are constructed, every binding is interface-typed,
  no reflection.
- The ITK cleanup left no architectural residue — no orphaned
  reflection, no dead `IRegistrationService` references, no stale
  `Release-WithITK` conditional in any csproj.
- `IsodoseLevelData` (Core POCO) / `IsodoseLevel` (App WPF-bindable peer)
  is a good model for future cases where a domain type needs a
  WPF-bindable counterpart.
- ESAPI types do not leak through the adapter — both
  `EsapiSummationDataLoader` and `EsapiDataSource` return
  `Core.Data.VolumeData` / `Core.Data.StructureData` POCOs to their
  callers, never raw `VMS.TPS.*` types.
