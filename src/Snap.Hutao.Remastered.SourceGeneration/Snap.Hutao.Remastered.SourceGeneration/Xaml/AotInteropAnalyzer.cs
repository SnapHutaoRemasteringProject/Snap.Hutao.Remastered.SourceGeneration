// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Snap.Hutao.Remastered.SourceGeneration.Extension;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Snap.Hutao.Remastered.SourceGeneration.Xaml;

/// <summary>
/// Checks contracts that this generator cannot repair by emitting a second partial declaration.
/// CsWinRT 2.x also requires source-visible exposure declarations; CsWinRT 3.x uses a build task.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class AotInteropAnalyzer : DiagnosticAnalyzer
{
    internal static readonly DiagnosticDescriptor PartialRequired = new(
        "SHSGAOT001", "A partial declaration is required",
        "Type '{0}' must be declared partial because source-generated XAML members are added to it",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor ConflictingBindingGenerators = new(
        "SHSGAOT002", "Conflicting binding property generators",
        "Type '{0}' cannot use both BindableCustomPropertyProvider and WinRT.GeneratedBindableCustomProperty; choose one implementation",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor OptInExposureRequired = new(
        "SHSGAOT003", "CsWinRT 2.x OptIn requires an exposed type",
        "Type '{0}' implements a generated WinRT binding interface; add [WinRT.GeneratedWinRTExposedType] to its original partial declaration when using CsWinRT 2.x OptIn",
        "NativeAOT", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor InvalidDependencyPropertyOwner = new(
        "SHSGAOT004", "Dependency property owner must support DependencyObject",
        "Type '{0}' declares a non-attached dependency property but does not inherit Microsoft.UI.Xaml.DependencyObject",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor UnsupportedIndexer = new(
        "SHSGAOT005", "Binding indexer must have one parameter",
        "Indexer on '{0}' has {1} parameters; generated XAML bindings support only one index parameter",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor GenerationFailure = new(
        "SHSGAOT006", "XAML source generation failed",
        "XAML generator could not process '{0}': {1}",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor DuplicateDependencyProperty = new(
        "SHSGAOT007", "Duplicate dependency property name",
        "Type '{0}' declares dependency property '{1}' more than once",
        "NativeAOT", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        PartialRequired,
        ConflictingBindingGenerators,
        OptInExposureRequired,
        InvalidDependencyPropertyOwner,
        UnsupportedIndexer,
        GenerationFailure,
        DuplicateDependencyProperty,
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class } type)
        {
            return;
        }

        bool hasBindingProvider = type.HasAttributeWithFullyQualifiedMetadataName(WellKnownMetadataNames.BindableCustomPropertyProviderAttribute);
        ImmutableArray<AttributeData> dependencyProperties = type.GetAttributes()
            .Where(static attribute => attribute.AttributeClass?.HasFullyQualifiedMetadataName(WellKnownMetadataNames.DependencyPropertyAttributeT) is true)
            .ToImmutableArray();

        if (!hasBindingProvider && dependencyProperties.IsEmpty)
        {
            return;
        }

        bool hasCsWinRTBinding = type.HasAttributeWithFullyQualifiedMetadataName("WinRT.GeneratedBindableCustomPropertyAttribute");
        bool isCsWinRT3 = IsCsWinRT3(context.Compilation);

        if (hasBindingProvider && hasCsWinRTBinding)
        {
            context.ReportDiagnostic(Diagnostic.Create(ConflictingBindingGenerators, type.Locations.FirstOrDefault(), type.Name));
        }

        // Every containing type must also be partial: HierarchyInfo emits nested partial declarations.
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            foreach (SyntaxReference declaration in current.DeclaringSyntaxReferences)
            {
                if (declaration.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax syntax &&
                    !syntax.Modifiers.Any(static token => token.IsKind(SyntaxKind.PartialKeyword)))
                {
                    context.ReportDiagnostic(Diagnostic.Create(PartialRequired, syntax.Identifier.GetLocation(), current.Name));
                }
            }
        }

        if (hasBindingProvider)
        {
            // A generated companion attribute is not a substitute in CsWinRT 2.x: its
            // source generator may not observe output from another generator in the same pass.
            if (!isCsWinRT3 && IsCsWinRTOptIn(context.Options) &&
                context.Compilation.GetTypeByMetadataName("WinRT.GeneratedWinRTExposedTypeAttribute") is not null &&
                !type.HasAttributeWithFullyQualifiedMetadataName("WinRT.GeneratedWinRTExposedTypeAttribute"))
            {
                context.ReportDiagnostic(Diagnostic.Create(OptInExposureRequired, type.Locations.FirstOrDefault(), type.Name));
            }

            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsIndexer && property.DeclaredAccessibility == Accessibility.Public && property.Parameters.Length != 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(UnsupportedIndexer, property.Locations.FirstOrDefault(), type.Name, property.Parameters.Length));
                }
            }
        }

        if (!dependencyProperties.IsEmpty)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (AttributeData attribute in dependencyProperties)
            {
                string? name = attribute.ConstructorArguments.Length > 0
                    ? attribute.ConstructorArguments[0].Value as string
                    : null;

                if (name is not null && !names.Add(name))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateDependencyProperty,
                        attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation() ?? type.Locations.FirstOrDefault(),
                        type.Name, name));
                }
            }
        }

        if (!dependencyProperties.IsEmpty &&
            context.Compilation.GetTypeByMetadataName("Microsoft.UI.Xaml.DependencyObject") is INamedTypeSymbol dependencyObject &&
            !InheritsFrom(type, dependencyObject) &&
            dependencyProperties.Any(static attribute => !IsAttached(attribute)))
        {
            context.ReportDiagnostic(Diagnostic.Create(InvalidDependencyPropertyOwner, type.Locations.FirstOrDefault(), type.Name));
        }
    }

    private static bool IsCsWinRT3(Compilation compilation)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (tree.Options is CSharpParseOptions options && options.PreprocessorSymbolNames.Contains("CSWINRT3_0"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCsWinRTOptIn(AnalyzerOptions options)
    {
        return options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.CsWinRTAotOptimizerEnabled", out string? value) &&
            string.Equals(value, "OptIn", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAttached(AttributeData attribute)
    {
        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
        {
            if (argument.Key == "IsAttached" && argument.Value.Value is true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }
}
