using System;
using System.IO;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using MiniCSharp.Gramma;

class Program
{
    static void Main(string[] args)
    {
        var filePath = "test.mcs"; // o Samples/test.mcs si lo pusiste en carpeta

        if (!File.Exists(filePath))
        {
            Console.WriteLine("Archivo de prueba no encontrado.");
            return;
        }

        var input = File.ReadAllText(filePath);
        var inputStream = new AntlrInputStream(input);
        var lexer = new MiniCSharpLexer(inputStream);
        var tokens = new CommonTokenStream(lexer);
        var parser = new MiniCSharpParser(tokens);

        // Opción para mostrar errores de forma más detallada
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new DiagnosticErrorListener());

        // Parsear
        var tree = parser.program();

        // Mostrar el árbol
        Console.WriteLine(tree.ToStringTree(parser));
    }
}