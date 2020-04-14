// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Operations;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.Editing;

namespace Microsoft.NetCore.Analyzers.Runtime
{
    [ExportCodeFixProvider(LanguageNames.CSharp, LanguageNames.VisualBasic), Shared]
    public class PreferStringContainsOverIndexOfFixer : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(PreferStringContainsOverIndexOfAnalyzer.RuleId);

        public sealed override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Document doc = context.Document;
            CancellationToken cancellationToken = context.CancellationToken;
            SyntaxNode root = await doc.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root.FindNode(context.Span) is SyntaxNode expression)
            {
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: MicrosoftNetCoreAnalyzersResources.PreferStringContainsOverIndexOfTitle,
                        createChangedDocument: async c =>
                        {
                            SemanticModel semanticModel = await doc.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                            var operation = semanticModel.GetOperationWalkingUpParentChain(expression, cancellationToken);
                            if (operation is IVariableDeclaratorOperation variableDeclaratorOperation)
                            {
                                return await HandleVariableDeclarator(variableDeclaratorOperation, doc, root, cancellationToken).ConfigureAwait(false);
                            }
                            else if (operation is IArgumentOperation argumentOperation)
                            {
                                if (argumentOperation.Value is ILiteralOperation argumentLiteral)
                                {
                                    return await HandleStringLiteral(argumentLiteral, doc, root, cancellationToken).ConfigureAwait(false);
                                }
                            }
                            return doc;
                        },
                        equivalenceKey: "PreferConstCharOverConstUnitStringInStringBuilderAppend"),
                    context.Diagnostics);
            }
        }

        private static async Task<Document?> HandleStringLiteral(ILiteralOperation argumentLiteral, Document doc, SyntaxNode root, CancellationToken cancellationToken)
        {
            if (argumentLiteral.ConstantValue.HasValue && argumentLiteral.ConstantValue.Value is string unitString && unitString.Length == 1)
            {
                DocumentEditor editor = await DocumentEditor.CreateAsync(doc, cancellationToken).ConfigureAwait(false);
                SyntaxGenerator generator = editor.Generator;
                char charValue = unitString[0];
                SyntaxNode charLiteralExpressionNode = generator.LiteralExpression(charValue);
                var newRoot = generator.ReplaceNode(root, argumentLiteral.Syntax, charLiteralExpressionNode);
                return doc.WithSyntaxRoot(newRoot);
            }
            return doc;
        }

        private static async Task<Document?> HandleVariableDeclarator(IVariableDeclaratorOperation variableDeclaratorOperation, Document doc, SyntaxNode root, CancellationToken cancellationToken)
        {
            IVariableDeclarationOperation variableDeclarationOperation = (IVariableDeclarationOperation)variableDeclaratorOperation.Parent;
            if (variableDeclarationOperation == null)
            {
                return null;
            }

            IVariableDeclarationGroupOperation variableGroupDeclarationOperation = (IVariableDeclarationGroupOperation)variableDeclarationOperation.Parent;
            if (variableGroupDeclarationOperation.Declarations.Length != 1)
            {
                return null;
            }

            if (variableDeclarationOperation.Declarators.Length != 1)
            {
                return null;
            }

            DocumentEditor editor = await DocumentEditor.CreateAsync(doc, cancellationToken).ConfigureAwait(false);
            SyntaxGenerator generator = editor.Generator;
            ILocalSymbol currentSymbol = variableDeclaratorOperation.Symbol;
            IVariableInitializerOperation variableInitializerOperation = OperationExtensions.GetVariableInitializer(variableDeclaratorOperation);
            if (variableInitializerOperation == null)
            {
                return null;
            }

            if (variableInitializerOperation.Value.ConstantValue.HasValue && variableInitializerOperation.Value.ConstantValue.Value is string unitString && unitString.Length == 1)
            {
                char charValue = unitString[0];
                SyntaxNode charLiteralExpressionNode = generator.LiteralExpression(charValue);
                var charTypeNode = generator.TypeExpression(SpecialType.System_Char);
                var charSyntaxNode = generator.LocalDeclarationStatement(charTypeNode, currentSymbol.Name, charLiteralExpressionNode, isConst: true);
                charSyntaxNode = charSyntaxNode.WithTriviaFrom(variableGroupDeclarationOperation.Syntax);
                var newRoot = generator.ReplaceNode(root, variableGroupDeclarationOperation.Syntax, charSyntaxNode);
                return doc.WithSyntaxRoot(newRoot);
            }

            return null;
        }
    }
}
