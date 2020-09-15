﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Completion.Providers;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Microsoft.CodeAnalysis.CSharp.Completion.Providers
{
    [ExportCompletionProvider(nameof(OperatorIndexerCompletionProvider), LanguageNames.CSharp)]
    [ExtensionOrder(After = nameof(SymbolCompletionProvider))]
    [Shared]
    internal class OperatorIndexerCompletionProvider : LSPCompletionProvider
    {
        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public OperatorIndexerCompletionProvider()
        {
        }

        private static readonly ImmutableHashSet<char> s_triggerCharactersHashSet = ImmutableHashSet.Create('.');

        internal override ImmutableHashSet<char> TriggerCharacters => s_triggerCharactersHashSet;

        internal override bool IsInsertionTrigger(SourceText text, int insertedCharacterPosition, OptionSet options)
        {
            var ch = text[insertedCharacterPosition];
            return ch == '.';
        }

        protected override Task<CompletionDescription> GetDescriptionWorkerAsync(Document document, CompletionItem item, CancellationToken cancellationToken)
            => SymbolCompletionItem.GetDescriptionAsync(item, document, cancellationToken);

        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            var cancellationToken = context.CancellationToken;
            var document = context.Document;
            var position = context.Position;
            var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);
            var token = syntaxTree.FindTokenOnLeftOfPosition(position, cancellationToken);
            if (token.IsKind(SyntaxKind.DotToken))
            {
                var parent = token.Parent;
                var expression = parent switch
                {
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
                    MemberBindingExpressionSyntax { Parent: ConditionalAccessExpressionSyntax conditionalAccess } _ => conditionalAccess.Expression,
                    _ => null,
                };
                if (expression != null)
                {
                    var semanticModel = await document.ReuseExistingSpeculativeModelAsync(position, cancellationToken).ConfigureAwait(false);
                    var container = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
                    var allMembers = container.GetMembers();
                    var allExplicitConversions = from m in allMembers.OfType<IMethodSymbol>()
                                                 where
                                                    m.IsConversion() && // MethodKind.Conversion
                                                    m.Name == WellKnownMemberNames.ExplicitConversionName && // op_Explicit
                                                    m.Parameters[0].Type == container // Convert from container type to other type
                                                 select SymbolCompletionItem.CreateWithSymbolId(
                                                     displayText: $"({m.ReturnType.ToNameDisplayString()})", // The type to convert to
                                                     symbols: ImmutableList.Create(m),
                                                     rules: CompletionItemRules.Default,
                                                     contextPosition: position,
                                                     properties: ImmutableDictionary<string, string>.Empty.Add("IsConversion", "true"));
                    context.AddItems(allExplicitConversions);
                }
            }
        }

        internal override async Task<CompletionChange> GetChangeAsync(Document document, CompletionItem item, TextSpan completionListSpan, char? commitKey, bool disallowAddingImports, CancellationToken cancellationToken)
        {
            if (item.Properties.TryGetValue("IsConversion", out var value) && value == "true")
            {
                var completionChange = await HandleConversionChangeAsync(document, item, completionListSpan, cancellationToken).ConfigureAwait(false);
                if (completionChange is { })
                {
                    return completionChange;
                }
            }

            return await base.GetChangeAsync(document, item, completionListSpan, commitKey, disallowAddingImports, cancellationToken).ConfigureAwait(false);
        }

        private async Task<CompletionChange> HandleConversionChangeAsync(Document document, CompletionItem item, TextSpan completionListSpan, CancellationToken cancellationToken)
        {
            var symbols = await SymbolCompletionItem.GetSymbolsAsync(item, document, cancellationToken).ConfigureAwait(false);
            var position = SymbolCompletionItem.GetContextPosition(item);
            var symbol = symbols.Single() as IMethodSymbol;
            var convertToType = symbol.ReturnType;
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var token = root.FindTokenOnLeftOfPosition(position);
            var syntax = token.Parent;
            var startPosition = completionListSpan.Start;
            var typeName = convertToType.ToMinimalDisplayString(await document.ReuseExistingSpeculativeModelAsync(startPosition, cancellationToken).ConfigureAwait(false), startPosition);
            if (syntax is MemberAccessExpressionSyntax memberAccess)
            {
                // expr. -> ((type)expr).
                var newNode =
                    ParenthesizedExpression(
                        CastExpression(IdentifierName(typeName), memberAccess.Expression.WithoutTrivia()));
                var newNodeText = newNode.ToFullString();
                var textChange = new TextChange(memberAccess.Expression.Span, newNodeText);
                return CompletionChange.Create(textChange);
            }

            return null;
        }
    }
}
