using System;
using System.Collections.Generic;
using System.IO; 
using System.Reflection;
using System.Reflection.Emit;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Lokad.ILPack; 
using parser.generated;
using System.Globalization;

namespace MiniCSharp.Grammar.encoder
{
    public class CodeGen : MiniCSharpParserBaseVisitor<object>
    {
        // --- CAMPOS DE LA CLASE (DECLARADOS AQUÍ) ---
        private readonly string _outputFileName;
        private readonly AssemblyName _assemblyName;
        private readonly AssemblyBuilder _assemblyBuilder;
        private readonly ModuleBuilder _moduleBuilder;
        private TypeBuilder _typeBuilder;
        private ILGenerator _ilGenerator;
        private MethodBuilder _currentMethod;
        private readonly List<Dictionary<string, LocalBuilder>> _scopedLocalVariables;

        // Referencia al constructor de la clase y al método Main
        private MethodBuilder _mainMethodBuilder;
        
        // Diccionario para registrar variables locales
        private readonly Dictionary<ParserRuleContext, LocalBuilder> _localVariables;

        /// <summary>
        /// El constructor prepara los objetos base para crear un ensamblado (.exe) dinámico.
        /// </summary>
        public CodeGen(string outputFileName)
        {
             this._outputFileName = outputFileName;
            _assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(outputFileName));
            
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.Run);

            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(outputFileName);
    
            _localVariables = new Dictionary<ParserRuleContext, LocalBuilder>();
            
