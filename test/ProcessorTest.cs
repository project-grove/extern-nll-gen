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
    }
}
