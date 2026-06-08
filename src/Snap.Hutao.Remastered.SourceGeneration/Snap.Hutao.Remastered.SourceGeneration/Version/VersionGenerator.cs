// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Snap.Hutao.Remastered.SourceGeneration.Extension;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using static Snap.Hutao.SourceGeneration.Primitive.FastSyntaxFactory;
using static Snap.Hutao.Remastered.SourceGeneration.WellKnownSyntax;
using System.Collections.Immutable;

namespace Snap.Hutao.Remastered.SourceGeneration.Version;

[Generator(LanguageNames.CSharp)]
internal sealed class VersionGenerator : IIncrementalGenerator
{
    private const string AppxManifestSuffix = ".appxmanifest";

    private static readonly Regex VersionRegex = new(
        @"Version=""(\d+)\.(\d+)\.(\d+)\.(\d+)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<VersionInfo> provider = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(AppxManifestSuffix, StringComparison.OrdinalIgnoreCase))
            .Collect()
            .SelectMany(ToFirstVersionInfo);

        context.RegisterImplementationSourceOutput(provider, GenerateVersionCodeWrapper);
    }

    private static IEnumerable<VersionInfo> ToFirstVersionInfo(ImmutableArray<AdditionalText> texts, CancellationToken token)
    {
        foreach (AdditionalText text in texts)
        {
            token.ThrowIfCancellationRequested();
            string content = text.GetText(token)!.ToString();
            Match match = VersionRegex.Match(content);
            if (match.Success)
            {
                yield return new VersionInfo
                {
                    Major = int.Parse(match.Groups[1].Value),
                    Minor = int.Parse(match.Groups[2].Value),
                    Build = int.Parse(match.Groups[3].Value),
                    Revision = int.Parse(match.Groups[4].Value),
                };
                yield break;
            }
        }
    }

    private static void GenerateVersionCodeWrapper(SourceProductionContext production, VersionInfo version)
    {
        try
        {
            GenerateVersionCode(production, version);
        }
        catch (Exception ex)
        {
            production.AddSource($"Error-{Guid.NewGuid()}.g.cs", ex.ToString());
        }
    }

    private static void GenerateVersionCode(SourceProductionContext production, VersionInfo version)
    {
        string versionString = $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

        CompilationUnitSyntax syntax = CompilationUnit()
            .WithMembers(SingletonList<MemberDeclarationSyntax>(FileScopedNamespaceDeclaration("Snap.Hutao.Remastered")
                .WithLeadingTrivia(NullableEnableTriviaList)
                .WithMembers(SingletonList<MemberDeclarationSyntax>(
                    ClassDeclaration("AppVersion")
                        .WithModifiers(PublicStaticTokenList)
                        .WithMembers(List<MemberDeclarationSyntax>(
                        [
                            FieldDeclaration(VariableDeclaration(StringType)
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("VersionString")
                                            .WithInitializer(EqualsValueClause(
                                                StringLiteralExpression(versionString))))))
                                .WithModifiers(PublicConstTokenList),
                            FieldDeclaration(VariableDeclaration(ParseTypeName("global::System.Version"))
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("CurrentVersion")
                                            .WithInitializer(EqualsValueClause(
                                                ObjectCreationExpression(ParseTypeName("global::System.Version"))
                                                    .WithArgumentList(ArgumentList(SeparatedList(
                                                    [
                                                        Argument(NumericLiteralExpression(version.Major)),
                                                        Argument(NumericLiteralExpression(version.Minor)),
                                                        Argument(NumericLiteralExpression(version.Build)),
                                                        Argument(NumericLiteralExpression(version.Revision))
                                                    ]))))))))
                                .WithModifiers(PublicStaticReadonlyTokenList),
                            FieldDeclaration(VariableDeclaration(IntType)
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("Major")
                                            .WithInitializer(EqualsValueClause(
                                                NumericLiteralExpression(version.Major))))))
                                .WithModifiers(PublicConstTokenList),
                            FieldDeclaration(VariableDeclaration(IntType)
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("Minor")
                                            .WithInitializer(EqualsValueClause(
                                                NumericLiteralExpression(version.Minor))))))
                                .WithModifiers(PublicConstTokenList),
                            FieldDeclaration(VariableDeclaration(IntType)
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("Build")
                                            .WithInitializer(EqualsValueClause(
                                                NumericLiteralExpression(version.Build))))))
                                .WithModifiers(PublicConstTokenList),
                            FieldDeclaration(VariableDeclaration(IntType)
                                    .WithVariables(SingletonSeparatedList(
                                        VariableDeclarator("Revision")
                                            .WithInitializer(EqualsValueClause(
                                                NumericLiteralExpression(version.Revision))))))
                                .WithModifiers(PublicConstTokenList),
                        ]))))))
            .NormalizeWhitespace();

        production.AddSource("AppVersion.g.cs", syntax.ToFullStringWithHeader());
    }

    private sealed record VersionInfo
    {
        public int Major { get; init; }
        public int Minor { get; init; }
        public int Build { get; init; }
        public int Revision { get; init; }
    }
}
