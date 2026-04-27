# Refactoring Backlog

Synthesized from `docs/duplication-audit.md`, `docs/clean-architecture-audit.md`,
`docs/dead-code-audit.md`, and `docs/hot-paths.md`.

## Sorting

Items are sorted by **priority score** (lower = do first):

```
priority_score = effort_score / inverse_risk_score
  effort_score:        S = 1   M = 2   L = 3
  inverse_risk_score:  LOW = 3 MED = 2 HIGH = 1
```

Cheapest-and-safest wins float to the top. Anything that touches a
method listed in `docs/hot-paths.md` is automatically **HIGH** risk
and tagged **PERFORMANCE-RISK**.

## Test-coverage labels

- **covered** — existing tests will catch a behavioural regression
  introduced by the refactor.
- **partly** — existing tests catch the most plausible breakage but
  leave a gap (e.g. UI behaviour, perf, edge cases).
- **needs tests** — no existing test exercises the change; add a
  small targeted test as part of the first commit.
- **not covered** — no tests are practical (e.g. WPF visual rendering,
  doc-only changes); rely on visual or build verification.

---

## Tier 1 — score 0.33 (S/LOW): do first, in any order

### 1.1 Delete `MainViewModel.WireIsodoseLevelEvents` / `OnIsodoseLevelChanged` / `_isodoseLevelArray` mirror
- **Effort:** S
- **Risk:** LOW
- **Coverage:** covered (build fails if any caller existed; existing rendering and DVH tests prove the live `DoseOverlayViewModel` peer is the real source of truth)
- **First commit:** *"Delete dead `WireIsodoseLevelEvents`, `OnIsodoseLevelChanged`, and `_isodoseLevelArray` mirror from MainViewModel"* — drop the three members at `MainViewModel.cs:114, 145-179` and `MainViewModel.Properties.cs:144`; rerun `dotnet build` and `dotnet test`.
- **References:** Duplication Family 13, Dead-code report Cat 1.

### 1.2 Add `SimpleLogger.Warning` to three silent catches in `JsonDataSource.cs`
- **Effort:** S
- **Risk:** LOW
- **Coverage:** needs tests (no existing test covers a malformed fixture file; add a small fixture with a corrupted dose slice to verify the warning fires)
- **First commit:** *"Log per-fixture parse failures in JsonDataSource"* — replace `catch { }` at `JsonDataSource.cs:177, 268, 299` with `catch (Exception ex) { SimpleLogger.Warning($"Failed to parse {file}: {ex.Message}"); }`.
- **References:** Dead-code report Cat 5 (the only **new** finding from that report).

### 1.3 Add `SimpleLogger.Warning` to silent catch in `PlanSummationDialog.IndexAllRegistrations`
- **Effort:** S
- **Risk:** LOW
- **Coverage:** not covered (UI dialog code; manual smoke test sufficient)
- **First commit:** *"Log indexing failures in PlanSummationDialog"* — replace `catch { }` at `PlanSummationDialog.xaml.cs:55` with the same shape as 1.2.
- **References:** Duplication Family 12, Dead-code report Cat 5.

### 1.4 Delete unused `IsodoseLevel(IsodoseLevelData)` ctor and `IsodoseLevel.ToData()`
- **Effort:** S
- **Risk:** LOW
- **Coverage:** covered (build fails if any caller existed; Grep confirms zero call sites)
- **First commit:** *"Remove unused IsodoseLevelData↔IsodoseLevel conversion methods"* — delete `IsodoseLevel.cs:132-148` and trim `IsodoseLevelData` if it becomes a pure DTO with no producer/consumer (decision: keep as Core POCO for serialization, delete only the unused App-side bridge).
- **References:** Duplication Family 15.

### 1.5 Drop stale "Registration interfaces" rule from architecture docs
- **Effort:** S
- **Risk:** LOW
- **Coverage:** n/a (docs only)
- **First commit:** *"Remove obsolete Registration interfaces reference from architecture docs"* — search `docs/regulatory/` and any CLAUDE.md for the phrase, delete or rewrite.
- **References:** Clean-architecture audit, "Stale rules" section.

