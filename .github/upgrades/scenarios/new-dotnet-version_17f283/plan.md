# .NET Version Upgrade Plan for ReXGlue GUI

- Repository: `C:\Users\owner\source\repos\ReXGlue GUI`
- Solution: `ReXGlue GUI.slnx`
- Project(s) in scope: `ReXGlue GUI\ReXGlue GUI.csproj`
- Target framework: `net10.0-windows`

---

## Table of Contents

- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Project-by-Project Plans](#project-by-project-plans)
- [Package Update Reference](#package-update-reference)
- [Breaking Changes Catalog](#breaking-changes-catalog)
- [Testing & Validation Strategy](#testing--validation-strategy)
- [Risk Management](#risk-management)
- [Complexity & Effort Assessment](#complexity--effort-assessment)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

---

## Executive Summary

- Selected Strategy: **All-At-Once Strategy** — All projects will be upgraded simultaneously in a single coordinated operation.

Rationale:
- Total projects: 1 (small solution)
- Project already targets `net10.0-windows` per assessment; assessment shows no required code changes for framework upgrade.
- No NuGet package updates required (0 packages reported).
- Low complexity and limited surface area for regressions.

Scope:
- Upgrade verification and validation for `ReXGlue GUI\ReXGlue GUI.csproj`.
- Confirm that project file properties are consistent with `net10.0-windows` and that SDK and tooling are installed on the build machine.

Critical notes:
- Assessment reported `net10.0-windows` as the project's current target; treat this plan as validation and readiness for execution rather than a large code upgrade. If the project were to be moved from `net10.0` or earlier, the steps below still apply.

---

## Migration Strategy

### Approach
- Strategy chosen: **All-At-Once Strategy** (atomic upgrade across solution).
- Justification: Single-project solution, SDK-style project, no package compatibility issues reported, small codebase (≈792 LOC). The All-At-Once approach minimizes overhead and is appropriate for a small WinForms application.

### High-level Phases
- Phase 0: Preparation
  - Verify required .NET SDK (`net10.0`) installed on developer and CI machines.
  - Verify `global.json` (if present) is updated or compatible with target SDK.
- Phase 1: Atomic Upgrade (verification pass)
  - Confirm project file `TargetFramework`/`TargetFrameworks` is `net10.0-windows` as required.
  - Restore packages and perform full solution build.
  - Fix any compilation issues (none expected per assessment).
- Phase 2: Test Validation
  - Execute unit/integration tests (none discovered in assessment).
  - Manual smoke tests for UI flows (REXSDK env configuration, file dialogs, environment updates).
- Phase 3: Finalization
  - Final verification and merge of atomic upgrade commit.

Note: Because assessment shows the project already targets `net10.0-windows`, Phase 1 serves primarily to verify SDK availability and that local/CI environments compile and run.

---

## Detailed Dependency Analysis

Summary:
- Projects: 1 (`ReXGlue GUI.csproj`)
- Project type: WinForms, SDK-style
- Project dependencies: none (no project-to-project references)
- Dependency depth: 0 (leaf and root)
- Circular dependencies: none

Migration ordering:
- All projects simultaneously (single project) — no dependency ordering complexity.

---

## Project-by-Project Plans

### Project: `ReXGlue GUI\ReXGlue GUI.csproj`

**Current State**:
- Target framework: `net10.0-windows` (assessment)
- SDK-style: True
- Project kind: WinForms
- Files: 4
- LOC: ~792
- NuGet packages: 0

**Target State**:
- Remains `net10.0-windows` (verify consistency)

**Migration Steps (what the executor will perform)**:
1. Preparation
   - Ensure `dotnet` SDK for `net10.0` is installed on developer/CI machines.
   - Verify `global.json` if present; update to reference `net10.0` SDK if organizational policy requires a pinned SDK.
2. Verify project file
   - Open `ReXGlue GUI.csproj` and confirm `TargetFramework` or `TargetFrameworks` contains `net10.0-windows` and that `<UseWindowsForms>true</UseWindowsForms>` (or equivalent) is present.
3. Restore and Build
   - `dotnet restore` then `dotnet build` the solution.
4. Address compilation issues (none expected)
   - If compilation errors appear, consult the Breaking Changes Catalog below and the assessment. Resolve API or package issues.
5. Functional validation
   - Run the application manually; verify key flows: environment variable configuration, folder selection dialogs, adding release path to user `Path`.
6. Finalize
   - Commit atomic upgrade changes to an upgrade branch, push for review, and merge per source control guidance.

Validation checklist (per-project):
- [ ] Project file targets `net10.0-windows` and has WinForms support enabled
- [ ] Solution builds with 0 errors
- [ ] No package vulnerabilities remain
- [ ] Manual smoke test of main UI flows successful

---

## Package Update Reference

- Assessment reported 0 NuGet packages. No package updates are required as part of this upgrade.

---

## Breaking Changes Catalog

Assessment found no source/binary incompatible API issues for the project. Expected common categories to watch for (generic checklist for executors):

- API surface changes in .NET runtime between major versions (none detected here).
- Behavioral changes in file/IO/path handling — validate path normalization flows.
- WinForms runtime changes — ensure event wiring and message box behavior remain consistent.

⚠️ Although assessment reports no incompatibilities, the executor should verify behavior around environment variable updates and Path manipulation, as these operations interact with OS state and can vary by platform/permissions.

---

## Testing & Validation Strategy

Because this is a single-project small solution, testing emphasis is on build verification and manual UI validation.

Automated steps (if available in your environment):
- `dotnet restore` + `dotnet build` (CI)
- Execute any unit tests present (none reported)

Manual steps:
- Launch application and verify:
  - `EnsureRexsdkConfigured` flow — selecting valid base folder, and canceling behavior.
  - `Apply` flow — setting BaseSDKPath and REXSDK env when appropriate, confirmation dialogs.
  - `PopulateNewProjectRoot` and `UpdatePreview` behaviors for given base folder inputs.

Validation checklist:
- [ ] Solution builds successfully in local and CI environments
- [ ] Manual smoke tests complete and identical behavior observed compared to pre-upgrade
- [ ] No regressions in environment variable handling

---

## Risk Management

Risk summary:
- Overall risk level: **Low** (single small project, no package updates, assessment reports no compatibility issues)

Potential risks and mitigations:
- Risk: SDK not installed on CI/developer machines
  - Mitigation: Verify SDK presence; update `global.json` or CI images to include required SDK.
- Risk: OS permission issues when writing user environment variables or Path
  - Mitigation: Confirm account privileges and document that environment change may require user sign-out/sign-in to take effect.
- Risk: Behavioral differences in WinForms runtime
  - Mitigation: Manual smoke testing and quick revert if severe issues found.

Contingency:
- If unexpected blocking compatibility issues are discovered (compilation errors or regressions), revert the upgrade branch and investigate root causes. Because the repository is small, a revert is low-cost.

---

## Complexity & Effort Assessment

Per-project complexity (relative):
- `ReXGlue GUI` — Low
  - Small LOC (~792), no package updates, SDK-style project, no dependencies.

Execution complexity: Low — single atomic verification/upgrade pass.

---

## Source Control Strategy

- Create a single upgrade branch from `master`, e.g. `upgrade/net10.0-atomic-<timestamp>`.
- If any project files or `global.json` require modification, include them in one atomic commit.
- Commit message template: `chore(upgrade): verify/ensure project targets net10.0-windows`.
- Open a PR for review with link to this plan and assessment file.
- After successful verification and approvals, merge to `master` using the normal branch protection rules.

Note: Because this is an All-At-Once Strategy for a single project, keep commits atomic and minimal.

---

## Success Criteria

The migration is complete when all of the following are true:

1. Technical
   - `ReXGlue GUI\ReXGlue GUI.csproj` targets `net10.0-windows` (confirmed in project file).
   - Solution restores and builds successfully with 0 errors in local and CI environments.
   - No package dependency conflicts remain.
   - No security vulnerabilities reported for NuGet packages (assessment reported none).

2. Quality
   - Manual smoke tests for UI flows pass (environment variable flows, dialogs, Path updates).

3. Process
   - Upgrade branch created, PR opened and merged per repository policies.

---

## Notes & Next Actions (for the executor)

- Verify `dotnet --list-sdks` on developer/CI machines includes the required `net10.0` SDK. If not, install SDK from URL provided in assessment tool output.
- If you also intend to migrate UI from WinForms to WPF (user previously indicated interest), that migration is a separate modernization effort and is NOT part of this atomic .NET version verification/upgrade. The WPF migration requires additional planning and is outside the scope of this `net10.0` upgrade plan; consider opening a separate scenario for WinForms→WPF migration.

---

*Plan generated from assessment at `C:\Users\owner\source\repos\ReXGlue GUI\.github\upgrades\scenarios\new-dotnet-version_17f283\assessment.md`.*
