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
        internal const string LoadFunctionName = "__LoadFunction";

        public static string Process(string source)
        {
            var nodes = Parse(source);
            // Step 1 - add necessary usings
            nodes = nodes.AddUsings(SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName("NativeLibraryLoader")));

            // Step 2 - get all extern methods and replace them
            var externMethods = GetExternMethods(nodes.DescendantNodes());
            var classNames = new HashSet<string>();
            foreach (var externMethod in externMethods)
            {
                var parent = externMethod.Parent as ClassDeclarationSyntax;
                classNames.Add(parent.Identifier.Text);
                nodes = nodes.RemoveNode(parent, SyntaxRemoveOptions.KeepNoTrivia);
                parent = parent.RemoveNode(externMethod, SyntaxRemoveOptions.KeepNoTrivia);
                foreach (var member in CreateMethodPointer(externMethod))
                    parent = parent.AddMembers(member);
                nodes = nodes.AddMembers(parent);
            }

            // Step 3 - generate loader function stub in each parent class
            var parentClasses = nodes.DescendantNodes()
                .Where(node => node is ClassDeclarationSyntax)
                .Select(node => (ClassDeclarationSyntax)node)
                .Where(c => classNames.Contains(c.Identifier.Text));
            foreach (var parent in parentClasses)
            {
                nodes = nodes.RemoveNode(parent, SyntaxRemoveOptions.KeepNoTrivia);
                var newParent = parent.AddMembers(LoaderFunctionStub());
                nodes = nodes.AddMembers(newParent);
            }

            // Step 4 - get the modified source
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
        internal static IEnumerable<MemberDeclarationSyntax> CreateMethodPointer(MethodDeclarationSyntax source)
        {
            var methodName = source.Identifier.Text; // TODO Read from DllImport if specified
            var delegateTypeName = source.Identifier.Text + "_t";
            var fieldName = "s_" + delegateTypeName;

            // private delegate T MethodName_t(...);
            var @delegate = SyntaxFactory.DelegateDeclaration(
                Empty<AttributeListSyntax>(),
                new SyntaxTokenList(Token(SyntaxKind.PrivateKeyword)),
                source.ReturnType,
                Identifier(delegateTypeName),
                source.TypeParameterList,
                source.ParameterList,
                source.ConstraintClauses);

            // private static MethodName_t s_MethodName_t = LoadFunction<T>("MethodName");
            var fieldVariableInitializer = SyntaxFactory.EqualsValueClause(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.GenericName(
                        Identifier(LoadFunctionName),
                        TypeArgument(delegateTypeName)),
                    Argument(methodName)));
            var fieldVariableDeclarator = SyntaxFactory.VariableDeclarator(
                Identifier(fieldName),
                null,
                fieldVariableInitializer
                );
            var fieldVariable = SyntaxFactory.VariableDeclaration(
                OfType(delegateTypeName),
                Separated(fieldVariableDeclarator)
            );
            var @field = SyntaxFactory.FieldDeclaration(
                Empty<AttributeListSyntax>(),
                new SyntaxTokenList(
                    Token(SyntaxKind.PrivateKeyword),
                    Token(SyntaxKind.StaticKeyword)
                    ),
                fieldVariable
            );

            // public static T MethodName(...) => s_MethodName_t(...);
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
                MethodPointer(fieldName, @delegate.ParameterList)
            );
            method = method.WithSemicolonToken(Token(SyntaxKind.SemicolonToken));
            return new List<MemberDeclarationSyntax>
            {
                @delegate, field, method
            };
        }

        internal static MethodDeclarationSyntax LoaderFunctionStub() =>
            SyntaxFactory.MethodDeclaration(
                Empty<AttributeListSyntax>(),
                new SyntaxTokenList(
                    Token(SyntaxKind.PrivateKeyword),
                    Token(SyntaxKind.StaticKeyword)
                ),
                OfType("T"),
                null,
                Identifier(LoadFunctionName),
                TypeParam("T"),
                Parameter("string", "name"),
                Empty<TypeParameterConstraintClauseSyntax>(),
                null,
                SyntaxFactory.ArrowExpressionClause(
                    SyntaxFactory.ParseExpression("throw new NotImplementedException();")));

        internal static SyntaxToken Token(SyntaxKind kind) =>
            SyntaxFactory.Token(kind);

        internal static SyntaxList<T> Empty<T>() where T : SyntaxNode =>
            new SyntaxList<T>();

        internal static SeparatedSyntaxList<T> Separated<T>(T node) where T : SyntaxNode =>
            SyntaxFactory.SeparatedList<T>(new T[] { node });

        internal static SyntaxToken Identifier(string name) =>
            SyntaxFactory.Identifier(name);

        internal static TypeSyntax OfType(string typeName) =>
            SyntaxFactory.IdentifierName(Identifier(typeName));

        internal static TypeArgumentListSyntax TypeArgument(string type) =>
            SyntaxFactory.TypeArgumentList(
                Token(SyntaxKind.LessThanToken),
                Separated<TypeSyntax>(OfType(type)),
                Token(SyntaxKind.GreaterThanToken));

        internal static TypeParameterListSyntax TypeParam(string type) =>
            SyntaxFactory.TypeParameterList(
                Token(SyntaxKind.LessThanToken),
                Separated<TypeParameterSyntax>(
                    SyntaxFactory.TypeParameter(type)
                    ),
                Token(SyntaxKind.GreaterThanToken));

        internal static ArgumentListSyntax Argument(string value) =>
            SyntaxFactory.ArgumentList(
                Token(SyntaxKind.OpenParenToken),
                Separated<ArgumentSyntax>(
                    SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(
                            SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(value)))),
                Token(SyntaxKind.CloseParenToken));

        internal static ParameterListSyntax Parameter(string type, string name) =>
            SyntaxFactory.ParameterList(
                Token(SyntaxKind.OpenParenToken),
                Separated<ParameterSyntax>(
                    SyntaxFactory.Parameter(
                        Empty<AttributeListSyntax>(),
                        new SyntaxTokenList(),
                        OfType(type),
                        Identifier(name),
                        null)),
                Token(SyntaxKind.CloseParenToken));

        internal static ArgumentListSyntax FromParams(ParameterListSyntax parameters)
        {
            var nodes = parameters.ChildNodes();
            var names = parameters.ChildNodes()
                .Select(n => n as ParameterSyntax)
                .Select(p => p.Identifier.Text);
            var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    names.Select(name => SyntaxFactory.Argument(
                        SyntaxFactory.IdentifierName(name))));
            return SyntaxFactory.ArgumentList(
                Token(SyntaxKind.OpenParenToken),
                arguments,
                Token(SyntaxKind.CloseParenToken)
                );
        }

        internal static ArrowExpressionClauseSyntax MethodPointer(
            string identifier,
            ParameterListSyntax parameters
            ) =>
            SyntaxFactory.ArrowExpressionClause(
                SyntaxFactory.InvocationExpression(
                    SyntaxFactory.IdentifierName(identifier),
                    FromParams(parameters)));
    }
}
