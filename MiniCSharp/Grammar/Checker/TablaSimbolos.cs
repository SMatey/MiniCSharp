namespace MiniCSharp
{
    using Antlr4.Runtime; // Para IToken y ParserRuleContext
    using System.Collections.Generic;
    using System.Linq;
    using System; // Para Console.WriteLine en Imprimir

    public class TablaSimbolos
    {
        private List<Ident> tabla; // Usamos List<Ident> en C#
        private int nivelActual;

        // Constantes para tipos
        public const int IntType = 0;
        public const int DoubleType = 1;
        public const int CharType = 2;
        public const int BoolType = 3;
        public const int StringType = 4;
        public const int VoidType = 5;
        public const int ClassType = 6;
        public const int ArrayType = 7; // Un tipo genérico para array, el tipo base se almacena en 'Type'
        public const int NullType = 8;
        public const int UnknownType = -1;

        public TablaSimbolos()
        {
            this.tabla = new List<Ident>();
            this.nivelActual = -1; // Nivel global/raíz
        }

        public int NivelActual => nivelActual; // Propiedad de solo lectura

        // --- Clases base para Identificadores ---
        public abstract class Ident
        {
            public IToken Token { get; set; } // Token del identificador (nombre)
            public int Type { get; set; }    // Tipo del identificador (usando las constantes de arriba)
            public int Nivel { get; set; }   // Nivel de ámbito donde fue declarado
            public ParserRuleContext DeclCtx { get; set; } // Contexto del árbol donde fue declarado

            protected Ident(IToken token, int type, ParserRuleContext declCtx, int currentNivel)
            {
                this.Token = token;
                this.Type = type;
                this.Nivel = currentNivel;
                this.DeclCtx = declCtx;
            }

            protected Ident(IToken token, int type, int nivel, ParserRuleContext declCtx)
            {
                this.Token = token;
                this.Type = type;
                this.Nivel = nivel;
                this.DeclCtx = declCtx;
            }

            public string GetName() => Token.Text;
        }

        // --- Tipos específicos de Identificadores ---

        public class VarIdent : Ident
        {
            public bool IsArray { get; set; }

            public VarIdent(IToken token, int type, bool isArray, ParserRuleContext declCtx, int currentNivel)
                : base(token, type, declCtx, currentNivel)
            {
                this.IsArray = isArray;
            }
            public VarIdent(IToken token, int type, int nivel, bool isArray, ParserRuleContext declCtx)
                : base(token, type, nivel, declCtx)
            {
                this.IsArray = isArray;
            }
        }

        public class MethodIdent : Ident
        {
            public List<ParamIdent> Params { get; private set; } // Lista de parámetros formales
            public int ReturnType => Type; // Coincide con 'Type' de la clase base Ident

            public MethodIdent(IToken token, int returnType, ParserRuleContext declCtx, int currentNivel)
                : base(token, returnType, declCtx, currentNivel)
            {
                this.Params = new List<ParamIdent>();
            }

            public void AddParam(ParamIdent param)
            {
                this.Params.Add(param);
            }
        }

        public class ParamIdent : VarIdent
        {
            // Los parámetros viven en el nivel del cuerpo del método (nivelActual + 1 al momento de la declaración del método)
            public ParamIdent(IToken token, int type, bool isArray, ParserRuleContext declCtx, int methodBodyNivel)
                : base(token, type, methodBodyNivel, isArray, declCtx)
            {
            }
        }

        public class ClassIdent : Ident
        {
            public TablaSimbolos Members { get; private set; } // Cada clase tiene su propia tabla de símbolos para miembros

            public ClassIdent(IToken token, ParserRuleContext declCtx, int currentNivel)
                : base(token, ClassType, declCtx, currentNivel)
            {
                this.Members = new TablaSimbolos();
            }
        }

        // --- Métodos de Gestión de la Tabla ---

        public void OpenScope()
        {
            this.nivelActual++;
        }

        public void CloseScope()
        {
            if (this.nivelActual < 0) return;

            // Eliminar identificadores del nivel actual. Insert(0,...) asegura que están al principio para este nivel.
            // O podemos usar LINQ RemoveAll, que es más claro.
            tabla.RemoveAll(ident => ident.Nivel == this.nivelActual);
            this.nivelActual--;
        }

        public Ident InsertarVar(IToken idToken, int type, bool isArray, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                // Error: Ya existe en el ámbito actual (manejar en el Checker)
                return null;
            }
            VarIdent newIdent = new VarIdent(idToken, type, isArray, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent); // Insertar al principio para el ámbito actual
            return newIdent;
        }

        public MethodIdent InsertarMethod(IToken idToken, int returnType, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                // Error: Ya existe en el ámbito actual (manejar en el Checker)
                return null;
            }
            MethodIdent newIdent = new MethodIdent(idToken, returnType, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        public ParamIdent InsertarParam(MethodIdent method, IToken idToken, int type, bool isArray, ParserRuleContext declCtx, int methodBodyNivel)
        {
            // Validar si el parámetro ya existe en la lista de parámetros del método (por nombre)
            if (method.Params.Any(p => p.GetName() == idToken.Text))
            {
                 // Error: Parámetro con el mismo nombre ya existe para este método (manejar en el Checker)
                return null;
            }

            ParamIdent newIdent = new ParamIdent(idToken, type, isArray, declCtx, methodBodyNivel);
            method.AddParam(newIdent);
            // También lo añadimos a la tabla general para que sea localizable durante el análisis del cuerpo del método.
            // Su 'Nivel' ya está ajustado al nivel del cuerpo del método.
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        public ClassIdent InsertarClass(IToken idToken, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                // Error: Ya existe en el ámbito actual (manejar en el Checker)
                return null;
            }
            ClassIdent newIdent = new ClassIdent(idToken, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        public Ident Buscar(string nombre)
        {
            // Busca desde el ámbito más interno (principio de la lista) hacia afuera.
            foreach (Ident id in tabla)
            {
                if (id.GetName() == nombre)
                {
                    return id;
                }
            }
            return null; // No encontrado
        }

        public Ident BuscarNivelActual(string nombre)
        {
            foreach (Ident id in tabla)
            {
                if (id.Nivel == this.nivelActual)
                {
                    if (id.GetName() == nombre)
                    {
                        return id;
                    }
                }
                else if (id.Nivel < this.nivelActual)
                {
                    // Optimización: Si ya pasamos a niveles inferiores (más antiguos en la lista), no estará en el actual.
                    break;
                }
            }
            return null; // No encontrado en el nivel actual
        }

        public static string TypeToString(int typeCode)
        {
            switch (typeCode)
            {
                case IntType: return "int";
                case DoubleType: return "double";
                case CharType: return "char";
                case BoolType: return "bool";
                case StringType: return "string";
                case VoidType: return "void";
                case ClassType: return "class";
                case ArrayType: return "array"; // Podrías querer más detalle aquí si conoces el tipo base
                case NullType: return "null";
                default: return "unknown_type";
            }
        }

        public void Imprimir()
        {
            Console.WriteLine($"----- INICIO TABLA (Nivel Actual: {nivelActual}) ------");
            foreach (Ident id in tabla)
            {
                Console.Write($"Nombre: {id.GetName()} (Nivel: {id.Nivel}, Tipo: {TypeToString(id.Type)}");
                if (id is VarIdent varId) // Usando pattern matching
                {
                    Console.Write($", EsArray: {varId.IsArray}");
                }
                else if (id is MethodIdent methodId)
                {
                    string paramsStr = string.Join(", ", methodId.Params.Select(p => $"{TypeToString(p.Type)}{(p.IsArray ? "[]" : "")} {p.GetName()}"));
                    Console.Write($", Params: [{paramsStr}]");
                }
                else if (id is ClassIdent classId)
                {
                     // Podrías querer imprimir los miembros de forma más selectiva o completa
                     Console.Write($" -> Miembros: (use classId.Members.Imprimir() para detalles)");
                }
                Console.WriteLine(")");
            }
            Console.WriteLine("----- FIN TABLA ------");
        }
    }
}