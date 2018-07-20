using extern_nll_gen;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static extern_nll_gen.Processor;

namespace test
{
    public class ProcessorTest
    {
        [Fact]
        public void ShouldOutputCode()
        {
            var source = "public class MyClass { }";
            var result = Process(source);
            Assert.Contains(source, result);
        }

        [Fact]
        public void ShouldAddUsings()
        {
            var source = Process("");
            var expected = new List<string> { "NativeLibraryLoader" };
            var actual = Parse(source).DescendantNodes()
                .Where(node => node is UsingDirectiveSyntax)
                .Select(node => (UsingDirectiveSyntax)node)
                .Select(u => u.Name.ToString());

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void ShouldFindExternMethods()
        {
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void myNativeMethod(int param, double[] values);
                    
                    public static void OtherMethod() {}
                }";
            var methods = GetExternMethods(Parse(source).DescendantNodes());
            Assert.Single(methods);
        }

        [Fact]
        public void ShouldRemoveExternMethods()
        {
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void myNativeMethod(int param, double[] values);
                    
                    public static void OtherMethod() {}
                }";
            var processedSource = Process(source);
            var methodsBefore = GetExternMethods(Parse(source).DescendantNodes());
            var methodsAfter = GetExternMethods(Parse(processedSource).DescendantNodes());
            Assert.Single(methodsBefore);
            Assert.Empty(methodsAfter);
        }

        [Fact]
        public void ShouldAddLoadFunctionMethod()
        {
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void myNativeMethod(int param, double[] values);
                    
                    public static void OtherMethod() {}
                }";
            var processedSource = Process(source);
            var rootNode = Parse(processedSource);
            Assert.Contains(rootNode.DescendantNodes(),
                n =>
                {
                    var method = n as MethodDeclarationSyntax;
                    if (method == null) return false;
                    if (method.Identifier.Text == Processor.LoadFunctionName) return true;
                    return false;
                });
        }

        [Fact]
        public void ShouldTransformExternMethods()
        {
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void Method(int param, double[] values);
                }";
            var @delegate = "private delegate void Method_t(int param, double[] values);";
            var field = "private static Method_t s_Method_t = " +
                    $"{Processor.LoadFunctionName}<Method_t>(\"Method\");";
            var method = "public static void Method(int param, double[] values) => s_Method_t(param, values);";

            var processedSource = Process(source);

            Assert.Contains(@delegate, processedSource);
            Assert.Contains(field, processedSource);
            Assert.Contains(method, processedSource);
        }

        // TODO Test DllImport function names
    }
}
