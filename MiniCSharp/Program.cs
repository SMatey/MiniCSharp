using System;
using System.IO;
using System.Collections.Generic;
using Antlr4.Runtime;
using MiniCSharp.Grammar.Checker;
using MiniCSharp.Grammar.encoder;
using parser.generated;

namespace MiniCSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            var filePath = "C:\\Users\\casam\\OneDrive\\Documentos\\Anio 2025\\semestre 1\\QA\\MiniCSharp\\MiniCSharp\\testCodeGen1.txt"; 

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: El archivo de prueba '{filePath}' no fue encontrado.");
                return;
            }

            try
            {
                var input = File.ReadAllText(filePath);
                var inputStream = new AntlrInputStream(input);
                var lexer = new MiniCSharpLexer(inputStream);
                var tokens = new CommonTokenStream(lexer);
                var parser = new MiniCSharpParser(tokens);

                parser.RemoveErrorListeners(); 
                var syntaxErrorListener = new SyntaxErrorListener();
                parser.AddErrorListener(syntaxErrorListener);

                MiniCSharpParser.ProgramContext tree = parser.program(); 

                if (syntaxErrorListener.HasErrors)
                {
                    Console.WriteLine("Errores de sintaxis detectados por el parser. Compilación detenida.");
                    foreach (var error in syntaxErrorListener.ErrorMessages)
                    {
                        Console.WriteLine(error);
                    }
                    return; 
                }

                Console.WriteLine("Análisis sintáctico completado sin errores.");
                
                // --- FASE 2: ANÁLISIS SEMÁNTICO (CHECKER) ---
                Console.WriteLine("\nIniciando análisis semántico (Checker)...");
                MiniCsharpChecker checker = new MiniCsharpChecker();
                checker.Visit(tree); 

                if (checker.Errors.Count > 0)
                {
                    Console.WriteLine($"\nSe encontraron {checker.Errors.Count} errores semánticos. Compilación detenida.");
                    foreach (string error in checker.Errors)
                    {
                        Console.WriteLine(error);
                    }
                    return; // Detener si hay errores semánticos
                }
                
                Console.WriteLine("\nAnálisis semántico (Checker) completado sin errores reportados.");

                // --- FASE 3: GENERACIÓN DE CÓDIGO ---
                Console.WriteLine("\nIniciando generación de código...");
                
                // 1. Crear una instancia del CodeGen, pasándole el nombre del archivo de salida.
                var outputFileName = "output.exe";
                CodeGen codeGenerator = new CodeGen(outputFileName);

                // 2. Visitar el árbol para generar el código CIL en memoria.
                codeGenerator.Visit(tree);

                // 3. Guardar el ensamblado generado en un archivo .exe.
                codeGenerator.SaveAssembly();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError inesperado durante la compilación: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    // Clase auxiliar para capturar errores de sintaxis del parser (sin cambios)
    public class SyntaxErrorListener : BaseErrorListener
    {
        public List<string> ErrorMessages { get; } = new List<string>();
        public bool HasErrors => ErrorMessages.Count > 0;

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            ErrorMessages.Add($"Error de sintaxis en línea {line}:{charPositionInLine} -> {msg} (token: '{offendingSymbol?.Text}')");
        }
    }
}