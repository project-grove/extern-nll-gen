using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace extern_nll_gen
{
    public static class Processor
    {
        internal const string LoadFunctionName = "__LoadFunction";
        private static Regex EntryPointRegex = new Regex(@"EntryPoint\s*=\s*[\\""]+([$a-zA-Z0-9_]*)[\\""]");

        public static string Process(string source, bool mangle = true)
        {
            var nodes = Parse(source);
            // Step 1 - add necessary usings
            nodes = nodes.AddUsings(SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName("NativeLibraryLoader")));

            // Step 2 - get all extern methods and replace them
            var classNames = new HashSet<string>();
            var externMethod = NextExternMethod(nodes);
            while (externMethod != null)
            {
                var parent = externMethod.Parent as ClassDeclarationSyntax;
                classNames.Add(parent.Identifier.Text);
                var newParent = parent.RemoveNode(externMethod, SyntaxRemoveOptions.KeepNoTrivia);
                foreach (var member in CreateMethodPointer(externMethod, mangle))
                    newParent = newParent.AddMembers(member);
                nodes = nodes.ReplaceNode(parent, newParent);
                externMethod = NextExternMethod(nodes);
            }

            // Step 3 - generate loader function stub in each parent class
            var parentClasses = nodes.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Where(c => classNames.Contains(c.Identifier.Text));

            foreach (var parent in parentClasses)
            {
                var newParent = parent.AddMembers(LoaderFunctionStub());
                nodes = nodes.ReplaceNode(parent, newParent);
            }

            // Step 4 - get the modified source
            return Formatter.Format(nodes, new AdhocWorkspace()).ToFullString();
        }

        internal static MethodDeclarationSyntax NextExternMethod(CompilationUnitSyntax nodes) =>
            GetExternMethods(nodes.DescendantNodes())
                .FirstOrDefault();

        internal static CompilationUnitSyntax Parse(string source) =>
            (CSharpSyntaxTree
                .ParseText(source)
                .GetRoot() as CompilationUnitSyntax);

        internal static IEnumerable<MethodDeclarationSyntax> GetExternMethods(
            IEnumerable<SyntaxNode> nodes
            ) =>
            nodes.OfType<MethodDeclarationSyntax>()
                .Where(method => method.Modifiers
                    .Select(modifier => modifier.Text)
                    .Contains("extern"));

        internal static IEnumerable<MemberDeclarationSyntax> CreateMethodPointer(
            MethodDeclarationSyntax source,
            bool mangle)
        {
            var methodName = source.Identifier.Text;
            var nativeMethodName = TryGetNativeName(source) ?? methodName;
            var delegateTypeName = mangle ? Mangle(source) : source.Identifier.Text + "_t";
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
                    Argument(nativeMethodName)));
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
            var modifiers = new SyntaxTokenList(source.Modifiers
                .Where(token => token.Kind() != SyntaxKind.ExternKeyword));
            var @method = SyntaxFactory.MethodDeclaration(
                Empty<AttributeListSyntax>(),
                modifiers,
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

        internal static ArgumentListSyntax FromParams(ParameterListSyntax paramList)
        {
            var parameters = paramList.ChildNodes()
                .OfType<ParameterSyntax>();
            var arguments = SyntaxFactory.SeparatedList<ArgumentSyntax>(
                    parameters.Select(param =>
                    {
                        var name = param.Identifier.Text;
                        var arg = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name));
                        var modifier = param.Modifiers.FirstOrDefault();
                        if (modifier != null)
                        {
                            switch (modifier.Text)
                            {
                                case "out":
                                    arg = arg.WithRefKindKeyword(Token(SyntaxKind.OutKeyword));
                                    break;
                                case "ref":
                                    arg = arg.WithRefKindKeyword(Token(SyntaxKind.RefKeyword));
                                    break;
                            }
                        }
                        return arg;
                    }));
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

        internal static string Mangle(MethodDeclarationSyntax method)
        {
            var methodName = method.Identifier.Text;
            var paramTypes = method.ParameterList.ChildNodes()
                .OfType<ParameterSyntax>()
                .Select(p => Slugify(p.Type.ToString().Replace('*', 'P')));
            return $"{methodName}_{string.Join('_', paramTypes)}_t";
        }

        internal static string TryGetNativeName(MethodDeclarationSyntax method)
        {
            var dllImportAttribute = method.AttributeLists
                .SelectMany(list => list.Attributes)
                .Where(attr => attr.Name.ToString() == "DllImport")
                .FirstOrDefault();
            if (dllImportAttribute == null) return null;
            if (dllImportAttribute.ArgumentList == null) return null;
            var attrs = dllImportAttribute.ArgumentList
                .ToString();
            var matches = EntryPointRegex.Match(attrs);
            if (!matches.Success) return null;
            var firstGroup = matches.Groups
                .Skip(1)
                .FirstOrDefault();
            if (firstGroup == null) return null;
            return firstGroup.Value;
        }

        internal static string Slugify(string str) =>
            new string(str.ToCharArray()
                .Select(c => (char.IsLetterOrDigit(c) || c == '_') ? c : '_')
                .ToArray());
    }
}
