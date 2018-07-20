using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp;

[assembly:InternalsVisibleTo("test")]

namespace extern_nll_gen
{
    class Program
    {
        public static void Main(string[] args)
        {
            var path = string.Join(' ', args);
            var source = File.ReadAllText(path);
            var output = Processor.Process(source);
            Console.WriteLine(output);
        }
    }
}
