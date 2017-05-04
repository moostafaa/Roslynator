﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Roslynator.CSharp.Refactorings.AddExceptionToDocumentationComment
{
    public static class AddExceptionToDocumentationCommentRefactoring
    {
        public static AddExceptionToDocumentationCommentAnalysis Analyze(
            ThrowStatementSyntax throwStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            ExpressionSyntax expression = throwStatement.Expression;

            if (expression?.IsMissing == false)
            {
                return Analyze(throwStatement, expression, semanticModel, cancellationToken);
            }
            else
            {
                return AddExceptionToDocumentationCommentAnalysis.NoSuccess;
            }
        }

        private static AddExceptionToDocumentationCommentAnalysis Analyze(
            SyntaxNode node,
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            ITypeSymbol exceptionSymbol = semanticModel.GetTypeSymbol(expression, cancellationToken);

            if (exceptionSymbol.InheritsFromException(semanticModel))
            {
                ISymbol declarationSymbol = GetDeclarationSymbol(node.SpanStart, semanticModel, cancellationToken);

                if (declarationSymbol != null)
                {
                    var containingMember = declarationSymbol.GetSyntax(cancellationToken) as MemberDeclarationSyntax;

                    if (containingMember != null)
                    {
                        DocumentationCommentTriviaSyntax comment = containingMember.GetSingleLineDocumentationComment();

                        if (comment != null
                            && CanAddExceptionToComment(comment, exceptionSymbol, semanticModel, cancellationToken))
                        {
                            ThrowInfo throwInfo = ThrowInfo.Create(node, exceptionSymbol, declarationSymbol);

                            return new AddExceptionToDocumentationCommentAnalysis(throwInfo, comment.ParentTrivia);
                        }
                    }
                }
            }

            return AddExceptionToDocumentationCommentAnalysis.NoSuccess;
        }

        private static IEnumerable<ThrowInfo> GetOtherUndocumentedExceptions(
            MemberDeclarationSyntax declaration,
            ISymbol declarationSymbol,
            Func<SyntaxNode, bool> predicate,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (SyntaxNode node in declaration.DescendantNodes(f => !f.IsKind(
                SyntaxKind.AnonymousMethodExpression,
                SyntaxKind.SimpleLambdaExpression,
                SyntaxKind.ParenthesizedLambdaExpression)))
            {
                switch (node.Kind())
                {
                    case SyntaxKind.ThrowStatement:
                        {
                            if (predicate(node))
                            {
                                var throwStatement = (ThrowStatementSyntax)node;

                                ThrowInfo info = GetUndocumentedExceptionInfo(node, throwStatement.Expression, declaration, declarationSymbol, semanticModel, cancellationToken);

                                if (info != null)
                                    yield return info;
                            }

                            break;
                        }
                }
            }
        }

        private static ThrowInfo GetUndocumentedExceptionInfo(
        SyntaxNode node,
        ExpressionSyntax expression,
        MemberDeclarationSyntax declaration,
        ISymbol declarationSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        {
            if (expression != null)
            {
                ITypeSymbol typeSymbol = semanticModel.GetTypeSymbol(expression, cancellationToken);

                if (typeSymbol.InheritsFromException(semanticModel))
                {
                    DocumentationCommentTriviaSyntax comment = declaration.GetSingleLineDocumentationComment();

                    if (comment != null
                        && CanAddExceptionToComment(comment, typeSymbol, semanticModel, cancellationToken))
                    {
                        return ThrowInfo.Create(node, typeSymbol, declarationSymbol);
                    }
                }
            }

            return null;
        }

        private static bool CanAddExceptionToComment(
            DocumentationCommentTriviaSyntax comment,
            ITypeSymbol exceptionSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            bool containsException = false;
            bool containsIncludeOrExclude = false;
            bool isFirst = true;

            foreach (XmlNodeSyntax node in comment.Content)
            {
                XmlElementInfo info;
                if (XmlElementInfo.TryCreate(node, out info))
                {
                    switch (info.ElementKind)
                    {
                        case XmlElementKind.Include:
                        case XmlElementKind.Exclude:
                            {
                                if (isFirst)
                                    containsIncludeOrExclude = true;

                                break;
                            }
                        case XmlElementKind.InheritDoc:
                            {
                                return false;
                            }
                        case XmlElementKind.Exception:
                            {
                                if (!containsException)
                                {
                                    if (info.IsXmlElement)
                                    {
                                        containsException = ContainsException((XmlElementSyntax)info.Element, exceptionSymbol, semanticModel, cancellationToken);
                                    }
                                    else
                                    {
                                        containsException = ContainsException((XmlEmptyElementSyntax)info.Element, exceptionSymbol, semanticModel, cancellationToken);
                                    }
                                }

                                break;
                            }
                    }

                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        containsIncludeOrExclude = false;
                    }
                }
            }

            return !containsIncludeOrExclude
                && !containsException;
        }

        private static bool ContainsException(XmlElementSyntax xmlElement, ITypeSymbol exceptionSymbol, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            XmlElementStartTagSyntax startTag = xmlElement.StartTag;

            if (startTag != null)
            {
                return ContainsException(startTag.Attributes, exceptionSymbol, semanticModel, cancellationToken);
            }

            return false;
        }

        private static bool ContainsException(XmlEmptyElementSyntax xmlEmptyElement, ITypeSymbol exceptionSymbol, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            return ContainsException(xmlEmptyElement.Attributes, exceptionSymbol, semanticModel, cancellationToken);
        }

        private static bool ContainsException(SyntaxList<XmlAttributeSyntax> attributes, ITypeSymbol exceptionSymbol, SemanticModel semanticModel, CancellationToken cancellationToken)
        {
            foreach (XmlAttributeSyntax xmlAttribute in attributes)
            {
                if (xmlAttribute.IsKind(SyntaxKind.XmlCrefAttribute))
                {
                    var xmlCrefAttribute = (XmlCrefAttributeSyntax)xmlAttribute;

                    CrefSyntax cref = xmlCrefAttribute.Cref;

                    if (cref != null)
                    {
                        ISymbol symbol = semanticModel.GetSymbol(cref, cancellationToken);

                        if (exceptionSymbol.Equals(symbol))
                            return true;
                    }
                }
            }

            return false;
        }

        internal static ISymbol GetDeclarationSymbol(
            int position,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ISymbol symbol = semanticModel.GetEnclosingSymbol(position, cancellationToken);

            if (symbol?.IsMethod() == true)
            {
                var methodsymbol = (IMethodSymbol)symbol;

                if (methodsymbol.MethodKind == MethodKind.Ordinary)
                {
                    if (methodsymbol.PartialImplementationPart != null)
                        symbol = methodsymbol.PartialImplementationPart;
                }
                else if (methodsymbol.AssociatedSymbol != null)
                {
                    symbol = methodsymbol.AssociatedSymbol;
                }
            }

            return symbol;
        }

        public static Task<Document> RefactorAsync(
            Document document,
            ThrowStatementSyntax throwStatement,
            CancellationToken cancellationToken)
        {
            return RefactorAsync(document, throwStatement, throwStatement.Expression, cancellationToken);
        }

        private static async Task<Document> RefactorAsync(
            Document document,
            SyntaxNode node,
            ExpressionSyntax expression,
            CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            ITypeSymbol exceptionSymbol = semanticModel.GetTypeSymbol(expression, cancellationToken);

            ISymbol declarationSymbol = GetDeclarationSymbol(node.SpanStart, semanticModel, cancellationToken);

            var memberDeclaration = await declarationSymbol
                .DeclaringSyntaxReferences[0]
                .GetSyntaxAsync(cancellationToken)
                .ConfigureAwait(false) as MemberDeclarationSyntax;

            SyntaxTrivia trivia = memberDeclaration.GetSingleLineDocumentationCommentTrivia();

            ThrowInfo throwInfo = ThrowInfo.Create(node, exceptionSymbol, declarationSymbol);

            return await RefactorAsync(
                document,
                trivia,
                throwInfo,
                memberDeclaration,
                declarationSymbol,
                semanticModel,
                cancellationToken).ConfigureAwait(false);
        }

        public static async Task<Document> RefactorAsync(
            Document document,
            AddExceptionToDocumentationCommentAnalysis analysis,
            CancellationToken cancellationToken)
        {
            SemanticModel semanticModel = await document.GetSemanticModelAsync().ConfigureAwait(false);

            var memberDeclaration = await analysis.DeclarationSymbol
                .DeclaringSyntaxReferences[0]
                .GetSyntaxAsync(cancellationToken)
                .ConfigureAwait(false) as MemberDeclarationSyntax;

            return await RefactorAsync(
                document,
                analysis.DocumentationComment,
                analysis.ThrowInfo,
                memberDeclaration,
                analysis.DeclarationSymbol,
                semanticModel,
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task<Document> RefactorAsync(
            Document document,
            SyntaxTrivia trivia,
            ThrowInfo throwInfo,
            MemberDeclarationSyntax memberDeclaration,
            ISymbol declarationSymbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            SourceText sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);

            var throwInfos = new List<ThrowInfo>() { throwInfo };

            foreach (ThrowInfo info in GetOtherUndocumentedExceptions(memberDeclaration, declarationSymbol, node => node != throwInfo.Node, semanticModel, cancellationToken))
            {
                if (!throwInfos.Any(f => f.ExceptionSymbol == info.ExceptionSymbol))
                    throwInfos.Add(info);
            }

            string indent = GetIndent(memberDeclaration.GetLeadingTrivia());

            var sb = new StringBuilder();

            foreach (ThrowInfo info in throwInfos)
            {
                sb.Append(indent);

                IParameterSymbol parameterSymbol = info.GetParameterSymbol(semanticModel, cancellationToken);

                AppendExceptionDocumentation(trivia, info.ExceptionSymbol, parameterSymbol, semanticModel, ref sb);
            }

            var textChange = new TextChange(new TextSpan(trivia.FullSpan.End, 0), sb.ToString());

            SourceText newSourceText = sourceText.WithChanges(textChange);

            return document.WithText(newSourceText);
        }

        private static string GetIndent(SyntaxTriviaList leadingTrivia)
        {
            if (leadingTrivia.Any())
            {
                int index = leadingTrivia.Count;

                while (index >= 1
                    && leadingTrivia[index - 1].IsWhitespaceTrivia())
                {
                    index--;
                }

                return string.Concat(leadingTrivia.Skip(index));
            }

            return "";
        }

        private static void AppendExceptionDocumentation(
            SyntaxTrivia trivia,
            ITypeSymbol exceptionSymbol,
            IParameterSymbol parameterSymbol,
            SemanticModel semanticModel,
            ref StringBuilder sb)
        {
            sb.Append("/// <exception cref=\"");

            foreach (char ch in exceptionSymbol.ToMinimalDisplayString(semanticModel, trivia.FullSpan.End))
            {
                if (ch == '<')
                {
                    sb.Append('{');
                }
                else if (ch == '>')
                {
                    sb.Append('}');
                }
                else
                {
                    sb.Append(ch);
                }
            }

            sb.Append("\">");

            if (parameterSymbol != null)
            {
                sb.Append("<paramref name=\"");
                sb.Append(parameterSymbol.Name);
                sb.Append("\"/>");

                if (exceptionSymbol.Equals(semanticModel.GetTypeByMetadataName(MetadataNames.System_ArgumentNullException)))
                    sb.Append(" is <c>null</c>.");
            }

            sb.Append("</exception>");
            sb.AppendLine();
        }
    }
}