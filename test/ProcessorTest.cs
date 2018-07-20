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

                    [DllImport]
                    public static extern void myNativeMethod2(int param, double[] values);
                    
                    public static void OtherMethod() {}
                }";
            var methods = GetExternMethods(Parse(source).DescendantNodes());
            Assert.Equal(2, methods.Count());
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
            var methodNames = new[] { "Method1", "Method2" };
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void Method1(int param, double[] values);
                    [DllImport]
                    public static extern void Method2(int param, double[] values);
                }";

            var processedSource = Process(source, mangle: false);

            foreach (var name in methodNames)
            {
                var @delegate = $"private delegate void {name}_t(int param, double[] values);";
                var field = $"private static {name}_t s_{name}_t = " +
                        $"{Processor.LoadFunctionName}<{name}_t>(\"{name}\");";
                var method = $"public static void {name}(int param, double[] values) => s_{name}_t(param, values);";
                
                Assert.Contains(@delegate, processedSource);
                Assert.Contains(field, processedSource);
                Assert.Contains(method, processedSource);
            }
        }

        [Fact]
        public void ShouldAddRefAndOutKeywordsIfNeeded()
        {
            var source = @"
                public static class NativeClass
                {
                    [DllImport]
                    public static extern void Method1(out int val);
                    [DllImport]
                    public static extern void Method2(int param1, ref int param2);
                }";
            var processedSource = Process(source, mangle: false);
            var method1 = "public static void Method1(out int val) => s_Method1_t(out val);";
            var method2 = "public static void Method2(int param1, ref int param2) => s_Method2_t(param1, ref param2);";

            Assert.Contains(method1, processedSource);
            Assert.Contains(method2, processedSource);
        }

        // TODO Test DllImport function names
    }
}
