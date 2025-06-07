using Antlr4.Runtime.Tree;
using Antlr4.Runtime;
using parser.generated;
using System.Collections.Generic;

namespace MiniCSharp.Grammar.Checker
{
    public class MiniCsharpChecker : MiniCSharpParserBaseVisitor<object>
    {
        private TablaSimbolos symbolTable;
        public List<string> Errors { get; private set; }
        
        private TablaSimbolos.MethodIdent currentProcessingMethod = null;
        private int currentMethodBodyScopeLevel = -1;
        public MiniCsharpChecker()
        {
            this.symbolTable = new TablaSimbolos();
            this.Errors = new List<string>();
        }

        public override object VisitProg(MiniCSharpParser.ProgContext context)
        {
            symbolTable.OpenScope(); 

            foreach (var usingDirContext in context.usingDirective())
            {
                Visit(usingDirContext); 
            }

            IToken classNameToken = context.ID().Symbol; 
            string mainClassName = classNameToken.Text; // Para depuración o mensajes

            if (context.children != null)
            {
                foreach (IParseTree child in context.children)
                {
                    if (child is MiniCSharpParser.VarDeclarationContext ||
                        child is MiniCSharpParser.ClassDeclarationContext ||
                        child is MiniCSharpParser.MethodDeclarationContext)
                    {
                        Visit(child); 
                    }
                }
            }
    
            
            Console.WriteLine($"--- Symbol Table for Program/Class: {mainClassName} (Level: {symbolTable.NivelActual}) ---");
            symbolTable.Imprimir();

            symbolTable.CloseScope(); 
            return null; 
        }

        public override object VisitUsingStat(MiniCSharpParser.UsingStatContext context)
        {
            Visit(context.qualifiedIdentifier());
            
            var qualifiedIdentCtx = context.qualifiedIdentifier() as MiniCSharpParser.QualifiedIdentContext;
            if (qualifiedIdentCtx != null)
            {
                
                string namespaceIdentifier = qualifiedIdentCtx.GetText();
                Console.WriteLine($"Checker DBG: VisitUsingStat - procesando 'using {namespaceIdentifier};'"); // Para depuración
            }
            
            return null; 
        }

        public override object VisitQualifiedIdent(MiniCSharpParser.QualifiedIdentContext context)
        {
            string fullIdentifier = context.GetText();
            Console.WriteLine($"Checker DBG: VisitQualifiedIdent - identificador: {fullIdentifier}"); // Para depuración
            return null;
        }

        public override object VisitVarDeclaration(MiniCSharpParser.VarDeclarationContext context)
        {
            MiniCSharpParser.TypeContext typeRuleContext = context.type();
            object typeResult = Visit(typeRuleContext);

            if (!(typeResult is int resolvedTypeCode) || resolvedTypeCode == TablaSimbolos.UnknownType)
            {
                return null;
            }

            ITerminalNode typeNameTerminalNode = typeRuleContext.GetToken(MiniCSharpLexer.ID, 0);
            // Es buena práctica comprobar si el nodo terminal (y por ende el token) no es null
            // aunque para un ID en la regla 'type' debería existir.
            if (typeNameTerminalNode != null && resolvedTypeCode == TablaSimbolos.VoidType && typeNameTerminalNode.Symbol.Text == "void")
            {
                Errors.Add($"Error: No se puede declarar una variable de tipo 'void' ('{context.GetText()}' en línea {context.Start.Line}).");
                return null;
            }

            ITerminalNode lbrackNode = typeRuleContext.GetToken(MiniCSharpLexer.LBRACK, 0);
            bool isArray = lbrackNode != null;

            // GetTokens devuelve IList<ITerminalNode> o ITerminalNode[]
            // Lo trataremos como IList<ITerminalNode> por consistencia con la API de ANTLR.
            IList<ITerminalNode> idTerminalNodes = context.GetTokens(MiniCSharpLexer.ID);
            if (idTerminalNodes != null)
            {
                foreach (ITerminalNode idNode in idTerminalNodes)
                {
                    IToken idToken = idNode.Symbol; // Obtener el IToken desde ITerminalNode
                    string varName = idToken.Text;

                    if (symbolTable.Buscar(varName) != null && symbolTable.BuscarNivelActual(varName) != null)
                    {
                        Errors.Add($"Error: La variable '{varName}' ya está definida en este ámbito (línea {idToken.Line}).");
                    }
                    else
                    {
                        symbolTable.InsertarVar(idToken, resolvedTypeCode, isArray, context);
                    }
                }
            }
            return null;
        }

