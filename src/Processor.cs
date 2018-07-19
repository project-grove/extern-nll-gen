using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace extern_nll_gen
{
    public static class Processor
    {
        public static string Process(string source)
        {
            var nodes = Parse(source);
            var externMethods = GetExternMethods(nodes);
            // TODO
            return "";
        }

        internal static IEnumerable<SyntaxNode> Parse(string source) =>
            (CSharpSyntaxTree
                .ParseText(source)
                .GetRoot() as CompilationUnitSyntax)
                .DescendantNodesAndSelf();

        internal static IEnumerable<MethodDeclarationSyntax> GetExternMethods(
            IEnumerable<SyntaxNode> nodes
            ) =>
            nodes.Where(node => node is MethodDeclarationSyntax)
                .Select(node => (MethodDeclarationSyntax)node)
                .Where(method => method.Modifiers
                    .Select(modifier => modifier.Text)
                    .Contains("extern"))
                .ToList();
    }
}
