using Lux.Compiler;

namespace Lux;

internal class Program
{
    private static void Main(string[] args)
    {
        var testFile = Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "test.lux");

        var compiler = new LuxCompiler();
        compiler.AddSource(testFile);

        var sucess = compiler.Compile();
        if (!sucess)
        {
            Console.WriteLine("Compilation FAILED.");

            foreach (var diag in compiler.Diagnostics.Diagnostics)
            {
                Console.WriteLine(diag.ToString());
            }
        }
        else
        {
            Console.WriteLine("Compilation SUCCESS.");
            Console.WriteLine("--- Generated Lua ---");
            foreach (var pkg in compiler.Packages.Values)
            {
                foreach (var file in pkg.Files)
                {
                    Console.WriteLine(file.GeneratedLua);
                }
            }
        }
    }
}