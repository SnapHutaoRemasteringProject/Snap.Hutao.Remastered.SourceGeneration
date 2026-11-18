// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Snap.Hutao.Remastered.SourceGeneration.Xaml;
using System.Collections.Immutable;

internal static class Program
{
    private const string Contracts = """
        using System;

        namespace Snap.Hutao.Remastered.Core.Annotation
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class BindableCustomPropertyProviderAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public sealed class DependencyPropertyAttribute<T> : Attribute
            {
                public DependencyPropertyAttribute(string name) { }
                public bool IsAttached { get; set; }
                public Type? TargetType { get; set; }
            }

            [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
            public sealed class CommandAttribute : Attribute
            {
                public CommandAttribute(string name) { }
            }
        }

        namespace WinRT
        {
            [AttributeUsage(AttributeTargets.Class)]
            public sealed class GeneratedBindableCustomPropertyAttribute : Attribute { }

            [AttributeUsage(AttributeTargets.Class)]
            public sealed class GeneratedWinRTExposedTypeAttribute : Attribute { }
        }

        namespace Microsoft.UI.Xaml.Data
        {
            public interface IBindableCustomPropertyImplementation
            {
                BindableCustomProperty? GetProperty(string name);
                BindableCustomProperty? GetProperty(Type indexParameterType);
            }

            public sealed class BindableCustomProperty
            {
                public BindableCustomProperty(
                    bool canRead, bool canWrite, string name, Type propertyType,
                    Func<object, object?>? getValue, Action<object, object?>? setValue,
                    Func<object, object, object?>? getIndexedValue,
                    Action<object, object?, object>? setIndexedValue) { }
            }
        }

        namespace Microsoft.UI.Xaml
        {
            public class DependencyObject
            {
                public object? GetValue(DependencyProperty property) => null;
                public void SetValue(DependencyProperty property, object? value) { }
            }

            public sealed class DependencyProperty
            {
                public static DependencyProperty Register(string name, Type propertyType, Type ownerType, PropertyMetadata metadata) => new();
                public static DependencyProperty RegisterAttached(string name, Type propertyType, Type ownerType, PropertyMetadata metadata) => new();
            }

            public sealed class PropertyMetadata
            {
                public static PropertyMetadata Create(object? defaultValue) => new();
            }
        }
        """;

