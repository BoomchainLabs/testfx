﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Composition;

using Analyzer.Utilities;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

using MSTest.Analyzers.Helpers;

namespace MSTest.Analyzers;

/// <summary>
/// Code fixer for <see cref="UseProperAssertMethodsAnalyzer"/>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseProperAssertMethodsFixer))]
[Shared]
public sealed class UseProperAssertMethodsFixer : CodeFixProvider
{
    /// <inheritdoc />
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; }
        = ImmutableArray.Create(DiagnosticIds.UseProperAssertMethodsRuleId);

    /// <inheritdoc />
    public override FixAllProvider GetFixAllProvider()
        // See https://github.com/dotnet/roslyn/blob/main/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
        => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc />
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        SyntaxNode root = await context.Document.GetRequiredSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        Diagnostic diagnostic = context.Diagnostics[0];
        string? mode = diagnostic.Properties[UseProperAssertMethodsAnalyzer.CodeFixModeKey];
        string? properAssertMethodName = diagnostic.Properties[UseProperAssertMethodsAnalyzer.ProperAssertMethodNameKey];
        if (mode is null || properAssertMethodName is null)
        {
            Debug.Fail($"Both '{nameof(mode)}' and '{properAssertMethodName}' are expected to be non-null.");
            return;
        }

        SyntaxNode diagnosticNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        if (diagnosticNode is not InvocationExpressionSyntax invocation)
        {
            Debug.Fail($"Is this an interesting scenario where IInvocationOperation for Assert call isn't associated with InvocationExpressionSyntax? SyntaxNode type: '{diagnosticNode.GetType()}', Text: '{diagnosticNode.GetText()}'");
            return;
        }

        SyntaxNode methodNameIdentifier = invocation.Expression;
        if (methodNameIdentifier is MemberAccessExpressionSyntax memberAccess)
        {
            methodNameIdentifier = memberAccess.Name;
        }

        if (methodNameIdentifier is not SimpleNameSyntax simpleNameSyntax)
        {
            Debug.Fail($"Is this an interesting scenario where we are unable to retrieve SimpleNameSyntax corresponding to the assert method? SyntaxNode type: '{methodNameIdentifier}', Text: '{methodNameIdentifier.GetText()}'.");
            return;
        }

        Func<CancellationToken, Task<Document>>? createChangedDocument = null;
        switch (mode)
        {
            case UseProperAssertMethodsAnalyzer.CodeFixModeSimple:
                createChangedDocument = ct => FixAssertMethodForSimpleModeAsync(context.Document, diagnostic.AdditionalLocations[0], diagnostic.AdditionalLocations[1], root, simpleNameSyntax, properAssertMethodName, ct);
                break;
            case UseProperAssertMethodsAnalyzer.CodeFixModeAddArgument:
                createChangedDocument = ct => FixAssertMethodForAddArgumentModeAsync(context.Document, diagnostic.AdditionalLocations[0], diagnostic.AdditionalLocations[1], diagnostic.AdditionalLocations[2], root, simpleNameSyntax, properAssertMethodName, ct);
                break;
            case UseProperAssertMethodsAnalyzer.CodeFixModeRemoveArgument:
                createChangedDocument = ct => FixAssertMethodForRemoveArgumentModeAsync(context.Document, diagnostic.AdditionalLocations, root, simpleNameSyntax, properAssertMethodName, diagnostic.Properties.ContainsKey(UseProperAssertMethodsAnalyzer.NeedsNullableBooleanCastKey), ct);
                break;
            case UseProperAssertMethodsAnalyzer.CodeFixModeCollectionCount:
                createChangedDocument = ct => FixAssertMethodForCollectionCountModeAsync(context.Document, diagnostic.AdditionalLocations, root, simpleNameSyntax, properAssertMethodName, ct);
                break;
            default:
                break;
        }

        if (createChangedDocument is not null)
        {
            context.RegisterCodeFix(
            CodeAction.Create(
                title: string.Format(CultureInfo.InvariantCulture, CodeFixResources.UseProperAssertMethodsFix, properAssertMethodName),
                createChangedDocument,
                equivalenceKey: nameof(UseProperAssertMethodsFixer)),
            diagnostic);
        }
    }

    private static async Task<Document> FixAssertMethodForSimpleModeAsync(Document document, Location conditionLocationToBeReplaced, Location replacementExpressionLocation, SyntaxNode root, SimpleNameSyntax simpleNameSyntax, string properAssertMethodName, CancellationToken cancellationToken)
    {
        // This doesn't properly handle cases like Assert.IsTrue(message: "My message", condition: x == null)
        // The proper handling of this may be Assert.IsNull(message: "My message", value: x)
        // Or: Assert.IsNull(x, "My message")
        // For now this is not handled.
        if (root.FindNode(conditionLocationToBeReplaced.SourceSpan) is not ArgumentSyntax conditionNodeToBeReplaced)
        {
            return document;
        }

        if (root.FindNode(replacementExpressionLocation.SourceSpan) is not ExpressionSyntax replacementExpressionNode)
        {
            return document;
        }

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        FixInvocationMethodName(editor, simpleNameSyntax, properAssertMethodName);
        editor.ReplaceNode(conditionNodeToBeReplaced, SyntaxFactory.Argument(replacementExpressionNode).WithAdditionalAnnotations(Formatter.Annotation));

        return editor.GetChangedDocument();
    }

    private static async Task<Document> FixAssertMethodForAddArgumentModeAsync(Document document, Location conditionLocation, Location expectedLocation, Location actualLocation, SyntaxNode root, SimpleNameSyntax simpleNameSyntax, string properAssertMethodName, CancellationToken cancellationToken)
    {
        // This doesn't properly handle cases like Assert.IsTrue(message: "My message", condition: x == y)
        // The proper handling of this may be Assert.AreEqual(message: "My message", expected: x, actual: y)
        // Or: Assert.AreEqual(x, y, "My message")
        // For now this is not handled.
        if (root.FindNode(conditionLocation.SourceSpan) is not ArgumentSyntax conditionNode)
        {
            return document;
        }

        if (conditionNode.Parent is not ArgumentListSyntax argumentList)
        {
            return document;
        }

        if (root.FindNode(expectedLocation.SourceSpan) is not { } expectedNode
            || expectedNode is not ArgumentSyntax and not ExpressionSyntax)
        {
            return document;
        }

        if (root.FindNode(actualLocation.SourceSpan) is not ExpressionSyntax actualNode)
        {
            return document;
        }

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        FixInvocationMethodName(editor, simpleNameSyntax, properAssertMethodName);

        ArgumentListSyntax newArgumentList = argumentList;
        ExpressionSyntax expectedExpression = expectedNode switch
        {
            ArgumentSyntax argument => argument.Expression,
            ExpressionSyntax expression => expression,
            _ => throw new InvalidOperationException($"Unexpected node type for expected argument: {expectedNode.GetType()}"),
        };
        newArgumentList = newArgumentList.ReplaceNode(conditionNode, SyntaxFactory.Argument(expectedExpression).WithAdditionalAnnotations(Formatter.Annotation));
        int insertionIndex = argumentList.Arguments.IndexOf(conditionNode) + 1;
        newArgumentList = newArgumentList.WithArguments(newArgumentList.Arguments.Insert(insertionIndex, SyntaxFactory.Argument(actualNode).WithAdditionalAnnotations(Formatter.Annotation)));

        editor.ReplaceNode(argumentList, newArgumentList);

        return editor.GetChangedDocument();
    }

    private static async Task<Document> FixAssertMethodForRemoveArgumentModeAsync(
        Document document,
        IReadOnlyList<Location> additionalLocations,
        SyntaxNode root,
        SimpleNameSyntax simpleNameSyntax,
        string properAssertMethodName,
        bool needsNullableBoolCast,
        CancellationToken cancellationToken)
    {
        // This doesn't properly handle cases like Assert.AreEqual(message: "My message", expected: true, actual: x)
        // The proper handling of this may be Assert.IsTrue(message: "My message", condition: x)
        // Or: Assert.IsTrue(x, "My message")
        // For now this is not handled.
        if (root.FindNode(additionalLocations[0].SourceSpan) is not ArgumentSyntax expectedArgumentToRemove)
        {
            return document;
        }

        if (expectedArgumentToRemove.Parent is not ArgumentListSyntax argumentList)
        {
            return document;
        }

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        FixInvocationMethodName(editor, simpleNameSyntax, properAssertMethodName);

        int argumentIndexToRemove = argumentList.Arguments.IndexOf(expectedArgumentToRemove);
        ArgumentListSyntax newArgumentList;
        if (additionalLocations.Count > 1 && needsNullableBoolCast &&
            root.FindNode(additionalLocations[1].SourceSpan) is ArgumentSyntax actualArgument)
        {
            Compilation compilation = editor.SemanticModel.Compilation;
            var castExpression = (CastExpressionSyntax)editor.Generator.CastExpression(
                compilation.GetSpecialType(SpecialType.System_Nullable_T).Construct(compilation.GetSpecialType(SpecialType.System_Boolean)),
                actualArgument.Expression);
            newArgumentList = argumentList.WithArguments(
                argumentList.Arguments.Replace(actualArgument, SyntaxFactory.Argument(castExpression)).RemoveAt(argumentIndexToRemove));
        }
        else
        {
            newArgumentList = argumentList.WithArguments(argumentList.Arguments.RemoveAt(argumentIndexToRemove));
        }

        editor.ReplaceNode(argumentList, newArgumentList);

        return editor.GetChangedDocument();
    }

    private static async Task<Document> FixAssertMethodForCollectionCountModeAsync(
        Document document,
        IReadOnlyList<Location> additionalLocations,
        SyntaxNode root,
        SimpleNameSyntax simpleNameSyntax,
        string properAssertMethodName,
        CancellationToken cancellationToken)
    {
        // Handle collection count transformations:
        // Assert.AreEqual(0, list.Count) -> Assert.IsEmpty(list)
        // Assert.AreEqual(3, list.Count) -> Assert.HasCount(3, list)
        // Assert.AreEqual(list.Count, 0) -> Assert.IsEmpty(list)
        // Assert.AreEqual(list.Count, 3) -> Assert.HasCount(3, list)
        if (root.FindNode(additionalLocations[0].SourceSpan) is not ArgumentSyntax firstArgument ||
            root.FindNode(additionalLocations[1].SourceSpan) is not ArgumentSyntax ||
            firstArgument.Parent is not ArgumentListSyntax argumentList)
        {
            return document;
        }

        if (root.FindNode(additionalLocations[2].SourceSpan) is not ExpressionSyntax collectionExpression)
        {
            return document;
        }

        DocumentEditor editor = await DocumentEditor.CreateAsync(document, cancellationToken).ConfigureAwait(false);
        FixInvocationMethodName(editor, simpleNameSyntax, properAssertMethodName);

        // Preserve any additional arguments beyond the first two (expected/actual)
        var additionalArguments = argumentList.Arguments.Skip(2).ToList();

        ArgumentListSyntax newArgumentList;

        if (properAssertMethodName == "IsEmpty")
        {
            // For IsEmpty, we just need the collection argument plus any additional arguments
            var newArguments = new List<ArgumentSyntax>
            {
                SyntaxFactory.Argument(collectionExpression).WithAdditionalAnnotations(Formatter.Annotation),
            };
            newArguments.AddRange(additionalArguments);
            newArgumentList = argumentList.WithArguments(SyntaxFactory.SeparatedList(newArguments));
        }
        else // HasCount
        {
            // For HasCount, we need count and collection arguments plus any additional arguments
            // additionalLocations[3] should contain the count expression
            if (additionalLocations.Count > 3 &&
                root.FindNode(additionalLocations[3].SourceSpan) is ArgumentSyntax countArgument)
            {
                var newArguments = new List<ArgumentSyntax>
                {
                    SyntaxFactory.Argument(countArgument.Expression).WithAdditionalAnnotations(Formatter.Annotation),
                    SyntaxFactory.Argument(collectionExpression).WithAdditionalAnnotations(Formatter.Annotation),
                };
                newArguments.AddRange(additionalArguments);
                newArgumentList = argumentList.WithArguments(SyntaxFactory.SeparatedList(newArguments));
            }
            else
            {
                // Fallback: something went wrong, don't apply the fix
                return document;
            }
        }

        editor.ReplaceNode(argumentList, newArgumentList);

        return editor.GetChangedDocument();
    }

    private static void FixInvocationMethodName(DocumentEditor editor, SimpleNameSyntax simpleNameSyntax, string properAssertMethodName)
        // NOTE: Switching Assert.IsTrue(x == y) to Assert.AreEqual(x, y) MAY produce an overload resolution error.
        // For example, Assert.AreEqual("string", true) will fail the inference for generic argument.
        // This is not very common and is currently not handled properly.
        // If needed, we can adjust the codefix to account for that case and
        // produce a GenericNameSyntax (e.g, AreEqual<object>) instead of IdentifierNameSyntax (e.g, AreEqual).
        => editor.ReplaceNode(simpleNameSyntax, SyntaxFactory.IdentifierName(properAssertMethodName));
}