### 1.6 Centralise `JsonSerializerOptions`
- **Effort:** S
- **Risk:** LOW
- **Coverage:** covered (FixtureFormatTests exercise both read paths)
- **First commit:** *"Extract shared JsonOpts constant in EQD2Viewer.Fixtures"* — move the identical declarations from `FixtureLoader.cs:23-28` and `JsonDataSource.cs:23-28` into a single `internal static class JsonOpts` in `EQD2Viewer.Fixtures`, both read-path files reference it.
- **References:** Duplication Family 16.

### 1.7 Extract BOM-stripping JSON read helper
- **Effort:** S
- **Risk:** LOW
- **Coverage:** needs tests (add a test fixture file with a UTF-8 BOM)
- **First commit:** *"Add JsonFileHelper.ReadJsonStripBom in Core.Serialization"* — collapse `FixtureLoader.cs:188-195` and `JsonDataSource.cs:379-385` to a single static helper.
- **References:** Duplication Family 8.

### 1.8 Add shared `TestVolumeFactory` for unit tests
- **Effort:** S
- **Risk:** LOW
- **Coverage:** covered (the tests are themselves the verification)
- **First commit:** *"Add EQD2Viewer.Tests/Common/TestVolumeFactory with MakeCt/MakeDose"* — replace the per-file `MakeReferenceCt` / `MakeDose` builders in `SummationServiceTests.cs:28-80` and `HotspotFinderTests.cs:14-38`.
- **References:** Duplication Family 10.

### 1.9 Extract `EsapiGeometryConverter` (`ToVolumeGeometry(Image)` / `(Dose)`, `ToVec3(VVector)`)
- **Effort:** S
- **Risk:** LOW
- **Coverage:** partly (CI builds against ESAPI stubs; smoke tests via DevRunner)
- **First commit:** *"Add EsapiGeometryConverter static class"* — collapse the identical `ToGeometry`/`ToDoseGeometry`/`ToVec3` methods at `EsapiSummationDataLoader.cs:262-298` and `EsapiDataSource.cs:386-422` into one shared static.
- **References:** Duplication Family 6.

### 1.10 Extract `EsapiVoxelLoader.LoadVoxels(Image)` / `LoadVoxels(Dose)`
- **Effort:** S
- **Risk:** LOW
- **Coverage:** partly (same as 1.9)
- **First commit:** *"Add EsapiVoxelLoader for shared voxel bulk-load"* — collapse the four identical `for (z) { vox[z] = new int[X,Y]; img.GetVoxels(z, vox[z]); }` blocks at `EsapiSummationDataLoader.cs:56-61, 87-92` and `EsapiDataSource.cs:124-129, 153-158`.
- **References:** Duplication Family 5.

### 1.11 Codify "VMS.TPS.* only in Esapi/Stubs/FixtureGenerator" as a build-time check
- **Effort:** S
- **Risk:** LOW
- **Coverage:** n/a (build infrastructure)
- **First commit:** *"Add Directory.Build.props guard restricting VMS.TPS references"* — add a `<Target>` that fails the build if any project not in the allowlist has a `<Reference Include="VMS.TPS.*">`.
- **References:** Clean-architecture audit, suggested follow-up #2.

---

## Tier 2 — score 0.50 (S/MED): one quick win that touches a hot path

### 2.1 Add `ImageUtils.RawToGy(int raw, DoseScaling s)` helper
- **Effort:** S
- **Risk:** MED (touches the rendering hot path indirectly via `PrepareDoseGrid` and `GetDoseAtPixel`)
- **Coverage:** covered (rendering pipeline tests + DoseResamplingIntegrationTests)
- **First commit:** *"Add ImageUtils.RawToGy thin wrapper to centralise dose scaling formula"* — replace the inline `(raw * RawScale + RawOffset) * UnitToGy` at `ImageRenderingService.cs:140-148, 395` and `SnapshotExporter.cs:85-86` with the helper. Verify rendered bitmaps are byte-identical before/after on the `octavius_50gy_25fx` fixture.
- **References:** Duplication Family 9.

---

## Tier 3 — score 0.67 (M/LOW): medium-effort safe wins

### 3.1 Extract `MatrixMath.BuildAffineFromBasisImages(Vec3, Vec3, Vec3, Vec3)`
- **Effort:** M
- **Risk:** LOW (startup-only path; correctness sensitive but well-tested by `ComputeAsync_AffineSourceIsPlan_InvertsMatrix`)
- **Coverage:** covered (existing affine summation tests pin the end-to-end behaviour)
- **First commit:** *"Add MatrixMath.BuildAffineFromBasisImages with unit tests"* — first add the helper *with* a test that pins the same identity-+-translation matrix the three current sites would emit, *then* wire the three call sites at `EsapiDataSource.cs:309-333`, `EsapiSummationDataLoader.cs:208-231`, `FixtureExporter.cs:489-525` in a follow-up commit.
- **References:** Duplication Family 11.

