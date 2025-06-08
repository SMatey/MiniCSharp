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

        public abstract class Ident
        {
            public IToken Token { get; set; }
            public int Type { get; set; }
            public int Nivel { get; set; }
            public ParserRuleContext DeclCtx { get; set; }

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
            public List<ParamIdent> Params { get; private set; }
            public int ReturnType => Type;

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
            public ParamIdent(IToken token, int type, bool isArray, ParserRuleContext declCtx, int methodBodyNivel)
                : base(token, type, methodBodyNivel, isArray, declCtx)
            {
            }
        }

        public class ClassIdent : Ident
        {
            public TablaSimbolos Members { get; private set; }

            public ClassIdent(IToken token, ParserRuleContext declCtx, int currentNivel)
                : base(token, ClassType, declCtx, currentNivel)
            {
                this.Members = new TablaSimbolos();
            }
        }

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

        public Ident InsertarVar(IToken idToken, int type, bool isArray, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                return null;
            }
            VarIdent newIdent = new VarIdent(idToken, type, isArray, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        public MethodIdent InsertarMethod(IToken idToken, int returnType, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                return null;
            }
            MethodIdent newIdent = new MethodIdent(idToken, returnType, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        // --- MÉTODO MODIFICADO ---
        public ParamIdent InsertarParam(MethodIdent method, IToken idToken, int type, bool isArray, ParserRuleContext declCtx, int methodBodyNivel, bool addToScope = true)
        {
            if (method.Params.Any(p => p.GetName() == idToken.Text))
            {
                return null;
            }

            ParamIdent newIdent = new ParamIdent(idToken, type, isArray, declCtx, methodBodyNivel);
            method.AddParam(newIdent); // Siempre se añade a la definición del método.

            // Solo se añade a la tabla de símbolos del scope actual si se indica.
            // Esto evita que los parámetros de 'len', 'ord', 'chr' contaminen el scope global.
            if (addToScope)
            {
                tabla.Insert(0, newIdent);
            }
            
            return newIdent;
        }

        public ClassIdent InsertarClass(IToken idToken, ParserRuleContext declCtx)
        {
            if (BuscarNivelActual(idToken.Text) != null)
            {
                return null;
            }
            ClassIdent newIdent = new ClassIdent(idToken, declCtx, this.nivelActual);
            tabla.Insert(0, newIdent);
            return newIdent;
        }

        public Ident Buscar(string nombre)
        {
            foreach (Ident id in tabla)
            {
                if (id.GetName() == nombre)
                {
                    return id;
                }
            }
            return null;
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
                    break;
                }
            }
            return null;
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
                case ArrayType: return "array";
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
    }
}