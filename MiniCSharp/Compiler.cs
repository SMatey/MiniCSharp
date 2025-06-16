// Compiler.cs (Versión Final para Ejecución en Memoria)
namespace MiniCSharp
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using Antlr4.Runtime;
    using MiniCSharp.Grammar.Checker;
    using MiniCSharp.Grammar.encoder;
    using parser.generated;

    public static class Compiler
    {
        public class CompilationResult
        {
            public bool Success { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
            public string Output { get; set; }
        }

        public static CompilationResult CompileAndRun(string filePath)
        {
            var result = new CompilationResult();
            Assembly generatedAssembly = null;

            try
            {
                // Fases 1 & 2: Parsing y Checking (sin cambios)
                string sourceCode = File.ReadAllText(filePath);
                var inputStream = new AntlrInputStream(sourceCode);
                var lexer = new MiniCSharpLexer(inputStream);
                var tokenStream = new CommonTokenStream(lexer);
                var parser = new MiniCSharpParser(tokenStream);

                var syntaxErrorListener = new SyntaxErrorListener();
                parser.RemoveErrorListeners();
                parser.AddErrorListener(syntaxErrorListener);

                var tree = parser.program();

                if (syntaxErrorListener.Errors.Count > 0)
                {
                    result.Success = false;
                    result.Errors.AddRange(syntaxErrorListener.Errors);
                    return result;
                }

                var checker = new MiniCsharpChecker();
                checker.Visit(tree);

                if (checker.Errors.Count > 0)
                {
                    result.Success = false;
                    result.Errors.AddRange(checker.Errors);
                    return result;
                }

                // Fase 3: Generación de Código en Memoria
                var codeGenerator = new CodeGen(); // Llama al constructor corregido
                generatedAssembly = (Assembly)codeGenerator.Visit(tree); // Visit(tree) devuelve el Assembly
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"Error fatal durante la compilación: {ex.Message}\n{ex.StackTrace}");
                return result;
            }

            // Fase 4: Ejecución desde Memoria con Reflection
            if (result.Success && generatedAssembly != null)
            {
                var originalConsoleOut = Console.Out;
                using (var writer = new StringWriter())
                {
                    Console.SetOut(writer);
                    try
                    {
                        MethodInfo mainMethod = null;
                        // Buscamos en todos los tipos definidos en nuestro ensamblado
                        foreach (var type in generatedAssembly.GetTypes())
                        {
                            mainMethod = type.GetMethod("Main");
                            if (mainMethod != null) break;
                        }

                        if (mainMethod != null)
                        {
                            mainMethod.Invoke(null, null); // Invocamos el método Main
                        }
                        else
                        {
                            writer.Write("Error de ejecución: No se encontró el método 'Main'.");
                        }
                    }
                    catch (Exception ex)
                    {
                        var innerEx = ex.InnerException ?? ex;
                        writer.Write($"--- ERROR EN TIEMPO DE EJECUCIÓN ---\n");
                        writer.Write($"Error: {innerEx.Message}\n");
                        writer.Write($"Stack Trace:\n{innerEx.StackTrace}");
                    }
                    finally
                    {
                        result.Output = writer.ToString();
                        Console.SetOut(originalConsoleOut);
                    }
                }
            }
            return result;
        }
    }

    // Tu SyntaxErrorListener
    public class SyntaxErrorListener : BaseErrorListener
    {
        public readonly List<string> Errors = new List<string>();

        public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
        {
            string errorMessage = $"Error Sintáctico: {msg} (línea {line}, columna {charPositionInLine + 1})";
            Errors.Add(errorMessage);
        }
    }
}