### 3.2 Extract `EsapiContourExtractor.Extract(StructureSet, int zSize)`
- **Effort:** M
- **Risk:** LOW (startup-only path)
- **Coverage:** partly (StructureRasterization integration tests verify the downstream consumer; the loader itself is exercised only via stubs)
- **First commit:** *"Add EsapiContourExtractor for shared per-slice contour walk"* — collapse `EsapiSummationDataLoader.cs:131-193` and `EsapiDataSource.cs:193-251` into a single helper. Keep the per-slice silent catch in the helper (it's the legitimate-silent case).
- **References:** Duplication Family 7.

---

## Tier 4 — score 1.00 (M/MED): one moderate refactor

### 4.1 Extract `DVHCalculator.BinToHistogram(...)`
- **Effort:** M
- **Risk:** MED (DVH math is on the per-DVH-render path but not the unsafe rendering path)
- **Coverage:** covered (DVHIntegrationTests + SummationServiceTests cover both call sites end-to-end)
- **First commit:** *"Add DVHCalculator.BinToHistogram with parameterised bin-edge tests"* — extract the binning loop shared by `SummationService.ComputeStructureEQD2DVH` (lines 341-381) and `DVHService.CalculateDVHFromSummedDose` (lines 62-94) into a Core helper. Pin the bin-edge convention with a test before extracting.
- **References:** Duplication Family 3.

---

## Tier 5 — score 1.50 (L/MED): a chunky XAML migration

### 5.1 Migrate XAML bindings off `MainViewModel` forwarders to `DoseOverlay.*` directly
- **Effort:** L
- **Risk:** MED (no compiler check on XAML binding paths — runtime-only failures)
- **Coverage:** not covered (WPF visual; manual smoke testing required)
- **First commit:** *"Migrate MainWindow.xaml DoseDisplayMode bindings to DoseOverlay path"* — pick **one** binding path (e.g. `DoseDisplayMode`), update XAML, run the DevRunner against `octavius_50gy_25fx`, verify the overlay changes mode as before. Repeat per binding in subsequent commits. **Do not delete the forwarders until every binding has migrated and the DevRunner is confirmed working.**
- **References:** Duplication Family 14, Dead-code report Cat 4.

---

## Tier 6 — score 2.00 (M/HIGH): performance-risk, do with benchmarks

### 6.1 Consolidate isodose Fill/Colorwash inner loops (Family 1)
- **Effort:** M
- **Risk:** HIGH — **PERFORMANCE-RISK** (touches `ImageRenderingService.RenderFillMode` / `RenderColorwashMode` and the duplicate inline copy in `MainViewModel.Summation.RenderSummedDoseBitmap`; both are unsafe per-pixel loops listed in `docs/hot-paths.md` §3 and §4)
- **Coverage:** covered for behavioural correctness (`RenderingPipelineTests`); **not benchmarked**
- **First commit:** *"Make ImageRenderingService.RenderFillMode/RenderColorwashMode reusable from MainViewModel.Summation"* — first widen the existing service methods so they accept a pre-locked `byte*` and stride (no behaviour change), benchmark the unchanged path on `octavius_50gy_25fx`, then in a follow-up commit replace the duplicate body in `MainViewModel.Summation.cs:419-461`. Re-benchmark; require <5% delta.
- **References:** Duplication Family 1; hot-paths §3, §4.

### 6.2 Extract private `SummationService.AccumulateEQD2Slice` helper (Family 4)
- **Effort:** M
- **Risk:** HIGH — **PERFORMANCE-RISK** (innermost summation triple-loop; ~10⁸ iterations per compute)
- **Coverage:** covered (SummationServiceTests pins compute outputs end-to-end); **not benchmarked**
- **First commit:** *"Extract SummationService.AccumulateEQD2Slice as private static helper, no behaviour change"* — pull the identical inner pass from `ComputeCore` (lines 172-187) and `RecomputeEQD2DisplayAsync` (lines 269-290) into a single private static. Benchmark `ComputeAsync` Release-mode on the fixture before and after; require <5% delta. The masked variant in `ComputeStructureEQD2DVH` (lines 354-369) gets folded in only after the unmasked extraction lands and benchmarks cleanly.
- **References:** Duplication Family 4; hot-paths §1.

---

## Tier 7 — score 3.00 (L/HIGH): largest commitments, plan separately

### 7.1 Extract world↔voxel coordinate-transform helper (Family 2)
- **Effort:** L
- **Risk:** HIGH — **PERFORMANCE-RISK** (six call sites across four files; the most-hit code path in the codebase via `BilinearSampleRaw`)
- **Coverage:** covered for behaviour (`SummationServiceTests` end-to-end); **not benchmarked, no micro-tests on the projection math**
- **First commit:** *"Add CoordinateTransform stride-packet helper in Core.Calculations with micro-tests"* — first add the helper as a *new* class with unit tests that pin its output against the current `AccumulatePhysicalDirect` math for known inputs. **Do not yet wire any call site.** Subsequent commits replace one call site at a time, each with a Release-mode benchmark vs. the previous commit. Final commit (after all six sites use the helper) deletes the inline duplications. Plan: ~6 commits.
- **References:** Duplication Family 2; hot-paths §1, §2.

### 7.2 Introduce `SummationService.PreparedState` to retire null-forgiving cluster (Family 17)
- **Effort:** L
- **Risk:** HIGH — **PERFORMANCE-RISK** (changes the lifecycle of `_cachedPlans`, `_summedSlices`, `_refGeo`, `_config`; all on the hot summation path)
- **Coverage:** covered for behaviour; lifecycle invariants are currently proved by the `!` operators not by tests
- **First commit:** *"Add SummationService.PreparedState wrapper, hold non-null fields after PrepareData"* — introduce a record/struct that carries the four fields once `PrepareData` succeeds; methods that previously asserted with `!` now take the wrapper as a parameter. The first commit only adds the wrapper next to existing nullable fields, no `!` removed yet. Subsequent commits migrate one method at a time. Each migration commit must benchmark `ComputeAsync` against the prior commit; require <2% delta because the change is on every voxel access.
- **References:** Duplication Family 17; hot-paths §1.

---

## Items intentionally not in the backlog

- **Recorded but not actioned items from `docs/duplication-audit.md` Category D** (MainViewModel partial fragmentation, SummationService monolith, ITK reflection-already-removed verification). These are recorded for awareness; no refactor proposed.
- **Family 12 *legitimately-silent* catches** in per-slice contour iteration (`EsapiSummationDataLoader.cs:181`, `EsapiDataSource.cs:239`, `FixtureExporter.cs:318`). Keep the silence; keep the explanatory comments. Touching them would add log noise per slice per structure.
- **Tier 5 forwarders → XAML migration** is in the backlog as 5.1 but only as a *gradual* per-binding effort, not as one big-bang refactor.

---

## Suggested execution order

1. **Burn down Tier 1** (eleven 0.33 items). Each is independent; can be done as separate small PRs in any order, or batched 3–4 per PR by topic. Net: ~150 LoC removed, two new actionable warning logs, three small extracted helpers, two doc/build hygiene items.
2. **Tier 2 single item.** Establishes the `RawToGy` helper that Tier 3.1 / 6.1 might consume.
3. **Tier 3 in pairs.** Both Tier 3 items add tests *before* extraction — keep that discipline.
4. **Tier 4** as a single PR.
5. **Tier 5** opened as a tracking issue with a per-binding checklist; merged piecewise over multiple weeks.
6. **Tier 6 and Tier 7** each gated on a one-time investment in a benchmark harness (`BenchmarkDotNet` or a hand-rolled stopwatch over `ComputeAsync` on the `octavius_50gy_25fx` fixture). Without that harness the **PERFORMANCE-RISK** tag cannot be discharged honestly.

---

## Open questions for the user before starting

1. Is there an appetite to add a `BenchmarkDotNet` project before Tier 6 / 7, or do you want to gate those tiers indefinitely until separate benchmark work?
2. Does Tier 5 (XAML migration) have a deadline, or can it stay deferred?
3. Tier 1.4 (Family 15) deletes the `IsodoseLevel(IsodoseLevelData)` ctor on the assumption no future serialization use is planned. Confirm before deleting.