        public override object VisitClassDeclaration(MiniCSharpParser.ClassDeclarationContext context)
        {
            IToken classToken = context.ID().Symbol; 
            string className = classToken.Text;
            
            if (symbolTable.BuscarNivelActual(className) != null)
            {
                Errors.Add($"Error: El identificador '{className}' ya está definido en este ámbito (línea {classToken.Line}). No se puede declarar como clase.");
                return null;
            }
            
            TablaSimbolos.ClassIdent classIdent = symbolTable.InsertarClass(classToken, context);
            if (classIdent == null)
            {
                Errors.Add($"Error: No se pudo registrar la clase '{className}' (línea {classToken.Line}).");
                return null;
            }
            Console.WriteLine($"Checker DBG: Declarada clase '{className}' en nivel {symbolTable.NivelActual}");

            
            TablaSimbolos outerSymbolTable = this.symbolTable;
            this.symbolTable = classIdent.Members; 
            
            this.symbolTable.OpenScope(); 

            
            foreach (var varDeclContext in context.varDecl()) //
            {
                Visit(varDeclContext); 
            }

            
            Console.WriteLine($"--- Members Symbol Table for Class: {className} ---");
            this.symbolTable.Imprimir();

            
            this.symbolTable.CloseScope();

            
            this.symbolTable = outerSymbolTable;

            return null;
        }

        public override object VisitMethodDeclaration(MiniCSharpParser.MethodDeclarationContext context)
        {
            int returnTypeCode;
            IToken methodNameToken = context.ID().Symbol; 

            if (context.VOID() != null) 
            {
                returnTypeCode = TablaSimbolos.VoidType;
            }
            else 
            {
                MiniCSharpParser.TypeContext returnTypeContext = context.type(); 
                if (returnTypeContext == null) 
                {
                    Errors.Add($"Error: Falta el tipo de retorno para el método '{methodNameToken.Text}' (línea {methodNameToken.Line}).");
                    return null;
                }

                object typeResult = Visit(returnTypeContext); 
                if (!(typeResult is int resolvedTypeCode) || resolvedTypeCode == TablaSimbolos.UnknownType)
                {
                    Errors.Add($"Error: Tipo de retorno inválido o desconocido para el método '{methodNameToken.Text}' (línea {methodNameToken.Line}).");
                    return null;
                }
                returnTypeCode = resolvedTypeCode;
            }

            if (symbolTable.BuscarNivelActual(methodNameToken.Text) != null)
            {
                Errors.Add($"Error: El identificador '{methodNameToken.Text}' ya está definido en este ámbito (línea {methodNameToken.Line}).");
                return null;
            }

            TablaSimbolos.MethodIdent methodIdent = symbolTable.InsertarMethod(methodNameToken, returnTypeCode, context);
            if (methodIdent == null) 
            {
                Errors.Add($"Error: No se pudo registrar el método '{methodNameToken.Text}' (línea {methodNameToken.Line}).");
                return null;
            }
            
            this.currentProcessingMethod = methodIdent;
            
            symbolTable.OpenScope();
            this.currentMethodBodyScopeLevel = symbolTable.NivelActual;

            if (context.formPars() != null)
            {
                Visit(context.formPars()); 
            }

            this.currentProcessingMethod = null;

            Visit(context.block());
            symbolTable.CloseScope();

            return null;
        }

