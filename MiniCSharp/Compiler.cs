namespace MiniCSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Antlr4.Runtime;
using MiniCSharp.Grammar.Checker;     
using MiniCSharp.Grammar.encoder;    
using parser.generated; 

public class Compiler
{
    // Clase de apoyo para el resultado de la compilación (sin cambios)
    public class CompilationResult
    {
        public bool Success { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string ExecutablePath { get; set; }
        public string Output { get; set; }
    }

    // El método principal que orquesta todo el proceso
    public static CompilationResult CompileAndRun(string filePath)
    {
        var result = new CompilationResult();

        try
        {
            // --- FASE 1: Análisis Léxico y Sintáctico ---
            string sourceCode = File.ReadAllText(filePath);
            var inputStream = new AntlrInputStream(sourceCode);
            var lexer = new MiniCSharpLexer(inputStream); // Ahora se resuelve
            var tokenStream = new CommonTokenStream(lexer);
            var parser = new MiniCSharpParser(tokenStream); // Ahora se resuelve

            var syntaxErrorListener = new SyntaxErrorListener();
            parser.RemoveErrorListeners();
            parser.AddErrorListener(syntaxErrorListener);

            var tree = parser.program(); // El método de tu regla inicial (prog)

            if (syntaxErrorListener.Errors.Count > 0)
            {
                result.Success = false;
                result.Errors.AddRange(syntaxErrorListener.Errors);
                return result;
            }

            // --- FASE 2: Análisis Semántico ---
            // FIX: Usamos el nombre exacto de tu clase 'MiniCsharpChecker'
            var checker = new MiniCsharpChecker(); 
            checker.Visit(tree);

            if (checker.Errors.Count > 0)
            {
                result.Success = false;
                result.Errors.AddRange(checker.Errors);
                return result;
            }

            // --- FASE 3: Generación de Código ---
            string outputExePath = Path.ChangeExtension(filePath, ".exe");
            // FIX: Usamos el nombre exacto de tu clase 'CodeGen'
            var codeGenerator = new CodeGen(outputExePath); 
            codeGenerator.Visit(tree);
            // FIX: Llamamos al método para guardar que creaste en tu CodeGen
            codeGenerator.SaveAssembly();

            result.ExecutablePath = outputExePath;

            // --- FASE 4: Ejecución ---
            if (File.Exists(result.ExecutablePath))
            {
                Process process = new Process();
                process.StartInfo.FileName = result.ExecutablePath;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;
                
                process.Start();

                string output = process.StandardOutput.ReadToEnd();
                string errorOutput = process.StandardError.ReadToEnd();
                
                process.WaitForExit();
                
                result.Success = true;
                result.Output = output;
                if (!string.IsNullOrEmpty(errorOutput))
                {
                    result.Output += "\n--- Errores de Ejecución ---\n" + errorOutput;
                }
            }
            else
            {
                result.Success = false;
                result.Errors.Add("Error: La generación de código no produjo un archivo ejecutable.");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Error fatal del compilador: {ex.Message}\n{ex.StackTrace}");
        }

        return result;
    }
}

public class SyntaxErrorListener : BaseErrorListener
{
    public readonly List<string> Errors = new List<string>();

    // Esta es la firma más probable que tu versión de ANTLR espera.
    // Incluye el parámetro 'TextWriter output'.
    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e)
    {
        // El cuerpo del método no cambia.
        string errorMessage = $"Error Sintáctico: {msg} (línea {line}, columna {charPositionInLine + 1})";
        Errors.Add(errorMessage);
    }
}