            _scopedLocalVariables = new List<Dictionary<string, LocalBuilder>>();
        }
        
        // --- MÉTODOS AUXILIARES PARA GESTIÓN DE ÁMBITOS ---
        private void OpenScope()
        {
            _scopedLocalVariables.Add(new Dictionary<string, LocalBuilder>());
        }

        private void CloseScope()
        {
            if (_scopedLocalVariables.Count > 0)
            {
                _scopedLocalVariables.RemoveAt(_scopedLocalVariables.Count - 1);
            }
        }

        private LocalBuilder DeclareLocal(string name, Type type)
        {
            // El ámbito actual es el último diccionario de la lista.
            var currentScope = _scopedLocalVariables.LastOrDefault();
            if (currentScope == null)
            {
                // No debería pasar si siempre se llama dentro de un ámbito abierto (ej. un método).
                throw new InvalidOperationException("No se puede declarar una variable fuera de un ámbito.");
            }
            if (currentScope.ContainsKey(name))
            {
                // El checker ya debería haber detectado esto, pero es una buena defensa.
                throw new InvalidOperationException($"La variable '{name}' ya está definida en el ámbito actual.");
            }

            LocalBuilder localBuilder = _ilGenerator.DeclareLocal(type);
            currentScope[name] = localBuilder;
            return localBuilder;
        }

        private LocalBuilder FindLocal(string name)
        {
            // Buscar desde el ámbito más interno hacia el más externo.
            foreach (var scope in Enumerable.Reverse(_scopedLocalVariables))
            {
                if (scope.TryGetValue(name, out LocalBuilder localBuilder))
                {
                    return localBuilder;
                }
            }
            return null; // No encontrada
        }
        public void SaveAssembly()
        {
            try
            {
                var generator = new AssemblyGenerator();
                generator.GenerateAssembly(_assemblyBuilder, _outputFileName);
                Console.WriteLine($"Ensamblado '{_outputFileName}' generado exitosamente.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al guardar el ensamblado: {ex.Message}");
            }
        }

        public override object VisitProg(MiniCSharpParser.ProgContext context)
        {
            string className = context.ID().GetText();

            _typeBuilder = _moduleBuilder.DefineType(className, TypeAttributes.Public | TypeAttributes.Class);
    
            if (context.children != null)
            {
                foreach (var child in context.children)
                {
                    if (child is MiniCSharpParser.VarDeclarationContext ||
                        child is MiniCSharpParser.ClassDeclarationContext ||
                        child is MiniCSharpParser.MethodDeclarationContext)
                    {
                        Visit(child);
                    }
                }
            }
    
            // Finalizamos la creación del tipo. Después de esto, no se le pueden añadir más métodos o campos.
            _typeBuilder.CreateType();
    
            return null; 
        }

        public override object VisitUsingStat(MiniCSharpParser.UsingStatContext context)
        {
            return base.VisitUsingStat(context);
        }

        public override object VisitQualifiedIdent(MiniCSharpParser.QualifiedIdentContext context)
        {
            return base.VisitQualifiedIdent(context);
        }

        public override object VisitVarDeclaration(MiniCSharpParser.VarDeclarationContext context)
        {
            // Esta implementación es para VARIABLES LOCALES (dentro de un método).
            if (_ilGenerator == null)
            {
                // TODO: Manejar declaración de campos de clase (variables globales).
                return null;
            }

            // 1. Obtener el tipo de .NET para la variable.
            Type varType = GetTypeFromTypeName(context.type().GetText());

            // 2. Obtener los nodos de los identificadores.
            ITerminalNode[] idNodes = context.ID();

            // 3. Iterar sobre la lista de nodos.
            if (idNodes != null)
            {
                foreach (ITerminalNode idNode in idNodes)
                {
                    IToken idToken = idNode.Symbol;

                    string varName = idToken.Text;

                    DeclareLocal(varName, varType);
            
                    // Console.WriteLine($"CodeGen DBG: Declarada variable local '{varName}' de tipo {varType.Name}");
                }
            }

            return null;
        }

        public override object VisitClassDeclaration(MiniCSharpParser.ClassDeclarationContext context)
        {
            return base.VisitClassDeclaration(context);
        }
        
        /// <summary>
        /// Convierte un nombre de tipo de MiniCSharp a un tipo de .NET para Reflection.Emit.
        /// </summary>
        private Type GetTypeFromTypeName(string typeName)
        {
            // Manejo básico para arrays.
            bool isArray = typeName.EndsWith("[]");
            if (isArray)
            {
                typeName = typeName.Replace("[]", "");
            }

            Type resultType;
            switch (typeName)
            {
                case "int":
                    resultType = typeof(int);
                    break;
                case "double":
                    resultType = typeof(double);
                    break;
                case "char":
                    resultType = typeof(char);
                    break;
                case "bool":
                    resultType = typeof(bool);
                    break;
                case "string":
                    resultType = typeof(string);
                    break;
                default:
                    resultType = typeof(object); 
                    break;
            }

            // Si era un array, devolvemos el tipo de array correspondiente (ej. int[]).
            return isArray ? resultType.MakeArrayType() : resultType;
        }

        public override object VisitMethodDeclaration(MiniCSharpParser.MethodDeclarationContext context)
        {
             // --- 1. Obtener Tipos para la Firma del Método ---
            var paramInfo = new List<(Type type, string name)>(); // Guardará tipo y nombre
            if (context.formPars() != null)
            {
                var result = Visit(context.formPars());
                if (result is List<(Type, string)> info)
                {
                    paramInfo = info;
                }
            }
            var paramTypes = paramInfo.Select(p => p.type).ToArray(); // Extraer solo los tipos para la firma

            Type returnType = (context.VOID() != null) ? typeof(void) : GetTypeFromTypeName(context.type().GetText());

            // --- 2. Definir el Método ---
            string methodName = context.ID().GetText();
            MethodBuilder methodBuilder = _typeBuilder.DefineMethod(
                methodName,
                MethodAttributes.Public | MethodAttributes.Static,
                returnType,
                paramTypes
            );
            _currentMethod = methodBuilder;

            if (methodName == "Main")
            {
                _mainMethodBuilder = methodBuilder;
            }
            
            _ilGenerator = methodBuilder.GetILGenerator();

            // --- 3. Declarar Parámetros como Variables Locales/Argumentos ---
            OpenScope(); // Abrir ámbito para parámetros y locales.

            // Asignar nombres a los parámetros y declararlos en el ámbito para poder usarlos.
            for (int i = 0; i < paramInfo.Count; i++)
            {
                var (type, name) = paramInfo[i];
                methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, name);
                
                // Los parámetros se tratan como variables locales especiales (argumentos).
                // Los declaramos en nuestro sistema de ámbitos para poder encontrarlos después.
                // NOTA: Para argumentos estáticos, su índice CIL empieza en 0.
                // Si fueran métodos de instancia, empezarían en 1 (el 0 es 'this').
                var localBuilder = _ilGenerator.DeclareLocal(type);
                localBuilder.SetLocalSymInfo(name); // Opcional: para depuración
                _scopedLocalVariables.Last()[name] = localBuilder;
                _ilGenerator.Emit(OpCodes.Ldarg, i); // Cargar argumento
                _ilGenerator.Emit(OpCodes.Stloc, localBuilder); // Guardar en variable local
            }

            // --- 4. Visitar el Cuerpo del Método ---
            Visit(context.block());

            CloseScope(); 

            // --- 5. Finalizar ---
            _ilGenerator.Emit(OpCodes.Ret);
            _ilGenerator = null;
            _currentMethod = null;

            return null;
        }

        public override object VisitFormalParams(MiniCSharpParser.FormalParamsContext context)
        {
            // Ahora devolvemos una lista de tuplas (Tipo, Nombre)
            var paramInfo = new List<(Type, string)>();
            var typeNodes = context.type();
            var idNodes = context.ID();

            for (int i = 0; i < idNodes.Length; i++)
            {
                Type paramType = GetTypeFromTypeName(typeNodes[i].GetText());
                string paramName = idNodes[i].GetText();
                paramInfo.Add((paramType, paramName));
            }
    
            return paramInfo;;
        }

        public override object VisitTypeIdent(MiniCSharpParser.TypeIdentContext context)
        {
            return base.VisitTypeIdent(context);
        }

        public override object VisitDesignatorStatement(MiniCSharpParser.DesignatorStatementContext context)
        {
            return base.VisitDesignatorStatement(context);
        }

        public override object VisitIfStatement(MiniCSharpParser.IfStatementContext context)
        {
            return base.VisitIfStatement(context);
        }

        public override object VisitForStatement(MiniCSharpParser.ForStatementContext context)
        {
            return base.VisitForStatement(context);
        }

        public override object VisitWhileStatement(MiniCSharpParser.WhileStatementContext context)
        {
            return base.VisitWhileStatement(context);
        }

        public override object VisitBreakStatement(MiniCSharpParser.BreakStatementContext context)
        {
            return base.VisitBreakStatement(context);
        }

        public override object VisitReturnStatement(MiniCSharpParser.ReturnStatementContext context)
        {
            // La regla es: "return" [ expr ] ";"
            if (context.expr() != null)
            {
                // Si hay una expresión, la visitamos. Esto dejará su valor
                // en la cima de la pila de evaluación CIL.
                Visit(context.expr());
            }

            // La instrucción 'ret' se emite al final del método en VisitMethodDeclaration,
            // por lo que aquí no es estrictamente necesario emitirla de nuevo si es la última
            // sentencia. Sin embargo, si hubiera código después de un return, necesitaríamos
            // un salto. Por ahora, visitar la expresión es suficiente, ya que el 'ret'
            // principal al final del método usará el valor que dejamos en la pila.
    
            return null;
        }

        public override object VisitReadStatement(MiniCSharpParser.ReadStatementContext context)
        {
            return base.VisitReadStatement(context);
        }

        public override object VisitWriteStatement(MiniCSharpParser.WriteStatementContext context)
        {
            return base.VisitWriteStatement(context);
        }

        public override object VisitBlockStatement(MiniCSharpParser.BlockStatementContext context)
        {
            return base.VisitBlockStatement(context);
        }

        public override object VisitSwitchDispatchStatement(MiniCSharpParser.SwitchDispatchStatementContext context)
        {
            return base.VisitSwitchDispatchStatement(context);
        }

        public override object VisitEmptyStatement(MiniCSharpParser.EmptyStatementContext context)
        {
            return base.VisitEmptyStatement(context);
        }

        public override object VisitForVarDecl(MiniCSharpParser.ForVarDeclContext context)
        {
            return base.VisitForVarDecl(context);
        }

        public override object VisitForInit(MiniCSharpParser.ForInitContext context)
        {
            return base.VisitForInit(context);
        }

        public override object VisitForDeclaredVarPart(MiniCSharpParser.ForDeclaredVarPartContext context)
        {
            return base.VisitForDeclaredVarPart(context);
        }

        public override object VisitForTypeAndMultipleVars(MiniCSharpParser.ForTypeAndMultipleVarsContext context)
        {
            return base.VisitForTypeAndMultipleVars(context);
        }

        public override object VisitForUpdate(MiniCSharpParser.ForUpdateContext context)
        {
            return base.VisitForUpdate(context);
        }

        public override object VisitSwitchStat(MiniCSharpParser.SwitchStatContext context)
        {
            return base.VisitSwitchStat(context);
        }

        public override object VisitSwitchBlockContent(MiniCSharpParser.SwitchBlockContentContext context)
        {
            return base.VisitSwitchBlockContent(context);
        }

        public override object VisitSwitchCaseSection(MiniCSharpParser.SwitchCaseSectionContext context)
        {
            return base.VisitSwitchCaseSection(context);
        }

        public override object VisitCaseLabel(MiniCSharpParser.CaseLabelContext context)
        {
            return base.VisitCaseLabel(context);
        }

        public override object VisitDefaultLabel(MiniCSharpParser.DefaultLabelContext context)
        {
            return base.VisitDefaultLabel(context);
        }

        public override object VisitBlockNode(MiniCSharpParser.BlockNodeContext context)
        {
            // La regla de C# es que cada bloque introduce un nuevo ámbito para variables.
            // Ej: una variable declarada en un 'if' no existe fuera de él.
            OpenScope();

            // Visitamos todas las declaraciones de variables y sentencias dentro del bloque.
            // base.VisitChildren(context) hace esto por nosotros en el orden correcto.
            base.VisitChildren(context);

            CloseScope();
    
            return null;
        }

        public override object VisitActualParams(MiniCSharpParser.ActualParamsContext context)
        {
            return base.VisitActualParams(context);
        }

        public override object VisitConditionNode(MiniCSharpParser.ConditionNodeContext context)
        {
            return base.VisitConditionNode(context);
        }

        public override object VisitConditionTermNode(MiniCSharpParser.ConditionTermNodeContext context)
        {
            return base.VisitConditionTermNode(context);
        }

        public override object VisitConditionFactNode(MiniCSharpParser.ConditionFactNodeContext context)
        {
            return base.VisitConditionFactNode(context);
        }

        public override object VisitExpression(MiniCSharpParser.ExpressionContext context)
        {
            return base.VisitExpression(context);
        }

        public override object VisitTypeCast(MiniCSharpParser.TypeCastContext context)
        {
            return base.VisitTypeCast(context);
        }

        public override object VisitTermNode(MiniCSharpParser.TermNodeContext context)
        {
            // 1. Visita el primer 'factor'.
            // Esto ejecutará uno de nuestros métodos Visit...Factor, que pondrá
            // el valor de ese factor (ej. un número, el valor de una variable) en la cima de la pila CIL.
            Visit(context.factor(0));

            // 2. Itera sobre el resto de los factores y operadores.
            // La regla es: factor (MULOP factor)*
            int operatorCount = context.MULOP().Length;
            for (int i = 0; i < operatorCount; i++)
            {
                // Visita el siguiente 'factor' para poner su valor en la pila.
                // Ahora la pila tiene dos valores: [valor1, valor2]
                Visit(context.factor(i + 1));

                // Obtiene el operador y emite la instrucción CIL correspondiente.
                IToken op = context.MULOP(i).Symbol;

                switch (op.Text)
                {
                    case "*":
                        // Emite la instrucción de multiplicación. Toma los dos valores de la
                        // pila, los multiplica, y pone el resultado de vuelta en la pila.
                        _ilGenerator.Emit(OpCodes.Mul);
                        break;
                    case "/":
                        // Emite la instrucción de división.
                        _ilGenerator.Emit(OpCodes.Div);
                        break;
                    case "%":
                        // Emite la instrucción de módulo/resto.
                        _ilGenerator.Emit(OpCodes.Rem);
                        break;
                }
            }

            // Al final de este método, el resultado del 'término' completo
            // (ej. 5 * 2 / 3) se encuentra en la cima de la pila, listo para ser
            // usado por VisitExpression o guardado en una variable.
            return null;
        }

        public override object VisitDesignatorFactor(MiniCSharpParser.DesignatorFactorContext context)
        {
            return base.VisitDesignatorFactor(context);
        }

        public override object VisitIntLitFactor(MiniCSharpParser.IntLitFactorContext context)
        {
            // Las constantes de tipo "int" se manejan en la gramática. 
            int value = int.Parse(context.INTLIT().GetText());
            // Ldc_I4 carga una constante entera de 4 bytes en la pila.
            _ilGenerator.Emit(OpCodes.Ldc_I4, value);
            return null;
        }

        public override object VisitDoubleLitFactor(MiniCSharpParser.DoubleLitFactorContext context)
        {
            // Las constantes de tipo "double" se manejan en la gramática. 
            // Usamos InvariantCulture para asegurar que el punto '.' sea siempre el separador decimal.
            double value = double.Parse(context.DOUBLELIT().GetText(), CultureInfo.InvariantCulture);
            // Ldc_R8 carga una constante flotante de 8 bytes (un double) en la pila.
            _ilGenerator.Emit(OpCodes.Ldc_R8, value);
            return null;
        }

        public override object VisitCharLitFactor(MiniCSharpParser.CharLitFactorContext context)
        {
            // Las constantes de tipo "char" se manejan en la gramática. 
            string text = context.CHARLIT().GetText();
            // Quitamos las comillas simples de los lados, ej. 'a' -> a
            char value = text.Substring(1, text.Length - 2)[0];
            // Los 'char' se cargan como enteros (su valor numérico Unicode).
            _ilGenerator.Emit(OpCodes.Ldc_I4, (int)value);
            return null;
        }

        public override object VisitStringLitFactor(MiniCSharpParser.StringLitFactorContext context)
        {
            // Las constantes de tipo "string" se manejan en la gramática. 
            string text = context.STRINGLIT().GetText();
            // Quitamos las comillas dobles de los lados, ej. "hola" -> hola
            string value = text.Substring(1, text.Length - 2);
            // Ldstr carga una referencia a un objeto string en la pila.
            _ilGenerator.Emit(OpCodes.Ldstr, value);
            return null;
        }

        public override object VisitTrueLitFactor(MiniCSharpParser.TrueLitFactorContext context)
        {
            // Las constantes de tipo "bool" se manejan en la gramática. 
            // En CIL, 'true' se representa con el entero 1.
            // Ldc_I4_1 es una instrucción optimizada para cargar el valor 1.
            _ilGenerator.Emit(OpCodes.Ldc_I4_1);
            return null;
        }

        public override object VisitFalseLitFactor(MiniCSharpParser.FalseLitFactorContext context)
        {
            // Las constantes de tipo "bool" se manejan en la gramática. 
            // En CIL, 'false' se representa con el entero 0.
            // Ldc_I4_0 es la instrucción optimizada para cargar el valor 0.
            _ilGenerator.Emit(OpCodes.Ldc_I4_0);
            return null;
        }

        public override object VisitNullLitFactor(MiniCSharpParser.NullLitFactorContext context)
        {
            // "null" es un nombre predeclarado en el ambiente estándar. 
            // Ldnull carga una referencia nula en la pila.
            _ilGenerator.Emit(OpCodes.Ldnull);
            return null;
        }

        public override object VisitNewObjectFactor(MiniCSharpParser.NewObjectFactorContext context)
        {
            return base.VisitNewObjectFactor(context);
        }

        public override object VisitParenExpressionFactor(MiniCSharpParser.ParenExpressionFactorContext context)
        {
            return base.VisitParenExpressionFactor(context);
        }

        public override object VisitDesignatorNode(MiniCSharpParser.DesignatorNodeContext context)
        {
            return base.VisitDesignatorNode(context);
        }

        public override object VisitRelationalOp(MiniCSharpParser.RelationalOpContext context)
        {
            return base.VisitRelationalOp(context);
        }

        public override object Visit(IParseTree tree)
        {
            return base.Visit(tree);
        }

        public override object VisitChildren(IRuleNode node)
        {
            return base.VisitChildren(node);
        }

        public override object VisitTerminal(ITerminalNode node)
        {
            return base.VisitTerminal(node);
        }

        public override object VisitErrorNode(IErrorNode node)
        {
            return base.VisitErrorNode(node);
        }

        public override string ToString()
        {
            return base.ToString();
        }
    }
}