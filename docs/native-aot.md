# WinRT / XAML Native AOT compatibility

This repository generates application code, **not** CsWinRT's ABI (Application Binary Interface) vtables. A generated partial class declaration alone does not force CsWinRT 2.x to recognize a type during the same source-generation pass.

## Compatibility matrix

| Contract | CsWinRT 2.3.1 (application AOT branch) | CsWinRT 3.0 preview |
| --- | --- | --- |
| Custom `[BindableCustomPropertyProvider]` | The existing generator produces direct, static `IBindableCustomPropertyImplementation` property/indexer delegates; the owning type and containing types must be `partial`. | The **custom** generator still needs `partial` to add members, even though CsWinRT's own ABI generator no longer generally requires `partial`. |
| CsWinRT `[GeneratedBindableCustomProperty]` | Use on ordinary `partial` models bound via runtime `{Binding}` when no custom binding provider is used. | Check the CsWinRT 3.x binding implementation before migrating; do not combine both generators. |
| `[GeneratedWinRTExposedType]` | In `CsWinRTAotOptimizerEnabled=OptIn`, put this on the **source** declaration of custom binder types crossing the WinRT boundary. `Auto` performs its own analysis. | CsWinRT 3.x moves AOT analysis to a build task, so the 2.x OptIn warning is suppressed with `CSWINRT3_0`. |
| `[DependencyProperty<T>]` | Generated instance properties call `GetValue`/`SetValue`; the declaring type must inherit `Microsoft.UI.Xaml.DependencyObject`. Attached registrations may reside on any partial owner. | The dependency-property API surface is unchanged here. |
| `DynamicWindowsRuntimeCast` | Apply only at actual 2.x **dynamic WinRT cast** call sites when CsWinRT reports a trim-unsafe cast. This generator cannot safely infer them from a DP's property type. | Do not blindly emit 2.x casts: review the actual CsWinRT 3.x marshalling pipeline. |
| Closed WinRT generic/boxed types | CsWinRT `Auto` can discover many uses; in `OptIn`, use source-level assembly `[GeneratedWinRTExposedExternalType(typeof(...))]` for confirmed ABI boundary types. | Must be checked with the build-task output. |

The `CSWINRT3_0` preprocessor symbol is defined by the CsWinRT 3.x SDK. Its presence is **not** inferred from an assembly version string.

## Generator diagnostics

- **SHSGAOT001** — A generated XAML member would require a non-`partial` class or containing declaration. No invalid partial companion is emitted.
- **SHSGAOT002** — `BindableCustomPropertyProvider` conflicts with `GeneratedBindableCustomProperty` on the same class. Choose exactly one.
- **SHSGAOT003** — In explicit CsWinRT 2.x `OptIn` mode, a custom binding provider lacks `GeneratedWinRTExposedType` on the source declaration.
- **SHSGAOT004** — A non-attached dependency property has an owner which does not inherit `DependencyObject`.
- **SHSGAOT005** — The direct custom binder accepts only single-parameter indexers.
- **SHSGAOT006** — XAML generation failed; a stable diagnostic replaces randomized invalid `Error-<GUID>.g.cs` output.
- **SHSGAOT007** — Duplicate dependency property names would emit overlapping fields/accessors. Generation for that class is suppressed.

`SHSGAOT001` and `SHSGAOT002` are compile errors because generating an invalid or duplicate implementation could produce misleading compiler failures. `SHSGAOT003` is a warning because crossing the WinRT ABI is a runtime use-site property and not every custom provider necessarily crosses that boundary.

## Binding behavior

The custom binding generator still uses statically typed property access and `Unsafe.As`/`Unsafe.Unbox` (which do not require runtime reflection). It includes public properties from the owner and bases, with derived declarations hiding same-named base properties. A single-indexer type is emitted once. When the owner has an accessible `IsViewUnloaded.Value` boolean (as in the application's ViewModels), setter delegates preserve the unloaded-view guard; other binding models do not emit an invalid access to an undeclared `IsViewUnloaded` member.

`{Binding}` uses runtime property paths and needs an `ICustomPropertyProvider`-compatible implementation. `{x:Bind}` is compiled and does **not** automatically justify generating a runtime binding provider.

## Compile-time regression harness

```powershell
dotnet run --configuration Release --project tests/Snap.Hutao.Remastered.SourceGeneration.Tests/Snap.Hutao.Remastered.SourceGeneration.Tests.csproj
```

The harness drives Roslyn against minimal WinUI/CsWinRT contract stubs and checks generated syntax, C# compilation, diagnostics, nested partial declarations, public indexers, inherited binding members, attached dependency properties, and 2.x-versus-3.x preprocessor behavior. It runs on CI via `.github/workflows/verify.yml`.

**Limits:** Stubs and Roslyn compilation are *not* a Windows App SDK or Native AOT runtime test. Before closing issue #1, consume the published generator in the application's `feat/aot-compatible` branch, build against its pinned CsWinRT 2.3.1, set `CsWinRTAotWarningLevel=2`, publish a fresh Native AOT executable, and verify actual runtime `{Binding}`, `{x:Bind}`, generated commands, attached properties, and representative collections/boxed values. Repeat the compatibility subset against the supported CsWinRT 3.x preview separately. A PR workflow on Linux cannot prove these runtime paths.

### References

- https://github.com/microsoft/CsWinRT/blob/2.3.1.260716.1/docs/aot-trimming.md
- https://github.com/microsoft/CsWinRT/releases
- https://github.com/SnapHutaoRemasteringProject/Snap.Hutao.Remastered.SourceGeneration/issues/1
