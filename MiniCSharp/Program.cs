using System;
using System.IO;
using System.Reflection;
using MiniCSharp.Grammar.Checker;
using MiniCSharp.Grammar.encoder;
using parser.generated;

namespace MiniCSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            var filePath = "C:\\Users\\Lizsa\\OneDrive\\Documents\\GitHub\\MiniCSharp\\MiniCSharp\\testCodeGen2.txt"; 
            var outputDllName = "output.dll";

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: El archivo de prueba '{filePath}' no fue encontrado.");
                return;
            }

            // --- FASE 1 & 2: PARSING Y CHECKING ---
            Console.WriteLine("--- Fase de Compilación ---");
            try
            {
                var input = File.ReadAllText(filePath);
                var inputStream = new Antlr4.Runtime.AntlrInputStream(input);
                var lexer = new MiniCSharpLexer(inputStream);
                var tokens = new Antlr4.Runtime.CommonTokenStream(lexer);
                var parser = new MiniCSharpParser(tokens);
                var tree = parser.program();
                
                // (Opcional: puedes añadir el listener de errores de sintaxis aquí si quieres)

                var checker = new MiniCsharpChecker();
                checker.Visit(tree);

                if (checker.Errors.Count > 0)
                {
                    Console.WriteLine($"\nSe encontraron {checker.Errors.Count} errores semánticos. Compilación detenida.");
                    foreach (string error in checker.Errors)
                    {
                        Console.WriteLine(error);
                    }
                    return;
                }
                Console.WriteLine("Análisis sintáctico y semántico completado sin errores.");
                
                // --- FASE 3: GENERACIÓN DE CÓDIGO ---
                Console.WriteLine("\nIniciando generación de código...");
                CodeGen codeGenerator = new CodeGen(outputDllName);
                codeGenerator.Visit(tree);
                codeGenerator.SaveAssembly();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError inesperado durante la compilación: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return;
            }

            // --- FASE 4: EJECUCIÓN DE LA DLL GENERADA ---
            Console.WriteLine("\n--- Fase de Ejecución ---");
            try
            {
                Assembly generatedAssembly = Assembly.LoadFrom(Path.GetFullPath(outputDllName));
                Type? programType = generatedAssembly.GetType("PruebaAsignaciones"); 
                
                if (programType != null)
                {
                    MethodInfo? mainMethod = programType.GetMethod("Main");
                    if (mainMethod != null)
                    {
                        Console.WriteLine("Ejecutando Main()...");
                        object? result = mainMethod.Invoke(null, null);

                        if (mainMethod.ReturnType != typeof(void))
                        {
                            Console.WriteLine($"Main() devolvió: {result}");
                        }
                        Console.WriteLine("Ejecución finalizada.");
                    }
                    else
                    {
                        Console.WriteLine("Error: No se encontró el método 'Main' en la clase generada.");
                    }
                }
                else
                {
                    Console.WriteLine("Error: No se encontró la clase principal en el ensamblado generado.");
                }
            }
            catch (Exception ex)
            {
                // --- INICIO DE LA MODIFICACIÓN ---

                // La excepción real está en la propiedad "InnerException".
                var innerEx = ex.InnerException ?? ex;

                Console.WriteLine($"\n--- ERROR DENTRO DEL CÓDIGO GENERADO ---");
                Console.WriteLine($"Tipo de Excepción: {innerEx.GetType().Name}");
                Console.WriteLine($"Mensaje: {innerEx.Message}");
                Console.WriteLine("Stack Trace del código generado:");
                Console.WriteLine(innerEx.StackTrace);
            }
        }
    }
}