    private static readonly MetadataReference[] References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path))
        .ToArray();

    private static void Main()
    {
        BinderHandlesSimpleModelsAndHiding();
        BinderPreservesViewModelUnloadGuard();
        InvalidPartialDeclarationsAreDiagnosedWithoutEmittingInvalidMembers();
        ConflictingProvidersAreDiagnosedWithoutDuplicateImplementations();
        OptInExposureOnlyAppliesToCsWinRT2();
        InvalidDependencyPropertyOwnerIsDiagnosed();
        AttachedDependencyPropertyCompiles();
        DerivedDependencyPropertyCompiles();
        DuplicateDependencyPropertiesAreDiagnosedAndSkipped();
        UnsupportedIndexersAreDiagnosedAndSkipped();

        Console.WriteLine("All source generator AOT regression checks passed.");
    }

    private static (CSharpCompilation Compilation, CSharpParseOptions ParseOptions) CreateCompilation(string source, bool csWinRT3 = false)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview, preprocessorSymbols: csWinRT3 ? ["CSWINRT3_0"] : []);
        SyntaxTree contracts = CSharpSyntaxTree.ParseText(Contracts, parseOptions, path: "Contracts.cs");
        SyntaxTree input = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Input.cs");
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedAotCompatibility",
            [contracts, input],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        return (compilation, parseOptions);
    }

    private static (string Source, Compilation Output) Generate(CSharpCompilation compilation, CSharpParseOptions parseOptions, IIncrementalGenerator generator)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create([generator.AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Verify(!generatorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Generator failed: " + string.Join("\n", generatorDiagnostics));
        string source = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
        return (source, output);
    }

    private static ImmutableArray<Diagnostic> Diagnose(CSharpCompilation compilation, bool optIn = false)
    {
        AnalyzerOptions options = new(ImmutableArray<AdditionalText>.Empty, new TestConfigProvider(optIn));
        return compilation.WithAnalyzers([new AotInteropAnalyzer()], options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static void AssertDiagnostic(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        Verify(diagnostics.Any(d => d.Id == id), "Missing " + id + ". Found: " + string.Join("; ", diagnostics));
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        Diagnostic[] errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Verify(errors.Length == 0, "Generated source does not compile:\n" + string.Join("\n", errors.Select(d => d.ToString())));
    }

    private static void Verify(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Count(string text, string pattern) => text.Split([pattern], StringSplitOptions.None).Length - 1;

    private static void BinderHandlesSimpleModelsAndHiding()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            internal class BaseModel
            {
                public string Name { get; set; } = "";
            }

            [BindableCustomPropertyProvider]
            internal sealed partial class Model : BaseModel
            {
                public new string Name { get; set; } = "";
                public string this[int index] { get => ""; set { } }
            }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        var (generated, output) = Generate(compilation, parseOptions, new BindableCustomPropertyGenerator());
        Verify(generated.Contains("IBindableCustomPropertyImplementation", StringComparison.Ordinal), "Binding interface not generated.");
        Verify(!generated.Contains("IsViewUnloaded", StringComparison.Ordinal), "Non-ViewModel must not access missing IsViewUnloaded.");
        Verify(Count(generated, "nameof(Name)") == 2, "Hidden inherited property generated duplicate binding cases.");
        Verify(generated.Contains("indexParameterType", StringComparison.Ordinal), "Indexer metadata missing.");
        AssertNoErrors(output);
    }

    private static void BinderPreservesViewModelUnloadGuard()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            public sealed class UnloadFlag { public bool Value { get; set; } }
            public class BaseModel { public UnloadFlag IsViewUnloaded { get; } = new(); }
            [BindableCustomPropertyProvider]
            internal sealed partial class Model : BaseModel { public string Name { get; set; } = ""; }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        var (generated, output) = Generate(compilation, parseOptions, new BindableCustomPropertyGenerator());
        Verify(generated.Contains("typedInstance.IsViewUnloaded.Value", StringComparison.Ordinal), "Existing unload guard lost.");
        AssertNoErrors(output);
    }

    private static void InvalidPartialDeclarationsAreDiagnosedWithoutEmittingInvalidMembers()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            internal class Container
            {
                [BindableCustomPropertyProvider]
                internal partial class Model { public string Text { get; set; } = ""; }
            }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(compilation), "SHSGAOT001");
        var (generated, _) = Generate(compilation, parseOptions, new BindableCustomPropertyGenerator());
        Verify(generated.Length == 0, "Generated a partial companion for a non-partial containing type.");
    }

    private static void ConflictingProvidersAreDiagnosedWithoutDuplicateImplementations()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;
            using WinRT;

            [BindableCustomPropertyProvider, GeneratedBindableCustomProperty]
            internal partial class Model { public int Value { get; set; } }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(compilation), "SHSGAOT002");
        var (generated, _) = Generate(compilation, parseOptions, new BindableCustomPropertyGenerator());
        Verify(generated.Length == 0, "Both binding providers generated the same interface.");
    }

    private static void OptInExposureOnlyAppliesToCsWinRT2()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            [BindableCustomPropertyProvider]
            internal partial class Model { public string Value { get; set; } = ""; }
            """;

        var (csWinRT2, _) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(csWinRT2, optIn: true), "SHSGAOT003");
        var (csWinRT3, _) = CreateCompilation(source, csWinRT3: true);
        Verify(Diagnose(csWinRT3, optIn: true).All(d => d.Id != "SHSGAOT003"), "3.x must not require the CsWinRT 2.x opt-in attribute.");

        const string optedIn = """
            using Snap.Hutao.Remastered.Core.Annotation;
            using WinRT;

            [BindableCustomPropertyProvider, GeneratedWinRTExposedType]
            internal partial class Model { public int Value { get; set; } }
            """;
        var (explicitExposure, _) = CreateCompilation(optedIn);
        Verify(Diagnose(explicitExposure, optIn: true).All(d => d.Id != "SHSGAOT003"), "Already registered WinRT type reported again.");
    }

    private static void InvalidDependencyPropertyOwnerIsDiagnosed()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            [DependencyProperty<int>("Count")]
            internal partial class Model { }
            """;

        var (compilation, _) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(compilation), "SHSGAOT004");
    }

    private static void AttachedDependencyPropertyCompiles()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            [DependencyProperty<string>("Tag", IsAttached = true)]
            internal partial class AttachedProperties { }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        Verify(Diagnose(compilation).All(d => d.Id != "SHSGAOT004"), "Attached DP owner incorrectly required to inherit DependencyObject.");
        var (generated, output) = Generate(compilation, parseOptions, new DependencyPropertyGenerator());
        Verify(generated.Contains("RegisterAttached", StringComparison.Ordinal), "Attached DP registration not emitted.");
        AssertNoErrors(output);
    }

    private static void DerivedDependencyPropertyCompiles()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;
            using Microsoft.UI.Xaml;

            [DependencyProperty<int>("Count")]
            internal sealed partial class CounterControl : DependencyObject { }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        Verify(Diagnose(compilation).All(d => d.Id != "SHSGAOT004"), "Derived DependencyObject owner rejected.");
        var (generated, output) = Generate(compilation, parseOptions, new DependencyPropertyGenerator());
        Verify(generated.Contains("Register", StringComparison.Ordinal), "Dependency property registration absent.");
        AssertNoErrors(output);
    }

    private static void DuplicateDependencyPropertiesAreDiagnosedAndSkipped()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;
            using Microsoft.UI.Xaml;

            [DependencyProperty<int>("Count")]
            [DependencyProperty<string>("Count")]
            internal partial class CounterControl : DependencyObject { }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(compilation), "SHSGAOT007");
        var (generated, _) = Generate(compilation, parseOptions, new DependencyPropertyGenerator());
        Verify(generated.Length == 0, "Duplicate DP generated overlapping property members.");
    }

    private static void UnsupportedIndexersAreDiagnosedAndSkipped()
    {
        const string source = """
            using Snap.Hutao.Remastered.Core.Annotation;

            [BindableCustomPropertyProvider]
            internal partial class Model
            {
                public int this[int first, int second] { get => first + second; set { } }
            }
            """;

        var (compilation, parseOptions) = CreateCompilation(source);
        AssertDiagnostic(Diagnose(compilation), "SHSGAOT005");
        var (_, output) = Generate(compilation, parseOptions, new BindableCustomPropertyGenerator());
        AssertNoErrors(output);
    }

    private sealed class TestConfigProvider(bool optIn) : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions global = new TestConfig(optIn);

        public override AnalyzerConfigOptions GlobalOptions => global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => global;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => global;
    }

    private sealed class TestConfig(bool optIn) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (optIn && key == "build_property.CsWinRTAotOptimizerEnabled")
            {
                value = "OptIn";
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
