namespace MiniCSharp
{
    using Antlr4.Runtime;
    using System.Collections.Generic;
    using System.Linq;
    using System;

    public class TablaSimbolos
    {
        private List<Ident> tabla;
        private int nivelActual;

        // Tipos soportados
        public const int IntType = 0;
        public const int DoubleType = 1;
        public const int CharType = 2;
        public const int BoolType = 3;
        public const int StringType = 4;
        public const int VoidType = 5;
        public const int ClassType = 6;
        public const int ArrayType = 7;
        public const int NullType = 8;
        public const int UnknownType = -1;

        public TablaSimbolos()
        {
            this.tabla = new List<Ident>();
            this.nivelActual = -1;
        }

        public int NivelActual => nivelActual;

        // Identificador base para todas las entradas
        public abstract class Ident
        {
            public IToken Token { get; set; }
            public int Type { get; set; }
            public int Nivel { get; set; }
            public ParserRuleContext DeclCtx { get; set; }

            protected Ident(IToken token, int type, ParserRuleContext declCtx, int nivel)
            {
                this.Token = token;
                this.Type = type;
                this.Nivel = nivel;
                this.DeclCtx = declCtx;
            }

            public string GetName() => Token.Text;
        }

        // Variable: Puede ser un tipo simple o un array
        public class VarIdent : Ident
        {
            public bool IsArray { get; set; }

            public VarIdent(IToken token, int type, bool isArray, ParserRuleContext declCtx, int nivel)
                : base(token, type, declCtx, nivel)
            {
                this.IsArray = isArray;
            }
        }

        // Método: Con una lista de parámetros
        public class MethodIdent : Ident
        {
            public List<ParamIdent> Params { get; private set; }
            public int ReturnType => Type;

            public MethodIdent(IToken token, int returnType, ParserRuleContext declCtx, int nivel)
                : base(token, returnType, declCtx, nivel)
            {
                this.Params = new List<ParamIdent>();
            }

            public void AddParam(ParamIdent param)
            {
                this.Params.Add(param);
            }
        }

        // Parámetro de método: Es un tipo de VarIdent
        public class ParamIdent : VarIdent
        {
            public ParamIdent(IToken token, int type, bool isArray, ParserRuleContext declCtx, int nivel)
                : base(token, type, isArray, declCtx, nivel)
            {
            }
        }

        // Clase: Tiene miembros (variables y métodos)
        public class ClassIdent : Ident
        {
            public TablaSimbolos Members { get; private set; }

            public ClassIdent(IToken token, ParserRuleContext declCtx, int nivel)
                : base(token, ClassType, declCtx, nivel)
            {
                this.Members = new TablaSimbolos();
            }
        }

        // Manejo de scopes
        public void OpenScope()
        {
            this.nivelActual++;
        }

        public void CloseScope()
        {
            if (this.nivelActual < 0) return;
            tabla.RemoveAll(ident => ident.Nivel == this.nivelActual);
            this.nivelActual--;
        }

        // Inserción genérica de identificadores
        private Ident InsertarIdent(IToken idToken, int type, ParserRuleContext declCtx, int nivel, Ident newIdent)
        {
            if (BuscarNivelActual(idToken.Text) != null)
                return null;

            tabla.Insert(0, newIdent);
            return newIdent;
        }

        // Inserción de variables
        public VarIdent InsertarVar(IToken idToken, int type, bool isArray, ParserRuleContext declCtx)
        {
            VarIdent newIdent = new VarIdent(idToken, type, isArray, declCtx, this.nivelActual);
            return (VarIdent)InsertarIdent(idToken, type, declCtx, this.nivelActual, newIdent);
        }

        // Inserción de métodos
        public MethodIdent InsertarMethod(IToken idToken, int returnType, ParserRuleContext declCtx)
        {
            MethodIdent newIdent = new MethodIdent(idToken, returnType, declCtx, this.nivelActual);
            return (MethodIdent)InsertarIdent(idToken, returnType, declCtx, this.nivelActual, newIdent);
        }

        // Inserción de clases
        public ClassIdent InsertarClass(IToken idToken, ParserRuleContext declCtx)
        {
            ClassIdent newIdent = new ClassIdent(idToken, declCtx, this.nivelActual);
            return (ClassIdent)InsertarIdent(idToken, ClassType, declCtx, this.nivelActual, newIdent);
        }

        // Búsqueda en todos los scopes (más cercano primero)
        public Ident Buscar(string nombre)
        {
            foreach (Ident id in tabla)
            {
                if (id.GetName() == nombre)
                    return id;
            }
            return null;
        }

        // Búsqueda solo en el scope actual
        public Ident BuscarNivelActual(string nombre)
        {
            foreach (Ident id in tabla)
            {
                if (id.Nivel == this.nivelActual && id.GetName() == nombre)
                    return id;
            }
            return null;
        }

        // Conversión de código de tipo a string
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
                case ArrayType: return "array";
                case NullType: return "null";
                default: return "unknown_type";
            }
        }

        // Para depuración
        public void Imprimir()
        {
            Console.WriteLine($"----- INICIO TABLA (Nivel Actual: {nivelActual}) ------");
            foreach (Ident id in tabla)
            {
                Console.Write($"Nombre: {id.GetName()} (Nivel: {id.Nivel}, Tipo: {TypeToString(id.Type)}");
                if (id is VarIdent varId)
                {
                    Console.Write($", EsArray: {varId.IsArray}");
                }
                else if (id is MethodIdent methodId)
                {
                    string paramsStr = string.Join(", ", methodId.Params.Select(p => $"{TypeToString(p.Type)}{(p.IsArray ? "[]" : "")} {p.GetName()}"));
                    Console.Write($", Params: [{paramsStr}]");
                }
                else if (id is ClassIdent)
                {
                    Console.Write($" -> Miembros: (use classId.Members.Imprimir() para detalles)");
                }
                Console.WriteLine(")");
            }
            Console.WriteLine("----- FIN TABLA ------");
        }
        
        // Inserción de parámetros en el contexto del método
        public ParamIdent InsertarParam(MethodIdent method, IToken idToken, int type, bool isArray, ParserRuleContext declCtx, int nivel, bool addToScope = true)
        {
            // Verificar que no haya parámetros duplicados en el método
            if (method.Params.Any(p => p.GetName() == idToken.Text))
                return null;

            // Crear el nuevo identificador de parámetro
            ParamIdent newIdent = new ParamIdent(idToken, type, isArray, declCtx, nivel);
            method.AddParam(newIdent);  // Añadirlo a la lista de parámetros del método

            // Opcionalmente agregarlo a la tabla de símbolos si se requiere (usualmente es el caso)
            if (addToScope)
                tabla.Insert(0, newIdent);

            return newIdent;
        }

    }
}
