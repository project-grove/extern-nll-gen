using System;
using Xunit;
using static extern_nll_gen.Processor;

namespace test
{
    public class ProcessorTest
    {
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
            var methods = GetExternMethods(Parse(source));
            Assert.Single(methods);
        }
    }
}