        public override object VisitFormalParams(MiniCSharpParser.FormalParamsContext context)
        {
            MiniCSharpParser.TypeContext[] typeNodes = context.type();     
            ITerminalNode[] idTerminalNodes = context.ID();                 

            
            if (idTerminalNodes != null && typeNodes != null && idTerminalNodes.Length == typeNodes.Length)
            {
                for (int i = 0; i < idTerminalNodes.Length; i++) 
                {
                    MiniCSharpParser.TypeContext typeCtx = typeNodes[i]; 
                    IToken paramToken = idTerminalNodes[i].Symbol; 
                    string paramName = paramToken.Text;

                    object typeResult = Visit(typeCtx); 
                    if (!(typeResult is int paramTypeCode) || paramTypeCode == TablaSimbolos.UnknownType)
                    {
                        Errors.Add($"Error: Tipo inválido o desconocido para el parámetro '{paramName}' del método '{this.currentProcessingMethod?.GetName() ?? "actual"}' (línea {paramToken.Line}).");
                        continue; 
                    }
                    if (paramTypeCode == TablaSimbolos.VoidType)
                    {
                         Errors.Add($"Error: Un parámetro de método ('{paramName}') no puede ser de tipo 'void' (línea {paramToken.Line}).");
                         continue;
                    }

                    bool isArray = typeCtx.GetToken(MiniCSharpLexer.LBRACK, 0) != null; 

                    if (this.currentProcessingMethod != null && this.currentProcessingMethod.Params.Any(p => p.GetName() == paramName))
                    {
                        Errors.Add($"Error: Parámetro duplicado '{paramName}' en el método '{this.currentProcessingMethod.GetName()}' (línea {paramToken.Line}).");
                        continue;
                    }
                    
                    if (this.currentProcessingMethod != null) 
                    {
                        symbolTable.InsertarParam(this.currentProcessingMethod, paramToken, paramTypeCode, isArray, typeCtx, this.currentMethodBodyScopeLevel);
                        Console.WriteLine($"Checker DBG: Declarado Parámetro '{paramName}' de tipo {TablaSimbolos.TypeToString(paramTypeCode)}{(isArray ? "[]" : "")} para método '{this.currentProcessingMethod.GetName()}' en nivel {this.currentMethodBodyScopeLevel}");
                    }
                }
            } else if (idTerminalNodes == null || typeNodes == null || idTerminalNodes.Length != typeNodes.Length)
            {
                Errors.Add($"Error: Discrepancia en el número de tipos e identificadores para los parámetros del método '{this.currentProcessingMethod?.GetName() ?? "actual"}' (línea {context.Start.Line}).");
            }
            return null;
        }

        public override object VisitTypeIdent(MiniCSharpParser.TypeIdentContext context)
        {
            ITerminalNode typeIdTerminalNode = context.GetToken(MiniCSharpLexer.ID, 0);
            if (typeIdTerminalNode == null)
            {
                Errors.Add($"Error: Falta el nombre del tipo en la declaración (línea {context.Start.Line}).");
                return TablaSimbolos.UnknownType;
            }

            IToken typeIdToken = typeIdTerminalNode.Symbol; // Obtener el IToken
            string typeName = typeIdToken.Text;
            int typeCode = TablaSimbolos.UnknownType;

            switch (typeName)
            {
                case "int": typeCode = TablaSimbolos.IntType; break;
                case "double": typeCode = TablaSimbolos.DoubleType; break;
                case "char": typeCode = TablaSimbolos.CharType; break;
                case "bool": typeCode = TablaSimbolos.BoolType; break;
                case "string": typeCode = TablaSimbolos.StringType; break;
                case "void": typeCode = TablaSimbolos.VoidType; break;
                default:
                    TablaSimbolos.Ident classIdent = symbolTable.Buscar(typeName);
                    if (classIdent != null && classIdent is TablaSimbolos.ClassIdent)
                    {
                        typeCode = TablaSimbolos.ClassType;
                    }
                    else
                    {
                        Errors.Add($"Error: Tipo desconocido o no definido '{typeName}' (línea {typeIdToken.Line}).");
                    }
                    break;
            }
            return typeCode;
        }

