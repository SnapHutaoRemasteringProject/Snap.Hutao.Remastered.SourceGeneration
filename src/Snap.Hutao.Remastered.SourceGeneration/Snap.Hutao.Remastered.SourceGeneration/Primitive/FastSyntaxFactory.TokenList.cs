// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static Snap.Hutao.Remastered.SourceGeneration.Primitive.SyntaxKeywords;

namespace Snap.Hutao.SourceGeneration.Primitive;

internal static partial class FastSyntaxFactory
{
    public static SyntaxTokenList PublicTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword);

    public static SyntaxTokenList PublicAbstractTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, AbstractKeyword);

    public static SyntaxTokenList PublicAbstractPartialTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, AbstractKeyword, PartialKeyword);

    public static SyntaxTokenList PublicReadOnlyPartialTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, ReadOnlyKeyword, PartialKeyword);

    public static SyntaxTokenList PublicSealedTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, SealedKeyword);

    public static SyntaxTokenList PublicStaticTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, StaticKeyword);

    public static SyntaxTokenList PublicStaticPartialTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, StaticKeyword, PartialKeyword);

    public static SyntaxTokenList PublicPartialTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, PartialKeyword);

    public static SyntaxTokenList ParamsTokenList { get; } = SyntaxFactory.TokenList(ParamsKeyword);

    public static SyntaxTokenList PartialTokenList { get; } = SyntaxFactory.TokenList(PartialKeyword);

    public static SyntaxTokenList PrivateTokenList { get; } = SyntaxFactory.TokenList(PrivateKeyword);

    public static SyntaxTokenList PrivatePartialTokenList { get; } = SyntaxFactory.TokenList(PrivateKeyword, PartialKeyword);

    public static SyntaxTokenList PrivateProtectedTokenList { get; } = SyntaxFactory.TokenList(PrivateKeyword, ProtectedKeyword);

    public static SyntaxTokenList PrivateStaticExternTokenList { get; } = SyntaxFactory.TokenList(PrivateKeyword, StaticKeyword, ExternKeyword);

    public static SyntaxTokenList PrivateStaticReadonlyTokenList { get; } = SyntaxFactory.TokenList(PrivateKeyword, StaticKeyword, ReadOnlyKeyword);

    public static SyntaxTokenList ProtectedTokenList { get; } = SyntaxFactory.TokenList(ProtectedKeyword);

    public static SyntaxTokenList ProtectedInternalTokenList { get; } = SyntaxFactory.TokenList(ProtectedKeyword, InternalKeyword);

    public static SyntaxTokenList PublicConstTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, ConstKeyword);

    public static SyntaxTokenList PublicOverrideTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, OverrideKeyword);

    public static SyntaxTokenList PublicReadOnlyTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, ReadOnlyKeyword);

    public static SyntaxTokenList PublicStaticReadonlyTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, StaticKeyword, ReadOnlyKeyword);

    public static SyntaxTokenList PublicStaticUnsafeTokenList { get; } = SyntaxFactory.TokenList(PublicKeyword, StaticKeyword, UnsafeKeyword);

    public static SyntaxTokenList StaticTokenList { get; } = SyntaxFactory.TokenList(StaticKeyword);

    public static SyntaxTokenList ThisTokenList { get; } = SyntaxFactory.TokenList(ThisKeyword);
}