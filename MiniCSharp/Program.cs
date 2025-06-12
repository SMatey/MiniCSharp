using System;
using System.IO;
using System.Collections.Generic; // Necesario para List<string> en SyntaxErrorListener
using Antlr4.Runtime;
// Asegúrate que el namespace de tu checker es correcto aquí
using MiniCSharp.Grammar.Checker; // Namespace donde reside MiniCsharpChecker y TablaSimbolos
using parser.generated; // Namespace de tu Lexer y Parser generados por ANTLR

namespace MiniCSharp // O el namespace principal de tu proyecto
{
    class Program
    {
        static void Main(string[] args)
        {
            // NOTA: El filePath está hardcodeado. Puede ser mejor hacerlo relativo o configurable.
            var filePath = "C:\\Users\\Lizsa\\OneDrive\\Documents\\GitHub\\MiniCSharp\\MiniCSharp\\test3.txt"; 

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

                // Remover el listener de errores por defecto y añadir uno personalizado
                // para capturar errores de sintaxis de forma más controlada.
                parser.RemoveErrorListeners(); 
                var syntaxErrorListener = new SyntaxErrorListener();
                parser.AddErrorListener(syntaxErrorListener);

                // Parsear y obtener el árbol sintáctico.
                // El método parser.program() devuelve un ProgramContext.
                MiniCSharpParser.ProgramContext tree = parser.program(); // Cambio clave aquí

                // Verificar si hubo errores de sintaxis durante el parseo
                if (syntaxErrorListener.HasErrors)
                {
                    Console.WriteLine("Errores de sintaxis detectados por el parser:");
                    foreach (var error in syntaxErrorListener.ErrorMessages)
                    {
                        Console.WriteLine(error);
                    }
                    Console.WriteLine("No se procederá al análisis semántico debido a errores de sintaxis.");
                    return; 
                }

                Console.WriteLine("Análisis sintáctico completado sin errores.");
                
                Console.WriteLine("\nIniciando análisis semántico (Checker)...");

                // Crear una instancia del Checker
                MiniCsharpChecker checker = new MiniCsharpChecker();
                checker.Visit(tree); 

                // Verificar si el Checker reportó errores semánticos
                if (checker.Errors.Count > 0)
                {
                    Console.WriteLine($"\nSe encontraron {checker.Errors.Count} errores semánticos:");
                    foreach (string error in checker.Errors)
                    {
                        Console.WriteLine(error);
                    }
                }
                else
                {
                    Console.WriteLine("\nAnálisis semántico (Checker) completado sin errores reportados.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError inesperado durante la compilación: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    // Clase auxiliar para capturar errores de sintaxis del parser
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