        public override object VisitDesignatorStatement(MiniCSharpParser.DesignatorStatementContext context)
        {
            object designatorResult = Visit(context.designator());

            if (!(designatorResult is TablaSimbolos.Ident ident))
            {
                // VisitDesignatorNode ya debería haber reportado un error si no pudo resolver el designador.
                // Si designatorResult es null o no es un Ident, no podemos continuar.
                // Un mensaje de error adicional aquí podría ser redundante si VisitDesignatorNode es completo.
                // Errors.Add($"Error: El designador '{context.designator().GetText()}' es inválido o no se pudo resolver (línea {context.designator().Start.Line}).");
                return null;
            }

            
            if (context.ASSIGN() != null) 
            {
                if (!(ident is TablaSimbolos.VarIdent varIdent)) 
                {
                    Errors.Add($"Error: El lado izquierdo de la asignación ('{ident.GetName()}') no es una variable o campo asignable (línea {ident.Token.Line}).");
                    return null;
                }
                // Aquí podrías añadir una verificación para constantes si VarIdent tuviera una bandera 'IsConstant'.

                object exprTypeResult = Visit(context.expr());
                if (!(exprTypeResult is int exprTypeCode) || exprTypeCode == TablaSimbolos.UnknownType)
                {
                    Errors.Add($"Error: La expresión en el lado derecho de la asignación para '{ident.GetName()}' tiene un tipo inválido o desconocido (línea {context.expr().Start.Line}).");
                    return null;
                }

                bool typesCompatible = ident.Type == exprTypeCode;
                if (!typesCompatible)
                {
                    if (exprTypeCode == TablaSimbolos.NullType &&
                        (ident.Type == TablaSimbolos.ClassType ||
                         ident.Type == TablaSimbolos.StringType ||
                         (varIdent.IsArray )))
                    {
                        typesCompatible = true;
                    }
                    // Aquí se podrían añadir más reglas de compatibilidad (ej. int a double).
                }

                if (!typesCompatible)
                {
                    Errors.Add($"Error: Tipos incompatibles en la asignación a '{ident.GetName()}'. Se esperaba '{TablaSimbolos.TypeToString(ident.Type)}' pero se encontró '{TablaSimbolos.TypeToString(exprTypeCode)}' (línea {ident.Token.Line}).");
                }
                Console.WriteLine($"Checker DBG: Asignación a '{ident.GetName()}' (tipo {TablaSimbolos.TypeToString(ident.Type)}) con expresión tipo {TablaSimbolos.TypeToString(exprTypeCode)}");
            }
            else if (context.LPAREN() != null) 
            {
                TablaSimbolos.MethodIdent methodIdent = null;

                if (ident is TablaSimbolos.MethodIdent castedMethodIdent)
                {
                    methodIdent = castedMethodIdent;
                }
                else
                {
                    Errors.Add($"Error: '{ident.GetName()}' no es un método y no puede ser llamado (línea {ident.Token.Line}).");
                    return null; 
                }

                List<int> actualParamTypes = new List<int>();
                if (context.actPars() != null)
                {
                    object actParsResult = Visit(context.actPars()); 
                    if (actParsResult is List<int> types)
                    {
                        actualParamTypes = types;
                    }
                    // else: VisitActualParams ya debería haber reportado errores si los hubo.
                }

                
                if (methodIdent.Params.Count != actualParamTypes.Count)
                {
                    Errors.Add($"Error: Número incorrecto de argumentos para el método '{methodIdent.GetName()}'. Se esperaban {methodIdent.Params.Count} pero se encontraron {actualParamTypes.Count} (línea {ident.Token.Line}).");
                }
                else
                {
                    for (int i = 0; i < methodIdent.Params.Count; i++)
                    {
                        if (methodIdent.Params[i].Type != actualParamTypes[i] &&
                            actualParamTypes[i] != TablaSimbolos.UnknownType) 
                        {
                            bool paramCompatible = false;
                            if (actualParamTypes[i] == TablaSimbolos.NullType &&
                               (methodIdent.Params[i].Type == TablaSimbolos.ClassType ||
                                methodIdent.Params[i].Type == TablaSimbolos.StringType ||
                                methodIdent.Params[i].IsArray)) 
                            {
                                paramCompatible = true;
                            }
                            // Aquí se podrían añadir más reglas de compatibilidad.

                            if (!paramCompatible)
                            {
                                Errors.Add($"Error: Tipo incorrecto para el parámetro {i + 1} ('{methodIdent.Params[i].GetName()}') del método '{methodIdent.GetName()}'. Se esperaba '{TablaSimbolos.TypeToString(methodIdent.Params[i].Type)}' pero se encontró '{TablaSimbolos.TypeToString(actualParamTypes[i])}' (línea {ident.Token.Line}).");
                            }
                        }
                    }
                }
                Console.WriteLine($"Checker DBG: Llamada al método '{methodIdent.GetName()}'");
            }
            else if (context.INCREMENT() != null || context.DECREMENT() != null) 
            {
                if (!(ident is TablaSimbolos.VarIdent)) 
                {
                    Errors.Add($"Error: El operando de '++' o '--' debe ser una variable o campo asignable (línea {ident.Token.Line}).");
                }
                else if (ident.Type != TablaSimbolos.IntType && ident.Type != TablaSimbolos.DoubleType)
                {
                    Errors.Add($"Error: El operando de '++' o '--' ('{ident.GetName()}') debe ser de tipo numérico (int, double), pero es '{TablaSimbolos.TypeToString(ident.Type)}' (línea {ident.Token.Line}).");
                }
                Console.WriteLine($"Checker DBG: Operación '{(context.INCREMENT() != null ? "++" : "--")}' sobre '{ident.GetName()}'");
            }

            return null;
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
            return base.VisitReturnStatement(context);
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

        public override object VisitBlockNode(MiniCSharpParser.BlockNodeContext context) // Hay que mejorarlo-------------
        {
            // Vamos a hacerlo simple: si un bloque puede declarar variables, debe tener su propio ámbito.
            bool isMethodBodyBlock = context.Parent is MiniCSharpParser.MethodDeclarationContext;
            if (!isMethodBodyBlock) // Solo abrir nuevo ámbito si no es el bloque principal del método
            {                     // (que ya tiene un ámbito abierto por VisitMethodDeclaration)
                symbolTable.OpenScope();
            }

            if (context.children != null) {
                foreach (var child in context.children) {
                    // La regla 'block' es: LBRACE (varDecl | statement)* RBRACE
                    // Así que los hijos relevantes son varDecl o statement
                    if (child is MiniCSharpParser.VarDeclarationContext ||
                        child is MiniCSharpParser.StatementContext) {
                        Visit(child);
                    }
                }
            }

            if (!isMethodBodyBlock)
            {
                symbolTable.CloseScope();
            }
            return null;
        }

        public override object VisitActualParams(MiniCSharpParser.ActualParamsContext context)
        {
            List<int> paramTypes = new List<int>();
            if (context.expr() != null) 
            {
                foreach (var exprContext in context.expr())
                {
                    object exprTypeResult = Visit(exprContext); 
                    if (exprTypeResult is int exprTypeCode)
                    {
                        paramTypes.Add(exprTypeCode);
                    }
                    else
                    {
                        Errors.Add($"Error: No se pudo determinar el tipo de un argumento en la llamada a método (línea {exprContext.Start.Line}).");
                        paramTypes.Add(TablaSimbolos.UnknownType); 
                    }
                }
            }
            return paramTypes;
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

        public override object VisitExpression(MiniCSharpParser.ExpressionContext context) //--------Matey
        {
            // 1. Obtener el tipo del primer término.
            object initialTypeResult = Visit(context.term(0));

            if (!(initialTypeResult is int currentTypeCode) || currentTypeCode == TablaSimbolos.UnknownType)
            {
                // VisitTermNode ya debería haber reportado el error específico.
                Errors.Add($"Error: La expresión contiene un término con tipo inválido o desconocido (línea {context.term(0).Start.Line}).");
                return TablaSimbolos.UnknownType;
            }

            // 2. Manejar el operador unario opcional (+ o -) al inicio de la expresión.
            if (context.ADDOP().Length > context.term().Length - 1) 
            {
                IToken unaryOp = context.ADDOP(0).Symbol;
                if (unaryOp.Text == "-")
                {
                    // El operador unario '-' solo se puede aplicar a tipos numéricos.
                    if (currentTypeCode != TablaSimbolos.IntType && currentTypeCode != TablaSimbolos.DoubleType)
                    {
                        Errors.Add($"Error: El operador unario '-' no se puede aplicar a un operando de tipo '{TablaSimbolos.TypeToString(currentTypeCode)}' (línea {unaryOp.Line}).");
                        return TablaSimbolos.UnknownType;
                    }
                }
            }

            // 3. Procesar el resto de la expresión: (ADDOP term)*
            int binaryOpCount = context.term().Length - 1;
            for (int i = 0; i < binaryOpCount; i++)
            {
                // Determinar el operador (+ o -)
                int opIndex = (context.ADDOP().Length > binaryOpCount) ? i + 1 : i;
                IToken op = context.ADDOP(opIndex).Symbol;

                // Obtener el tipo del siguiente término
                object nextTermTypeResult = Visit(context.term(i + 1));
                if (!(nextTermTypeResult is int nextTermTypeCode) || nextTermTypeCode == TablaSimbolos.UnknownType)
                {
                    Errors.Add($"Error: La expresión contiene un término con tipo inválido o desconocido (línea {context.term(i + 1).Start.Line}).");
                    return TablaSimbolos.UnknownType;
                }

                // 4. Verificar la compatibilidad de tipos para la operación
                if (op.Text == "+")
                {
                    // Reglas para la suma:
                    // int + int = int
                    // double + double = double
                    // string + string = string (concatenación)
                    // (podrían añadirse más, como int + double = double)
                    if (currentTypeCode == TablaSimbolos.IntType && nextTermTypeCode == TablaSimbolos.IntType)
                        currentTypeCode = TablaSimbolos.IntType;
                    else if (currentTypeCode == TablaSimbolos.DoubleType && nextTermTypeCode == TablaSimbolos.DoubleType)
                        currentTypeCode = TablaSimbolos.DoubleType;
                    else if (currentTypeCode == TablaSimbolos.StringType && nextTermTypeCode == TablaSimbolos.StringType)
                        currentTypeCode = TablaSimbolos.StringType;
                    else
                    {
                        Errors.Add($"Error: El operador '+' no se puede aplicar a operandos de tipo '{TablaSimbolos.TypeToString(currentTypeCode)}' y '{TablaSimbolos.TypeToString(nextTermTypeCode)}' (línea {op.Line}).");
                        return TablaSimbolos.UnknownType;
                    }
                }
                else if (op.Text == "-")
                {
                    // Reglas para la resta: solo numéricos
                    if (currentTypeCode == TablaSimbolos.IntType && nextTermTypeCode == TablaSimbolos.IntType)
                        currentTypeCode = TablaSimbolos.IntType;
                    else if (currentTypeCode == TablaSimbolos.DoubleType && nextTermTypeCode == TablaSimbolos.DoubleType)
                        currentTypeCode = TablaSimbolos.DoubleType;
                    else
                    {
                        Errors.Add($"Error: El operador '-' no se puede aplicar a operandos de tipo '{TablaSimbolos.TypeToString(currentTypeCode)}' y '{TablaSimbolos.TypeToString(nextTermTypeCode)}' (línea {op.Line}).");
                        return TablaSimbolos.UnknownType;
                    }
                }
            }

            // El tipo resultante de la expresión completa es el valor final de currentTypeCode.
            return currentTypeCode;
        }

        public override object VisitTypeCast(MiniCSharpParser.TypeCastContext context)
        {
            return base.VisitTypeCast(context);
        }

        public override object VisitTermNode(MiniCSharpParser.TermNodeContext context) //--------Matey
        {
            // 1. Obtener el tipo del primer factor.
            object initialTypeResult = Visit(context.factor(0));

            if (!(initialTypeResult is int currentTypeCode) || currentTypeCode == TablaSimbolos.UnknownType)
            {
                Errors.Add($"Error: La expresión contiene un factor con tipo inválido o desconocido (línea {context.factor(0).Start.Line}).");
                return TablaSimbolos.UnknownType;
            }

            // 2. Procesar el resto del término: (mulop factor)*
            int operatorCount = context.factor().Length - 1;
            for (int i = 0; i < operatorCount; i++)
            {
                object nextFactorTypeResult = Visit(context.factor(i + 1));
                if (!(nextFactorTypeResult is int nextFactorTypeCode) || nextFactorTypeCode == TablaSimbolos.UnknownType)
                {
                    Errors.Add($"Error: La expresión contiene un factor con tipo inválido o desconocido (línea {context.factor(i + 1).Start.Line}).");
                    return TablaSimbolos.UnknownType;
                }
                
                IToken op = context.MULOP(i).Symbol;

                // 3. Verificar la compatibilidad de tipos para la operación
                switch (op.Text)
                {
                    case "*":
                    case "/":
                        // Reglas para '*' y '/': solo numéricos
                        if ((currentTypeCode == TablaSimbolos.IntType || currentTypeCode == TablaSimbolos.DoubleType) &&
                            (nextFactorTypeCode == TablaSimbolos.IntType || nextFactorTypeCode == TablaSimbolos.DoubleType))
                        {
                            // Promoción de tipo: si alguno es double, el resultado es double.
                            if (currentTypeCode == TablaSimbolos.DoubleType || nextFactorTypeCode == TablaSimbolos.DoubleType)
                            {
                                currentTypeCode = TablaSimbolos.DoubleType;
                            }
                            else
                            {
                                currentTypeCode = TablaSimbolos.IntType;
                            }
                        }
                        else
                        {
                            Errors.Add($"Error: El operador '{op.Text}' no se puede aplicar a operandos de tipo '{TablaSimbolos.TypeToString(currentTypeCode)}' y '{TablaSimbolos.TypeToString(nextFactorTypeCode)}' (línea {op.Line}).");
                            return TablaSimbolos.UnknownType;
                        }
                        break;

                    case "%":
                        // Regla para '%': solo enteros
                        if (currentTypeCode == TablaSimbolos.IntType && nextFactorTypeCode == TablaSimbolos.IntType)
                        {
                            currentTypeCode = TablaSimbolos.IntType;
                        }
                        else
                        {
                            Errors.Add($"Error: El operador '%' solo se puede aplicar a operandos de tipo 'int', pero se usaron '{TablaSimbolos.TypeToString(currentTypeCode)}' y '{TablaSimbolos.TypeToString(nextFactorTypeCode)}' (línea {op.Line}).");
                            return TablaSimbolos.UnknownType;
                        }
                        break;
                }
            }

            return currentTypeCode;
        }

        public override object VisitDesignatorFactor(MiniCSharpParser.DesignatorFactorContext context) //---------Matey
        {
            // Primero, visitamos el designator para saber a qué identificador se refiere.
            object designatorResult = Visit(context.designator());

            if (!(designatorResult is TablaSimbolos.Ident ident))
            {
                // Si el designador no se pudo resolver, el error ya fue reportado por VisitDesignatorNode.
                return TablaSimbolos.UnknownType;
            }

            // Ahora, diferenciamos los dos casos de la regla 'factor'.
            // Caso 1: Es una llamada a método (designator seguido de '(').
            if (context.LPAREN() != null)
            {
                // Si hay un '(', el designador DEBE ser un método.
                if (!(ident is TablaSimbolos.MethodIdent methodIdent))
                {
                    Errors.Add($"Error: '{ident.GetName()}' no es un método y no puede ser usado en una expresión de llamada (línea {ident.Token.Line}).");
                    return TablaSimbolos.UnknownType;
                }

                // Un método que devuelve 'void' no puede ser usado como parte de una expresión.
                if (methodIdent.ReturnType == TablaSimbolos.VoidType)
                {
                    Errors.Add($"Error: El método '{methodIdent.GetName()}' es de tipo 'void' y su llamada no puede ser usada como un valor en una expresión (línea {ident.Token.Line}).");
                    return TablaSimbolos.UnknownType;
                }

                // Verificar los parámetros de la llamada. Reutilizamos la lógica que ya tenemos en VisitDesignatorStatement.
                List<int> actualParamTypes = new List<int>();
                if (context.actPars() != null)
                {
                    object actParsResult = Visit(context.actPars());
                    if (actParsResult is List<int> types)
                    {
                        actualParamTypes = types;
                    }
                }
                
                // Verificar que el número y tipo de los parámetros coincidan.
                if (methodIdent.Params.Count != actualParamTypes.Count)
                {
                    Errors.Add($"Error: Número incorrecto de argumentos para el método '{methodIdent.GetName()}'. Se esperaban {methodIdent.Params.Count} pero se encontraron {actualParamTypes.Count} (línea {ident.Token.Line}).");
                    return TablaSimbolos.UnknownType;
                }
                else
                {
                    // Verificar tipos (simplificado, puedes añadir más reglas de compatibilidad)
                    for (int i = 0; i < methodIdent.Params.Count; i++)
                    {
                        if (methodIdent.Params[i].Type != actualParamTypes[i] && actualParamTypes[i] != TablaSimbolos.UnknownType)
                        {
                             Errors.Add($"Error: Tipo incorrecto para el parámetro {i + 1} del método '{methodIdent.GetName()}'. Se esperaba '{TablaSimbolos.TypeToString(methodIdent.Params[i].Type)}' pero se encontró '{TablaSimbolos.TypeToString(actualParamTypes[i])}' (línea {ident.Token.Line}).");
                             // Podríamos devolver UnknownType aquí también si un parámetro es incorrecto
                        }
                    }
                }

                // Si todo está bien, el tipo de este factor es el tipo de retorno del método.
                return methodIdent.ReturnType;
            }
            // Caso 2: Es una variable o campo (solo 'designator').
            else
            {
                // Si no hay '(', el designador DEBE ser una variable/campo. No puede ser un método o una clase.
                if (!(ident is TablaSimbolos.VarIdent)) 
                {
                    Errors.Add($"Error: El identificador '{ident.GetName()}' no es una variable/campo y no puede ser usado como un valor en una expresión (línea {ident.Token.Line}).");
                    return TablaSimbolos.UnknownType;
                }
                
                // El tipo de este factor es el tipo de la variable/campo.
                return ident.Type;
            }
        }

        public override object VisitIntLitFactor(MiniCSharpParser.IntLitFactorContext context) // ----Matey
        {
            // Un IntLitFactor siempre representa un valor de tipo 'int'.
            // Devolvemos el código de tipo correspondiente desde nuestra TablaSimbolos.
            return TablaSimbolos.IntType;
        }

        public override object VisitDoubleLitFactor(MiniCSharpParser.DoubleLitFactorContext context) // ----Matey
        {
            // Un DoubleLitFactor siempre representa un valor de tipo 'double'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.DoubleType;
        }

        public override object VisitCharLitFactor(MiniCSharpParser.CharLitFactorContext context)// ----Matey
        {
            // Un CharLitFactor siempre representa un valor de tipo 'char'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.CharType;
        }

        public override object VisitStringLitFactor(MiniCSharpParser.StringLitFactorContext context)// ----Matey
        {
            // Un stringLitFactor siempre representa un valor de tipo 'string'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.StringType;
        }

        public override object VisitTrueLitFactor(MiniCSharpParser.TrueLitFactorContext context)// ----Matey
        {
            // Un TrueLitFactor siempre representa un valor de tipo 'Bool'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.BoolType;
        }

        public override object VisitFalseLitFactor(MiniCSharpParser.FalseLitFactorContext context)// ----Matey
        {
            // Un FalseLitFactor siempre representa un valor de tipo 'Bool'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.BoolType;
        }

        public override object VisitNullLitFactor(MiniCSharpParser.NullLitFactorContext context)// ----Matey
        {
            // Un NullLitFactor siempre representa un valor de tipo 'Null'.
            // Devolvemos el código de tipo correspondiente.
            return TablaSimbolos.NullType;
        }

        public override object VisitNewObjectFactor(MiniCSharpParser.NewObjectFactorContext context)
        {
            // La regla es: NEW ID ( (LBRACK RBRACK) )? # NewObjectFactor
            // Por ahora, solo manejaremos la creación de clases (NEW ID)
            string className = context.ID().GetText();
            TablaSimbolos.Ident classIdent = symbolTable.Buscar(className);

            if (classIdent != null && classIdent is TablaSimbolos.ClassIdent)
            {
                // La expresión 'new MiClase()' tiene como tipo 'MiClase'.
                // Devolvemos el tipo genérico ClassType por ahora.
                // En un sistema más avanzado, podríamos devolver una referencia al ClassIdent mismo.
                return TablaSimbolos.ClassType;
            }
    
            Errors.Add($"Error: No se puede crear una instancia del tipo '{className}' porque no es una clase definida (línea {context.ID().Symbol.Line}).");
            return TablaSimbolos.UnknownType;
        }

        public override object VisitParenExpressionFactor(MiniCSharpParser.ParenExpressionFactorContext context) //-------Matey
        {
            return base.VisitParenExpressionFactor(context);
        }

        public override object VisitDesignatorNode(MiniCSharpParser.DesignatorNodeContext context)
        {
            IToken firstIdToken = context.ID(0)?.Symbol; 
            // O context.GetToken(MiniCSharpLexer.ID, 0)?.Symbol;

            if (firstIdToken == null) {
                Errors.Add($"Error: Designador inválido o vacío (línea {context.Start.Line}).");
                return null;
            }

            TablaSimbolos.Ident ident = symbolTable.Buscar(firstIdToken.Text);

            if (ident == null) {
                Errors.Add($"Error: El identificador '{firstIdToken.Text}' no ha sido declarado (línea {firstIdToken.Line}).");
                return null; 
            }

            // TODO: Si hay context.DOT() o context.LBRACK(), procesar el resto del designador.
            // Por ejemplo, si hay DOT ID:
            // if (context.DOT().Count > 0 && ident is TablaSimbolos.ClassIdent classIdent) {
            //     TablaSimbolos outerTable = this.symbolTable;
            //     this.symbolTable = classIdent.Members;
            //     // ... buscar el siguiente ID en this.symbolTable (tabla de miembros) ...
            //     // Esto se vuelve recursivo o iterativo.
            //     this.symbolTable = outerTable;
            // }
            // Similar para LBRACK expr RBRACK (acceso a array).

            return ident; // Devuelve el 'Ident' del primer ID por ahora.
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