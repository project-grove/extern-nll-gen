# What is that?
It's a .NET CLI tool to transform this:
```csharp
[DllImport("myPreciousLib.dll")]
public static extern void myNativeMethod(int param, double[] values);
```
Into this:
```csharp
// delegate type
private delegate void myNativeMethod_t(int param, double[] values);

// static field where the native function pointer is loaded
private static myNativeMethod_t s_myNativeMethod_t = __LoadFunction<myNativeMethod_t>("myNativeMethod");

// public method for use within your code
public static void myNativeMethod(int param, double[] values) => s_myNativeMethod_t(param, values);


// here you should implement function loading (see below)
private static T __LoadFunction<T>(string name)
{
    throw new NotImplementedException();
}
```

# Well... I don't understand
The problem with the current .NET Standard implementation that the **DllImport** attribute doesn't support multiple DLL names and doesn't provide a mechanism like [Mono's DllMap](https://www.mono-project.com/docs/advanced/pinvoke/dllmap/) either.

So, for example, if you want to create a cross-platform OpenGL wrapper, you got a problem, because on Windows the DLL can be named **OpenGL32.dll**, on Linux it can be **libGL.so** or **libGL.so.1**, and on Mac it can be **libGL.dylib**!

# Okay, how can we solve this?
There are several ways to overcome this problem.

First way is to use ``#ifdef`` and do separate builds for different platforms. It's not pretty and makes your binary non-portable.

Second way is to generate stub libraries that just re-export the symbols from the needed ones. For example, generate a "opengl.dll", "opengl.so" and "opengl.dylib", and pass "opengl" as the DLL name for DllImport.
Which basically means that you should bundle these stubs with your app, and have a separate build step to generate them, which isn't pretty too.

The third ways is to convert raw function pointers (loaded by the platform's DLL loader) into delegates.

# Transform whaaaat?
Yeah, you can convert a raw pointer into a delegate.
So basically you need three things for each native method:
- The delegate signature (which maps to the native method)
```csharp
delegate int myFun(int param);
```
- A field which will hold the delegate implementation
```csharp
myFun s_myFun = Load<myFun>("myNativeFunctionName");
```
- A public method which will call the delegate stored in this field (it have the same signature as the delegate in most cases)
```csharp
public int MyNativeFunction(int param) => s_myFun(param);
```

Which map to one DllImport signature:
```csharp
[DllImport("MyPreciousLib.dll")]
public static extern int myFun(int param);
```

# Well, I guess I need a step-by-step guide 
1. Write, generate or take an existing DllImport-based source
2. Pass each source file to the tool (``extern-nll-gen MySource.cs``)
3. The tool will print the modified source. Save it
4. Implement the ``__LoadFunction`` method

The last step is probably the hardest part of the whole process.

Well, to be honest, it's not that hard. There is a [ready-to-use library for it]().

Here is an example from the [SDL2 wrapper](https://github.com/project-grove/SDL2.NETCore):
```csharp
using System.Runtime.InteropServices;
using NativeLibraryLoader;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace SDL2.Internal
{
    internal static class Loader_SDL2
    {
        /* -------- these are just helpers, you can ignore them -------- */
        // the handle to the native library
        static NativeLibrary _sdl2;        

        // an internal property to ease access to it
        internal static NativeLibrary Sdl2 {
            get {
                if (_sdl2 == null) {
                    _sdl2 = LoadSDL2();
                }
                return _sdl2;
            }
        }

        /* ------------------ now the important part ------------------ */
        // the method which loads the library (notice the DLL names)
        static NativeLibrary LoadSDL2()
        {
            string[] names = null;
            if (IsOSPlatform(OSPlatform.Windows)) {
                names = new [] { "SDL2.dll" };
            } else if (IsOSPlatform(OSPlatform.OSX)) {
                names = new [] { "libSDL2.dylib" };
            } else {
                names = new [] {
                    "libSDL2-2.0.so.0",
                    "libSDL2.so"
                };
            }
            return new NativeLibrary(names);
        }

        // and finally a __LoadFunction implementation (pretty easy)
        internal static T LoadFunction<T>(string name)
        {
            return Sdl2.LoadFunction<T>(name);
        }
    }
}
```


In future we will possibly get a similar thing in the [framework](https://github.com/dotnet/corefx/issues/32015), but it's in the future and we need it now.

# Installation
```
dotnet build
dotnet pack -o .
dotnet tool install -g extern-nll-gen --add-source src
```

# Usage
```
extern-nll-gen MyPreciousDllImportSource.cs
```

# Why extern-nll-gen?
Extern is for ``extern`` methods.

nll is for NativeLibraryLoader.

gen is for generate.
