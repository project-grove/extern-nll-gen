using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace extern_nll_gen
{
    public static class Processor
    {
        public static string Process(string source)
        {
            var nodes = Parse(source);
            // Step 1 - add necessary usings
            nodes = nodes.AddUsings(SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName("NativeLibraryLoader")));

            // Step 2 - get all extern methods and remove them from source
            var externMethods = GetExternMethods(nodes.DescendantNodes());
            foreach (var externMethod in externMethods)
                nodes = nodes.RemoveNode(externMethod, SyntaxRemoveOptions.KeepNoTrivia);

            // TODO

            return Formatter.Format(nodes, new AdhocWorkspace()).ToFullString();
        }

        internal static CompilationUnitSyntax Parse(string source) =>
            (CSharpSyntaxTree
                .ParseText(source)
                .GetRoot() as CompilationUnitSyntax);

        internal static IEnumerable<MethodDeclarationSyntax> GetExternMethods(
            IEnumerable<SyntaxNode> nodes
            ) =>
            nodes.Where(node => node is MethodDeclarationSyntax)
                .Select(node => (MethodDeclarationSyntax)node)
                .Where(method => method.Modifiers
                    .Select(modifier => modifier.Text)
                    .Contains("extern"));

        // TODO Check
        internal static IEnumerable<SyntaxNode> CreateMethodPointer(MethodDeclarationSyntax source)
        {
            var methodName = source.Identifier.Text; // TODO Read from DllImport if specified
            var delegateTypeName = source.Identifier.Text + "_t";
            var fieldName = "s_" + delegateTypeName;

            var @delegate = SyntaxFactory.DelegateDeclaration(
                Empty<AttributeListSyntax>(),
                new SyntaxTokenList(Token(SyntaxKind.PrivateKeyword)),
                source.ReturnType,
                Identifier(delegateTypeName),
                source.TypeParameterList,
                source.ParameterList,
                source.ConstraintClauses);

            var @field = SyntaxFactory.ParseStatement(
                $"private static {delegateTypeName} {fieldName} = " +
                $"LoadFunction<{delegateTypeName}>(\"{methodName}\");"
                );

            var @method = SyntaxFactory.MethodDeclaration(
                Empty<AttributeListSyntax>(),
                new SyntaxTokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword)
                ),
                @delegate.ReturnType,
                null,
                Identifier(methodName),
                @delegate.TypeParameterList,
                @delegate.ParameterList,
                @delegate.ConstraintClauses,
                null,
                MethodPointer(Identifier(fieldName), @delegate.ParameterList)
            );
            return new List<SyntaxNode>
            {
                @delegate, field, method
            };
        }

        internal static SyntaxToken Token(SyntaxKind kind) =>
            SyntaxFactory.Token(kind);

        internal static SyntaxList<T> Empty<T>() where T : SyntaxNode =>
            new SyntaxList<T>();

        internal static SyntaxToken Identifier(string name) =>
            SyntaxFactory.Identifier(name);

        internal static TypeSyntax OfType(string typeName) =>
            SyntaxFactory.IdentifierName(Identifier(typeName));

        internal static ArrowExpressionClauseSyntax MethodPointer(
            SyntaxToken identifier,
            ParameterListSyntax parameters
            )
        {
            // TODO
            throw new NotImplementedException();
        }
    }
}
