# Snap.Hutao.Remastered.SourceGeneration
Source Code Generator for Snap.Hutao.Remastered

# Development Guideline

Use https://roslynquoter.azurewebsites.net/ to get SyntaxTree

1. Every `IncrementalValue(s)Provider<T>`'s step result should be an `IEquatable<T>` to make it really becomes incremental.
2. So the intermediate models should be a `record (class/struct)` if possible
3. Intermediate array/enumerable should be a `ImmutableArray<T>` if possible, the pipeline use IA internally and has special check for it.
## Native AOT and WinUI

For CsWinRT 2.x/3.x compatibility requirements, XAML binding contracts, diagnostics, and Roslyn smoke tests, see [Native AOT compatibility](docs/native-aot.md).

For reproducible build commands, reviewer test cases, evidence requirements, and a **manual Windows Native AOT acceptance checklist**, see [Developer review and validation](docs/developer-validation.md).
