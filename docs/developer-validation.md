# Developer review and verification — WinRT / XAML Native AOT (#1, PR #2)

This guide is for **reviewers and application developers**, not an assertion that every runtime acceptance criterion is already satisfied. The source generator emits C# members for XAML binding and dependency properties; **CsWinRT** (C#/WinRT, the Windows Runtime interop toolchain) generates the actual WinRT ABI (Application Binary Interface) exposure. **Native AOT** compiles managed code into a native executable ahead of time. A successful generator compile alone does not prove an AOT executable can bind at runtime.

Primary references: [issue #1](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.SourceGeneration/issues/1), [PR #2](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.SourceGeneration/pull/2), [compatibility rules and diagnostics](native-aot.md), [consuming app AOT instructions](https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered/blob/feat/aot-compatible/docs/aot-readiness.md).

## 1. Scope and acceptance gates

| Gate | What it establishes | Source of evidence |
| --- | --- | --- |
| **A — Roslyn** | Analyzer reports correct diagnostics; emitted C# compiles against minimal contracts; old static bindings and unload semantics are retained | This repository's regression harness and `generator-tests` CI job |
| **B — Actual Windows build** | The **exact** reviewed generator package is consumed by the application, and generated code builds using the real Windows App SDK / pinned CsWinRT 2.x | Windows build logs, project/package versions and generated source |
| **C — Published Native AOT** | Fresh native binaries are produced, have the expected deployment files and do not silently fall back to managed CoreCLR / ReadyToRun | `dotnet publish` log and `Test-NativePublish.ps1` output |
| **D — UI runtime** | Runtime `{Binding}`, `DisplayMemberPath`, compiled `{x:Bind}`, commands, DPs and indexers actually read and update values in the **published** executable | Manual UI results, diagnostics, repro screenshots or recordings |
| **E — CsWinRT 3.x** | A separately pinned 3.x toolchain compiles and runs representative shapes with its actual ABI/build-task implementation | Separate preview build logs and runtime results; do **not** infer from the `CSWINRT3_0` conditional test |

**Do not close #1 based only on gate A.** The source generator does not automatically discover all actual WinRT dynamic cast sites or closed generic/boxed ABI instantiations. Issues in application call sites must be reported separately and investigated with concrete AOT diagnostics.

## 2. Run regression checks (Linux, macOS, Windows)

Install the .NET 10 SDK (the repository's CI uses `10.0.x`). From the source-generator repository root:

```powershell
dotnet --info
dotnet restore tests/Snap.Hutao.Remastered.SourceGeneration.Tests/Snap.Hutao.Remastered.SourceGeneration.Tests.csproj
dotnet run -c Release --project tests/Snap.Hutao.Remastered.SourceGeneration.Tests/Snap.Hutao.Remastered.SourceGeneration.Tests.csproj
dotnet pack src/Snap.Hutao.Remastered.SourceGeneration/Snap.Hutao.Remastered.SourceGeneration/Snap.Hutao.Remastered.SourceGeneration.csproj -c Release
```

Expect the harness to end with `All source generator AOT regression checks passed.`, with exit code **0**. A failing Roslyn diagnostic assertion, a C# compile error, or a failed package build is a **gate A failure**. CI executes the harness from `.github/workflows/verify.yml`.

Review source and generated output for these regression cases:

- Public **instance and static properties**: `GetProperty("Kind")` must return a descriptor for a static `Kind`; a public setter is retained where present.
- **Static `[Command]` methods**: the generated command *property* must remain discoverable as a bindable descriptor, even though no instance receiver is required to read it.
- **Derived member hiding**: a derived `Name` replaces the base descriptor; a derived `string IsViewUnloaded` must **not** inherit a base unload guard.
- **Unload guard**: a readable `IsViewUnloaded.Value : bool` suppresses writes after unload; direct and inherited `Value` work. Private base flags, inaccessible getters and static `Value` must **not** generate invalid access expressions.
- Public **single-parameter indexers** work; both direct and inherited multi-parameter indexers report `SHSGAOT005` and are not generated.
- Conflicting providers `[BindableCustomPropertyProvider]` + `[WinRT.GeneratedBindableCustomProperty]` report `SHSGAOT002`; the generator emits no duplicate binding interface.
- Required `partial` declarations in both the model and its containing types are diagnosed; duplicated dependency property names report `SHSGAOT007`.
- Instance DP owners inherit `DependencyObject`; attached DP registrations compile without that restriction. In CsWinRT 2.x **OptIn**, `SHSGAOT003` calls for source-visible `[WinRT.GeneratedWinRTExposedType]`, whereas the synthetic 3.x conditional does not warn.

The test harness intentionally uses **minimal fake** WinUI/CsWinRT contracts. It does **not** compile against the real Windows App SDK or validate ABI marshalling.

## 3. Stage the exact reviewed package for the application

**Do not test NuGet release `1.3.17` and claim to have tested PR #2.** As of this guide, the application branch `feat/aot-compatible` centrally pins `Snap.Hutao.Remastered.SourceGeneration` **1.3.17**, which does not include these PR changes.

Recommended safe local workflow (Windows PowerShell; separate source-generator and application checkouts):

```powershell
# Run in the source-generator checkout, with PR #2 checked out.
$feed = Join-Path $PWD "artifacts/local-nuget"
New-Item -ItemType Directory -Force -Path $feed | Out-Null
dotnet pack src/Snap.Hutao.Remastered.SourceGeneration/Snap.Hutao.Remastered.SourceGeneration/Snap.Hutao.Remastered.SourceGeneration.csproj -c Release -p:PackageVersion=1.3.18-aotreview.1 -o $feed
```

In the **application checkout only**, temporarily change `Directory.Packages.props` to use `1.3.18-aotreview.1` for `Snap.Hutao.Remastered.SourceGeneration`. Add `$feed` as an **additional NuGet source** to your local NuGet configuration. Keep the application's other package sources (this is not an invitation to replace `NuGet.Config` or alter production dependency versions).

Verify that `obj/project.assets.json` resolves **the locally packed** generator package, not a cached 1.3.17. Record the package version, branch and commit SHA. Restore the application's original `Directory.Packages.props` and local feed configuration after validation; do not commit these temporary pinning edits to the consuming application.

Alternative: use an explicit local analyzer project reference in a disposable application validation branch; ensure only **one** version of the source generator is loaded. Merely checking out the PR branch alongside the app does not change the app's NuGet package.

## 4. Compile and publish with the real Windows toolchain

Prerequisites: Windows, .NET 11 SDK, Windows SDK and VC++ toolchain required by the application, its Windows App SDK dependencies, and the separately staged generator. Verify the currently checked out application branch's pinned versions; it currently uses **CsWinRT 2.3.1** and **Windows App SDK 2.6.6-experimental**, with `win-x64` and `net11.0-windows10.0.26100.0`.

From the **application repository root**:

```powershell
$project = "src/Snap.Hutao.Remastered/Snap.Hutao.Remastered/Snap.Hutao.Remastered.csproj"
$nativeOutput = Join-Path $PWD ("artifacts/aot/sourcegen-review-" + (Get-Date -Format yyyyMMdd-HHmmss))
dotnet restore $project -r win-x64 -p:PublishAot=true -p:PublishReadyToRun=false
dotnet publish $project -c Debug -p:Platform=x64 -r win-x64 -p:PublishAot=true -p:PublishReadyToRun=false -p:SelfContained=true -p:JsonSerializerIsReflectionEnabledByDefault=false "-p:PublishDir=$nativeOutput/"
./scripts/Test-NativePublish.ps1 -PublishDirectory $nativeOutput
```

Use a **fresh output directory** and matching restore properties. `dotnet build -p:PublishAot=true` is **not** proof of a native AOT publication; stale `native` outputs must not be reused. The application already configures `CsWinRTAotWarningLevel=3` for `AotAnalysis=true`; to reproduce the stricter analyzer scenario, add `-p:AotAnalysis=true` to a **separate** compile/analysis run, without disabling analysis or swallowing warnings.

If external dependencies or unrelated application AOT errors block publishing, report them verbatim and mark gates C/D as **blocked**, rather than `PASS`. Do not suppress diagnostics or mutate application behavior just to make this generator's verification green.

## 5. Manual runtime cases on the published executable

Execute under a disposable / isolated profile if appropriate (the application documents `SNAP_HUTAO_PROFILE_ROOT` for unpackaged launches). No actual credentials or private user data are required for the core binding cases.

| Test | Action | Expected behavior |
| --- | --- | --- |
| Runtime `{Binding}` | Navigate to a page/template using an annotated view model and a runtime property path; also inspect a `DisplayMemberPath` / `TextMemberPath` consumer | Values appear and refresh; no missing `ICustomPropertyProvider` member or blank item labels |
| Compiled `{x:Bind}` | Open a page with generated DP / view-model property binding and change its source value | Compiled binding reads the right member; OneWay / TwoWay changes propagate according to the XAML mode |
| Static property / command | Bind to an annotated model with a static `Kind` and a generated static `[Command]`; invoke command | `GetProperty` resolves both; command execution works with no reflection-only fallback |
| Unload flag | Change a property, unload the view, then attempt a late property update through the binding descriptor | Guarded setter does **not** write after `IsViewUnloaded.Value` becomes true |
| Indexer | Bind to a public single-parameter indexer, including one inherited from a base type | Correct value read/write; no unexpected bindable descriptor ambiguity |
| Dependency properties | Read/write an instance DP on a `DependencyObject` control and an attached DP | Correct value/accessor behavior; no missing `GetValue`/`SetValue` or cast failure |
| Boxed / generic values | Exercise a real UI source with a closed WinRT generic or boxed primitive / mapped collection | No `InvalidCastException`, `MissingMethodException` or missing ABI exposure; record the **exact closed type** if failed |

Confirm **both** initial reads and changes after notification. Attach the screenshot, app log, generated source or precise exception for each failure. Test the same key paths on normal (non-AOT) build to detect unintended JIT/ReadyToRun regressions.

### CsWinRT 3.x — separate exploratory gate

Use a **different disposable validation branch** and a concrete, published CsWinRT 3.x preview version. Record its version, SDK-generated `CSWINRT3_0` symbol and actual compilation/build-task behavior. Do **not** simply define `CSWINRT3_0` manually in the 2.x application or replace an SDK package in-place and call it 3.x validation.

Specifically check which 2.x `DynamicWindowsRuntimeCast` metadata paths 3.x replaces, whether `IBindableCustomPropertyImplementation` remains compatible with its binding generator, and whether ABI registration still needs explicit root metadata. Do not remove 2.x compatibility annotations before an equivalent 3.x path is proven.

## 6. Reviewer results report (copy into the PR or issue)

```text
Generator commit / package version:
Application commit / branch:
.NET SDK / Windows SDK / VC++:
Windows App SDK / CsWinRT exact versions:
CsWinRT optimizer setting (Auto/OptIn) and warning level:
Command / log artifact links:

[ ] A Roslyn harness + pack — PASS / FAIL
[ ] B Real WinUI / CsWinRT build — PASS / FAIL / BLOCKED
[ ] C Fresh Native AOT publish + Test-NativePublish — PASS / FAIL / BLOCKED
[ ] D Runtime {Binding}, DisplayMemberPath, x:Bind, DP, command,
    inherited indexer, static member, unloaded-view guard — PASS / FAIL / BLOCKED
[ ] E Separate CsWinRT 3.x preview — PASS / FAIL / NOT TESTED

Failure location / HRESULT / exception:
Generated code excerpt or reproducer:
Normal (non-AOT) baseline result:
Remaining risks and owner:
```

**Merge decision:** Gate A is automated. Gates B–D require a real Windows application and published Native AOT; gate E is a separate preview migration study. A green Linux CI does **not** qualify as full completion of issue #1.
