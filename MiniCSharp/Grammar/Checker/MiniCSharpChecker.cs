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
        private int loopDepth = 0;
        private int switchDepth = 0;
        private TablaSimbolos.MethodIdent currentProcessingMethod = null;
        private int currentMethodBodyScopeLevel = -1;
        private int currentSwitchExprType = TablaSimbolos.UnknownType;
        public MiniCsharpChecker()
        {
            this.symbolTable = new TablaSimbolos();
            this.Errors = new List<string>();
        }
        
    public override object VisitProg(MiniCSharpParser.ProgContext context)
        {
            symbolTable.OpenScope(); // Abre el scope global del programa

            // ---------------------------------------------------------------------------------------------------------
            // Inserción de funciones predefinidas (Built-in functions)
            // Se añaden a la tabla de símbolos como MethodIdent, y sus parámetros se añaden directamente al MethodIdent.
            // NO se usa symbolTable.InsertarParam aquí para los parámetros de built-in,
            // porque no deben aparecer en el scope global de la tabla.
            // ---------------------------------------------------------------------------------------------------------

            // 1. "Write" method: void Write(int value)
            IToken writeToken = new CommonToken(MiniCSharpLexer.WRITE, "Write");
            // InsertarMethod añade el método a la tabla de símbolos del scope actual (nivel 0 para global).
            TablaSimbolos.MethodIdent writeMethod = symbolTable.InsertarMethod(writeToken, TablaSimbolos.VoidType, null);
            // Añadir el parámetro directamente a la definición del método.
            IToken writeParamToken = new CommonToken(MiniCSharpLexer.ID, "value");
            writeMethod.AddParam(new TablaSimbolos.ParamIdent(
                writeParamToken,
                TablaSimbolos.IntType,
                false, // isArray: false
                null,  // declCtx: null for built-in params
                0      // methodBodyNivel: 0 o el nivel que uses para parámetros de built-in.
                       // Este nivel en ParamIdent es más para el contexto interno del método,
                       // no para si está en la tabla global.
            ));

            // 2. "len" method: int len(int[] a)
            IToken lenTok = new CommonToken(MiniCSharpLexer.ID, "len");
            TablaSimbolos.MethodIdent lenMeth = symbolTable.InsertarMethod(lenTok, TablaSimbolos.IntType, null);
            IToken lenParamTok = new CommonToken(MiniCSharpLexer.ID, "a");
            lenMeth.AddParam(new TablaSimbolos.ParamIdent(
                lenParamTok,
                TablaSimbolos.IntType,
                true,  // isArray: true
                null,
                0
            ));

            // 3. "ord" method: int ord(char c)
            IToken ordTok = new CommonToken(MiniCSharpLexer.ID, "ord");
            TablaSimbolos.MethodIdent ordMeth = symbolTable.InsertarMethod(ordTok, TablaSimbolos.IntType, null);
            IToken ordParamTok = new CommonToken(MiniCSharpLexer.ID, "c");
            ordMeth.AddParam(new TablaSimbolos.ParamIdent(
                ordParamTok,
                TablaSimbolos.CharType,
                false, // isArray: false
                null,
                0
            ));

            // 4. "chr" method: char chr(int i)
            IToken chrTok = new CommonToken(MiniCSharpLexer.ID, "chr");
            TablaSimbolos.MethodIdent chrMeth = symbolTable.InsertarMethod(chrTok, TablaSimbolos.CharType, null);
            IToken chrParamTok = new CommonToken(MiniCSharpLexer.ID, "i");
            chrMeth.AddParam(new TablaSimbolos.ParamIdent(
                chrParamTok,
                TablaSimbolos.IntType,
                false, // isArray: false
                null,
                0
            ));
            // ---------------------------------------------------------------------------------------------------------

            // Procesa las directivas 'using'
            foreach (var usingDirContext in context.usingDirective())
            {
                Visit(usingDirContext);
            }

            // Obtiene el nombre de la clase principal (el ID del contexto Prog)
            IToken classNameToken = context.ID().Symbol;
            string mainClassName = classNameToken.Text;

            // Visita las declaraciones de variables, clases y métodos dentro del programa
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

            // Imprime la tabla de símbolos al finalizar el scope del programa
            Console.WriteLine($"--- Symbol Table for Program/Class: {mainClassName} (Level: {symbolTable.NivelActual}) ---");
            symbolTable.Imprimir();

            symbolTable.CloseScope(); // Cierra el scope global
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
                Errors.Add($"Error: El identificador '{className}' ya está definido en este ámbito (línea {classToken.Line}).");
                return null;
            }
            
            var classIdent = symbolTable.InsertarClass(classToken, context);
            if (classIdent == null)
            {
                Errors.Add($"Error: No se pudo registrar la clase '{className}' (línea {classToken.Line}).");
                return null;
            }
            Console.WriteLine($"Checker DBG: Declarada clase '{className}' en nivel {symbolTable.NivelActual}");
            
            var outerTable = this.symbolTable;
            this.symbolTable = classIdent.Members;

            foreach (var varDeclCtx in context.varDecl())
            {
                Visit(varDeclCtx);
            }
            
            Console.WriteLine($"--- Members Symbol Table for Class: {className} ---");
            this.symbolTable.Imprimir();
            
            this.symbolTable = outerTable;
            return null;
        }

        public override object VisitMethodDeclaration(MiniCSharpParser.MethodDeclarationContext context)
        {
            // --- Pasos 1 y 2: Determinar firma e insertar método (tu código aquí es correcto) ---
            int returnTypeCode;
            IToken methodNameToken = context.ID().Symbol; 

            if (context.VOID() != null) 
            {
                returnTypeCode = TablaSimbolos.VoidType;
            }
            else 
            {
                // ... (tu lógica para obtener el tipo de retorno es correcta) ...
                object typeResult = Visit(context.type()); 
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
            
            // --- LÓGICA CORREGIDA ---
            
            // 3. Establecer el método actual ANTES de procesar su contenido.
            this.currentProcessingMethod = methodIdent;
            
            symbolTable.OpenScope();
            // this.currentMethodBodyScopeLevel = symbolTable.NivelActual; // Esta línea es más para CodeGen, pero no hace daño

            // 4. Visitar los parámetros Y el cuerpo del método.
            // _currentProcessingMethod permanecerá activo durante estas visitas.
            if (context.formPars() != null)
            {
                Visit(context.formPars()); 
            }

            Visit(context.block());

            symbolTable.CloseScope();

            // 5. Limpiar el método actual DESPUÉS de haber procesado todo su contenido.
            this.currentProcessingMethod = null;

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
               return null;
            }

            
            if (context.ASSIGN() != null) 
            {
                if (!(ident is TablaSimbolos.VarIdent varIdent)) 
                {
                    Errors.Add($"Error: El lado izquierdo de la asignación ('{ident.GetName()}') no es una variable o campo asignable (línea {ident.Token.Line}).");
                    return null;
                }

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
            // 1. Resolver tipo de la condición
            object condTypeObj = Visit(context.condition());
            int condType = (condTypeObj is int t) ? t : TablaSimbolos.UnknownType;

            // 2. Verificar que sea bool
            if (condType != TablaSimbolos.BoolType)
            {
                int line = context.condition().Start.Line;
                Errors.Add($"Error: La condición de 'if' debe ser de tipo 'bool', pero se encontró '{TablaSimbolos.TypeToString(condType)}' (línea {line}).");
            }

            // 3. Visitar bloque/statement del 'then'
            Visit(context.statement(0));

            // 4. Si hay 'else', visitar su statement
            if (context.ELSE() != null && context.statement().Length > 1)
            {
                Visit(context.statement(1));
            }

            return null;
        }

        public override object VisitForStatement(MiniCSharpParser.ForStatementContext context)
        {
            // 1. Abrir un nuevo ámbito para el bucle for.
            // Las variables declaradas en forInit (como 'int i = 0;') vivirán solo en este ámbito.
            symbolTable.OpenScope();

            // 2. Visitar la parte de inicialización del for (forInit).
            // Aquí puede ser una declaración de variable (varDecl) o una expresión (expr).
            if (context.forInit() != null)
            {
                // El forInit puede ser un VarDeclarationContext o un ExpressionContext.
                // El Visit adecuado será llamado por ANTLR.
                Visit(context.forInit());
            }

            // 3. Visitar la condición del for.
            // Esta sección ha sido ajustada para manejar cuando la condición es nula (ausente).
            if (context.condition() != null)
            {
                object conditionTypeObj = Visit(context.condition());
                int conditionType = (conditionTypeObj is int t) ? t : TablaSimbolos.UnknownType;

                if (conditionType != TablaSimbolos.BoolType)
                {
                    Errors.Add($"Error: La condición del bucle 'for' debe ser de tipo booleana (línea {context.condition().Start.Line}).");
                }
            }
            else // ¡Este 'else' es clave para el ERROR 16, cuando la condición está ausente!
            {
                Errors.Add($"Error: La condición del bucle 'for' es obligatoria y debe ser booleana (línea {context.Start.Line}).");
            }

            // 4. Visitar la parte de actualización del for (forUpdate).
            if (context.forUpdate() != null)
            {
                Visit(context.forUpdate());
            }

            // 5. Visitar el cuerpo del bucle for.
            if (context.statement() != null)
            {
                Visit(context.statement());
            }

            // 6. Cerrar el ámbito del bucle for.
            // Esto eliminará las variables locales del for (como 'i' si se declaró dentro).
            symbolTable.CloseScope();

            return null; // El for statement no produce un valor.
        }
        
        public override object VisitForVarDecl(MiniCSharpParser.ForVarDeclContext context)
        {
            // Esta lógica es MUY similar a tu VisitVarDeclaration, pero para la regla forVarDecl.
            // Solo permitimos una declaración de variable en forVarDecl, no una lista como en varDecl.

            MiniCSharpParser.TypeContext typeRuleContext = context.type();
            object typeResult = Visit(typeRuleContext);

            if (!(typeResult is int resolvedTypeCode) || resolvedTypeCode == TablaSimbolos.UnknownType)
            {
                // Si el tipo es desconocido o no se resolvió, ya hay un error, no continuamos.
                return null;
            }

            // Validación para no usar 'void' como tipo de variable (opcional, pero buena práctica)
            ITerminalNode typeNameTerminalNode = typeRuleContext.GetToken(MiniCSharpLexer.ID, 0);
            if (typeNameTerminalNode != null && resolvedTypeCode == TablaSimbolos.VoidType && typeNameTerminalNode.Symbol.Text == "void")
            {
                Errors.Add($"Error: No se puede declarar una variable de tipo 'void' ('{context.GetText()}' en línea {context.Start.Line}).");
                return null;
            }

            bool isArray = context.LBRACK().Length > 0; // Verifica si es un array (ej: int[] i)

            // Obtener el ID de la variable declarada (solo hay uno en forVarDecl)
            ITerminalNode idNode = context.ID();
            IToken idToken = idNode.Symbol;
            string varName = idToken.Text;

            // Verificar si la variable ya existe en el ámbito actual (para detectar duplicados en el for)
            if (symbolTable.BuscarNivelActual(varName) != null)
            {
                Errors.Add($"Error: La variable '{varName}' ya ha sido declarada en este ámbito del 'for' (línea {idToken.Line}).");
            }
            else
            {
                // ¡Insertar la variable en la tabla de símbolos en el ámbito actual del for!
                // Tu InsertarVar ya usa this.nivelActual, lo cual es perfecto.
                symbolTable.InsertarVar(idToken, resolvedTypeCode, isArray, context);
            }

            // Si hay una inicialización (ej: ' = 0'), visita la expresión y comprueba el tipo.
            if (context.expr() != null)
            {
                object exprTypeObj = Visit(context.expr());
                int exprType = (exprTypeObj is int t) ? t : TablaSimbolos.UnknownType;

                // Aquí podrías añadir lógica más sofisticada de asignación de tipos,
                // por ejemplo, si se permite la conversión implícita de int a double.
                if (exprType != TablaSimbolos.UnknownType && exprType != resolvedTypeCode)
                {
                    Errors.Add($"Error: Tipo incompatible en la inicialización de '{varName}'. Se esperaba '{TablaSimbolos.TypeToString(resolvedTypeCode)}' pero se encontró '{TablaSimbolos.TypeToString(exprType)}' (línea {idToken.Line}).");
                }
            }

            return null; // Este método no devuelve un tipo de valor.
        }
         public override object VisitForTypeAndMultipleVars(MiniCSharpParser.ForTypeAndMultipleVarsContext context)
        {
            MiniCSharpParser.TypeContext typeRuleContext = context.type();
            object typeResult = Visit(typeRuleContext);
            if (!(typeResult is int resolvedTypeCode) || resolvedTypeCode == TablaSimbolos.UnknownType)
            {
                return null;
            }

            foreach (var declaredVarPartCtx in context.forDeclaredVarPart())
            {
                IToken idToken = declaredVarPartCtx.ID().Symbol;
                string varName = idToken.Text;
                int line = idToken.Line;

                bool isArray = declaredVarPartCtx.LBRACK().Any();

                if (symbolTable.BuscarNivelActual(varName) != null)
                {
                    Errors.Add($"Error: La variable '{varName}' ya ha sido declarada en este ámbito (línea {line}).");
                    continue;
                }

                symbolTable.InsertarVar(idToken, resolvedTypeCode, isArray, declaredVarPartCtx);

                if (declaredVarPartCtx.expr() != null)
                {
                    object exprTypeObj = Visit(declaredVarPartCtx.expr());
                    int exprType = (exprTypeObj is int t) ? t : TablaSimbolos.UnknownType;

                    if (exprType != TablaSimbolos.UnknownType && exprType != resolvedTypeCode)
                    {
                        Errors.Add($"Error: Tipo incompatible en la inicialización de '{varName}'. Se esperaba '{TablaSimbolos.TypeToString(resolvedTypeCode)}' pero se encontró '{TablaSimbolos.TypeToString(exprType)}' (línea {line}).");
                    }
                }
            }

            return null;
        }
                
        
        public override object VisitForInit(MiniCSharpParser.ForInitContext context)
        {
            // Caso 1: Múltiples variables declaradas con un solo tipo (ej: for (int i = 0, limit = 5; ...))
            if (context.forTypeAndMultipleVars() != null) // Si esta alternativa coincide
            {
                Visit(context.forTypeAndMultipleVars()); // Llama al nuevo método Visit
            }
            // Caso 2: Múltiples expresiones (ej: for (i = 0, j = 10; ...))
            else if (context.expr() != null && context.expr().Any())
            {
                foreach (var exprCtx in context.expr())
                {
                    Visit(exprCtx);
                }
            }

            return null;
        }
        

        public override object VisitWhileStatement(MiniCSharpParser.WhileStatementContext context)
        {
            // Incrementamos el contador de loopDepth al entrar en el bucle
            loopDepth++;

            // 1. Chequear el tipo de la condición
            object condTypeObj = Visit(context.condition());
            int condType = (condTypeObj is int t) ? t : TablaSimbolos.UnknownType;

            // 2. Verificar que la condición sea de tipo bool
            if (condType != TablaSimbolos.BoolType)
            {
                int line = context.condition().Start.Line;
                Errors.Add(
                    $"Error: La condición de 'while' debe ser de tipo 'bool', " +
                    $"pero se encontró '{TablaSimbolos.TypeToString(condType)}' (línea {line})."
                );
            }

            // 3. Visitar recursivamente el cuerpo del while
            Visit(context.statement());

            // Salimos del bucle → decrementamos el contador de loopDepth
            loopDepth--;

            return null;
        }

        public override object VisitBreakStatement(MiniCSharpParser.BreakStatementContext context)
        {
            // 'break' es válido si estamos dentro de un bucle O un switch
            if (loopDepth == 0 && switchDepth == 0)
            {
                Errors.Add($"Error semántico: La sentencia 'break' solo puede aparecer dentro de un bucle 'for', 'while' o una sentencia 'switch' (línea {context.Start.Line}).");
            }
            return null;
        }

        public override object VisitReturnStatement(MiniCSharpParser.ReturnStatementContext context)
        {
            // 1. Verificar que estemos dentro de un método
            if (currentProcessingMethod == null)
            {
                int line = context.Start.Line;
                Errors.Add($"Error: 'return' fuera de un método (línea {line}).");
                return null;
            }

            bool hasExpr = context.expr() != null;
            int methodReturnType = currentProcessingMethod.ReturnType;

            // 2. Si el método es void, no debe haber expresión
            if (methodReturnType == TablaSimbolos.VoidType)
            {
                if (hasExpr)
                {
                    int line = context.Start.Line;
                    Errors.Add(
                        $"Error: El método '{currentProcessingMethod.GetName()}' es void y no debe devolver un valor " +
                        $"(return con expresión) (línea {line})."
                    );
                }
            }
            else
            {
                // 3. Si el método no es void, debe haber expresión
                if (!hasExpr)
                {
                    int line = context.Start.Line;
                    Errors.Add(
                        $"Error: El método '{currentProcessingMethod.GetName()}' debe devolver un valor de tipo " +
                        $"'{TablaSimbolos.TypeToString(methodReturnType)}' (falta expresión en return) (línea {line})."
                    );
                }
                else
                {
                    // 4. Verificar compatibilidad de tipos
                    object exprTypeObj = Visit(context.expr());
                    int exprType = (exprTypeObj is int t) ? t : TablaSimbolos.UnknownType;

                    bool compatible =
                        exprType == methodReturnType ||
                        (exprType == TablaSimbolos.NullType &&
                         (methodReturnType == TablaSimbolos.ClassType ||
                          methodReturnType == TablaSimbolos.StringType ||
                          methodReturnType == TablaSimbolos.ArrayType));

                    if (!compatible)
                    {
                        int line = context.expr().Start.Line;
                        Errors.Add(
                            $"Error: Tipo incompatible en return de '{currentProcessingMethod.GetName()}'. " +
                            $"Se esperaba '{TablaSimbolos.TypeToString(methodReturnType)}' " +
                            $"pero se encontró '{TablaSimbolos.TypeToString(exprType)}' (línea {line})."
                        );
                    }
                }
            }

            return null;
        }

        public override object VisitReadStatement(MiniCSharpParser.ReadStatementContext context)
        {
            // 1. Visitar el designator para resolver a qué variable se refiere y obtener su tipo.
            // Se espera que VisitDesignatorNode devuelva una TablaSimbolos.Ident.
            object designatorResult = Visit(context.designator());

            // Asegúrate de que el designator resuelva a una entrada de la tabla de símbolos (VarIdent, ParamIdent, etc.)
            if (!(designatorResult is TablaSimbolos.Ident identEntry))
            {
                Errors.Add($"Error: El argumento de 'read' debe ser una variable asignable (línea {context.designator().Start.Line}).");
                return null;
            }

            // 2. Verificar que el identificador sea una variable (no un método, clase, etc.).
            // Ojo: Si tu lenguaje permite leer campos de clase (ej: `read(myObject.field)`),
            // la lógica en VisitDesignatorNode ya debería haber resuelto `identEntry` a esa `VarIdent` o `ParamIdent` del campo.
            if (!(identEntry is TablaSimbolos.VarIdent) && !(identEntry is TablaSimbolos.ParamIdent))
            {
                Errors.Add($"Error: El argumento de 'read' debe ser una variable (línea {context.designator().Start.Line}).");
                return null;
            }

            // 3. Obtener el tipo de la variable.
            int varType = identEntry.Type;

            // 4. Verificar que el tipo de la variable sea "simple" y asignable.
            // Los tipos de referencia (clases, arrays) generalmente no se leen directamente con `read()`.
            // Adapta esta lista según los tipos "simples" que tu lenguaje soporta para `read`.
            if (varType == TablaSimbolos.UnknownType ||
                varType == TablaSimbolos.VoidType ||
                varType == TablaSimbolos.ClassType || // No se puede leer una instancia de clase directamente
                varType == TablaSimbolos.ArrayType)   // No se puede leer un array completo directamente
            {
                Errors.Add($"Error: 'read' solo puede aplicarse a variables de tipos simples (int, double, char, bool, string) (línea {context.designator().Start.Line}).");
                return null;
            }

            // Si todo es correcto, no se añaden errores.
            return null;
        }

        public override object VisitWriteStatement(MiniCSharpParser.WriteStatementContext context) // ¡Asegurate del 'override' aquí!
        {
            // *** ¡IMPORTANTE! ***
            // NO debes buscar 'Write' en la tabla de símbolos aquí.
            // 'Write' es una sentencia incorporada (built-in) de tu lenguaje.

            // 1. Visita la expresión que se quiere imprimir para determinar su tipo.
            object exprTypeObj = Visit(context.expr());
            int exprType = (exprTypeObj is int t) ? t : TablaSimbolos.UnknownType;

            // 2. Valida que el tipo de la expresión sea imprimible.
            if (exprType == TablaSimbolos.UnknownType ||
                (exprType != TablaSimbolos.IntType &&
                 exprType != TablaSimbolos.DoubleType && // Si tienes tipo double
                 exprType != TablaSimbolos.BoolType &&   // Si permites imprimir booleanos
                 exprType != TablaSimbolos.CharType &&   // Si tienes tipo char
                 exprType != TablaSimbolos.StringType && // Si tienes tipo string
                 exprType != TablaSimbolos.NullType))    // Si permites imprimir 'null'
            {
                Errors.Add($"Error: Tipo de expresión no imprimible en sentencia 'write': '{TablaSimbolos.TypeToString(exprType)}' (línea {context.expr().Start.Line}).");
            }

            // 3. Si hay un INTLIT (como en 'write(expr, 10);'), puedes añadir validación aquí.
            if (context.INTLIT() != null)
            {
                // Lógica de validación para el argumento INTLIT
            }

            // Retorna null para la sentencia, ya que no produce un valor de tipo.
            return null;
        }

        public override object VisitBlockStatement(MiniCSharpParser.BlockStatementContext context)
        {
            // Aquí, simplemente delegas el control al método Visit de la regla 'block' real.
            // Toda la lógica semántica de un bloque (ámbitos, varDecls, statements) debe estar en VisitBlockNode.
            Console.WriteLine($"Checker DBG: Visitando BlockStatement (como parte de una sentencia) en línea {context.Start.Line}");
            return Visit(context.block()); // Llama a VisitBlockNode
        }

        public override object VisitSwitchDispatchStatement(MiniCSharpParser.SwitchDispatchStatementContext context)
        {
            // Aquí simplemente delegamos al método Visit que maneja la estructura real del switch.
            // Es útil si, por ejemplo, quisieras añadir lógica específica que se aplique solo cuando un switch
            // es tratado como una sentencia general (por ejemplo, para algún análisis de flujo de control).
            // Por ahora, solo llamamos al Visit de la regla 'switchStatement' en sí misma.
            return Visit(context.switchStatement());
        }

        public override object VisitEmptyStatement(MiniCSharpParser.EmptyStatementContext context)
        {
            return base.VisitEmptyStatement(context);
        }

        public override object VisitSwitchStat(MiniCSharpParser.SwitchStatContext context)
        {
            switchDepth++; // Entra en una sentencia switch

            // 1. Visitar y verificar el tipo de la expresión del switch
            object exprTypeResult = Visit(context.expr());

            if (!(exprTypeResult is int switchExprType))
            {
                Errors.Add($"Error semántico: La expresión de la sentencia 'switch' no pudo ser evaluada a un tipo válido (línea {context.Start.Line}).");
                currentSwitchExprType = TablaSimbolos.UnknownType; // Reset en caso de error
            }
            else
            {
                // Verificar que la expresión del switch sea de tipo ordinal (int o char)
                if (switchExprType != TablaSimbolos.IntType && switchExprType != TablaSimbolos.CharType)
                {
                    Errors.Add($"Error semántico: La expresión de la sentencia 'switch' debe ser de tipo 'int' o 'char', no '{TablaSimbolos.TypeToString(switchExprType)}' (línea {context.Start.Line}).");
                    currentSwitchExprType = TablaSimbolos.UnknownType; // Marcar como desconocido para evitar más errores en 'case'
                }
                else
                {
                    currentSwitchExprType = switchExprType; // Almacena el tipo para su uso en los 'case'
                    Console.WriteLine($"Checker DBG: Expresión de switch de tipo {TablaSimbolos.TypeToString(currentSwitchExprType)}.");
                }
            }

            // Visitar el bloque del switch
            Visit(context.switchBlock());

            currentSwitchExprType = TablaSimbolos.UnknownType; // Restablecer el tipo de la expresión switch al salir
            switchDepth--; // Sale de la sentencia switch
            return null;
        }

        public override object VisitSwitchBlockContent(MiniCSharpParser.SwitchBlockContentContext context)
        {
            // Simplemente visita todas las secciones de switch dentro del bloque
            foreach (var sectionContext in context.switchSection())
            {
                Visit(sectionContext);
            }
            return null;
        }

        public override object VisitSwitchCaseSection(MiniCSharpParser.SwitchCaseSectionContext context)
        {
            // Visitar todos los labels (case o default) de esta sección
            foreach (var labelContext in context.switchLabel())
            {
                Visit(labelContext);
            }

            // Visitar todas las sentencias dentro de esta sección de case/default
            // Nota: Aquí se asume que las sentencias internas se manejan con sus propios Visit.
            foreach (var statementContext in context.statement())
            {
                Visit(statementContext);
            }
            return null;
        }

        public override object VisitCaseLabel(MiniCSharpParser.CaseLabelContext context)
        {
            // 1. Visitar la expresión del case para obtener su tipo y valor (si es constante)
            object exprTypeResult = Visit(context.expr());

            if (!(exprTypeResult is int caseExprType))
            {
                Errors.Add($"Error semántico: La expresión del 'case' no pudo ser evaluada a un tipo válido (línea {context.Start.Line}).");
                return null;
            }

            // 2. Verificar que el tipo de la expresión del case coincida con el tipo de la expresión del switch
            if (currentSwitchExprType != TablaSimbolos.UnknownType && caseExprType != currentSwitchExprType)
            {
                Errors.Add($"Error semántico: El tipo de la expresión del 'case' ('{TablaSimbolos.TypeToString(caseExprType)}') no coincide con el tipo de la expresión del 'switch' ('{TablaSimbolos.TypeToString(currentSwitchExprType)}') (línea {context.Start.Line}).");
            }

            // 3. Verificar que la expresión del case sea una constante.
            // Esta parte requiere que VisitExpr y sus subcomponentes (VisitFactor, etc.)
            // puedan determinar si un valor es constante.
            // Para una implementación "mínima" según la imagen, nos enfocamos en el tipo.
            // La validación de "constante" completa es más avanzada y requeriría
            // modificaciones significativas en cómo VisitExpr/VisitFactor devuelven información.
            // Si solo aceptas literales, entonces la presencia de un literal ya implica "constante".
            // Para el ejemplo, asumo que 'expr' en 'case' solo contendrá literales directamente.
            // Si no es un literal, podrías añadir un error aquí:
            // if (!(context.expr().GetChild(0) is MiniCSharpParser.FactorContext factor &&
            //       (factor is MiniCSharpParser.IntLitFactorContext || factor is MiniCSharpParser.CharLitFactorContext)))
            // {
            //     Errors.Add($"Error semántico: La expresión del 'case' debe ser un valor literal constante (línea {context.Start.Line}).");
            // }

            Console.WriteLine($"Checker DBG: Expresión de case de tipo {TablaSimbolos.TypeToString(caseExprType)}.");

            return null;
        }

        public override object VisitDefaultLabel(MiniCSharpParser.DefaultLabelContext context)
        {
            // No hay una expresión para verificar en el default, simplemente se marca
            Console.WriteLine($"Checker DBG: Encontrado label 'default'.");
            return null;
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
            // Una condición (condition) se compone de uno o más "términos de condición" (condTerm)
            // unidos por el operador OR (||).
            // Visitamos cada 'condTerm' para asegurar que todos sean booleanos.
            foreach (var condTermCtx in context.condTerm())
            {
                // Importante: 'Visit(condTermCtx)' llamará al método correcto: VisitConditionTermNode
                object typeObj = Visit(condTermCtx);
                if (typeObj is int type && type != TablaSimbolos.BoolType)
                {
                    Errors.Add($"Error: La expresión en el operador OR (||) debe ser booleana (línea {condTermCtx.Start.Line}).");
                }
            }
            // El resultado de una condición (condition) siempre es de tipo booleano.
            return TablaSimbolos.BoolType;
        }

        public override object VisitConditionTermNode(MiniCSharpParser.ConditionTermNodeContext context) // ¡Nombre corregido aquí!
        {
            // Un "término de condición" (condTerm) se compone de uno o más "factores de condición" (condFact)
            // unidos por el operador AND (&&).
            // Visitamos cada 'condFact' para asegurar que todos sean booleanos.
            foreach (var condFactCtx in context.condFact())
            {
                // Importante: 'Visit(condFactCtx)' llamará al método correcto: VisitConditionFactNode
                object typeObj = Visit(condFactCtx);
                if (typeObj is int type && type != TablaSimbolos.BoolType)
                {
                    Errors.Add($"Error: La expresión en el operador AND (&&) debe ser booleana (línea {condFactCtx.Start.Line}).");
                }
            }
            // El resultado de un término de condición (condTerm) siempre es de tipo booleano.
            return TablaSimbolos.BoolType;
        }

        public override object VisitConditionFactNode(MiniCSharpParser.ConditionFactNodeContext context)
        {
            // ... (código anterior si lo tenés) ...

            object leftTypeObj = Visit(context.expr(0));
            object rightTypeObj = Visit(context.expr(1));

            // Corrección: Usa nombres de variables de patrón diferentes (ej: 'tl' para left, 'tr' para right)
            int leftType = (leftTypeObj is int tl) ? tl : TablaSimbolos.UnknownType;
            int rightType = (rightTypeObj is int tr) ? tr : TablaSimbolos.UnknownType;

            // ... (resto de tu lógica de comprobación de tipos y retorno) ...

            if (leftType == TablaSimbolos.UnknownType || rightType == TablaSimbolos.UnknownType)
            {
                // Si los tipos ya son desconocidos, no se hace más.
            }
            else if (leftType != rightType)
            {
                Errors.Add($"Error: Tipos incompatibles en la comparación relacional: '{TablaSimbolos.TypeToString(leftType)}' y '{TablaSimbolos.TypeToString(rightType)}' (línea {context.Start.Line}).");
            }

            return TablaSimbolos.BoolType;
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
            return Visit(context.expr());
        }

        public override object VisitDesignatorNode(MiniCSharpParser.DesignatorNodeContext context)
        {
            // El designador siempre comienza con un ID (el nombre base de la variable, campo, o método).
            IToken firstToken = context.ID(0).Symbol;
            string firstName = firstToken.Text;
            int line = firstToken.Line; // Usamos la línea del primer token para errores iniciales

            // 1. Buscar la entrada base en la tabla de símbolos.
            TablaSimbolos.Ident currentIdent = symbolTable.Buscar(firstName);

            if (currentIdent == null)
            {
                Errors.Add($"Error: El identificador '{firstName}' no ha sido declarado (línea {line}).");
                return null; // No se puede resolver el designador.
            }

            // 2. Procesar las partes adicionales del designador (DOT ID o LBRACK expr RBRACK)
            // Usamos índices para ID y expr porque context.ID() y context.expr() dan listas.
            int idIndex = 1;   // ID(0) ya lo usamos, empezamos en ID(1) para miembros
            int exprIndex = 0; // Para acceder a las expresiones de índice en un array

            // Iteramos sobre todos los hijos del contexto del designador, saltándonos el primer ID.
            // Los hijos serán DOT, LBRACK, ID de miembro, Expr de índice, RBRACK, etc.
            foreach (var suffix in context.children.Skip(1))
            {
                // Si en algún punto el identificador actual es nulo (por un error anterior en la cadena del designador),
                // salimos para evitar NullReferenceException.
                if (currentIdent == null) return null;

                // Comprobamos si el hijo actual es un nodo terminal (un token como '.' o '[').
                if (suffix is Antlr4.Runtime.Tree.ITerminalNode termNode)
                {
                    // Acceso a miembro: obj.member
                    if (termNode.Symbol.Type == MiniCSharpLexer.DOT)
                    {
                        // Verificar que el identificador actual sea una clase o un objeto de clase.
                        // Tu primera versión comprueba si es 'ClassIdent'
                        // Tu segunda versión comprueba si es 'VarIdent' cuyo 'Type' es 'ClassType'
                        // La segunda versión es más robusta porque una variable de clase es una VarIdent.
                        if (!(currentIdent is TablaSimbolos.VarIdent varIdent) || varIdent.Type != TablaSimbolos.ClassType)
                        {
                            Errors.Add($"Error: El operador '.' solo se puede aplicar a un objeto de una clase, pero '{currentIdent.GetName()}' no lo es (línea {termNode.Symbol.Line}).");
                            return null;
                        }

                        // Necesitamos el ClassIdent que define la estructura de la clase
                        // La segunda versión tiene una lógica más completa para obtener el ClassIdent desde VarIdent.DeclCtx
                        TablaSimbolos.ClassIdent classDef = null;
                        if (varIdent.DeclCtx is MiniCSharpParser.VarDeclarationContext varDeclCtx)
                        {
                            if (varDeclCtx.type() is MiniCSharpParser.TypeIdentContext typeIdentCtx)
                            {
                                string className = typeIdentCtx.ID().GetText();
                                classDef = symbolTable.Buscar(className) as TablaSimbolos.ClassIdent;
                            }
                        }
                        
                        if (classDef == null)
                        {
                            Errors.Add($"Error interno: No se pudo resolver la definición de clase para '{varIdent.GetName()}' (línea {varIdent.Token.Line}).");
                            return null;
                        }

                        // Obtener el nombre del miembro (el ID después del DOT)
                        // Usamos idIndex para obtener el siguiente ID de la lista context.ID()
                        string fieldName = context.ID(idIndex++)?.GetText();
                        if (string.IsNullOrEmpty(fieldName)) // Si no hay ID después del DOT, es un error sintáctico o de estructura de árbol
                        {
                            Errors.Add($"Error: Se esperaba un nombre de miembro después de '.' (línea {termNode.Symbol.Line}).");
                            return null;
                        }

                        // Buscar el miembro en la tabla de símbolos de la clase.
                        TablaSimbolos.Ident memberIdent = classDef.Members.Buscar(fieldName);
                        if (memberIdent == null)
                        {
                            Errors.Add($"Error: La clase '{classDef.GetName()}' no contiene una definición para el miembro '{fieldName}' (línea {termNode.Symbol.Line}).");
                            return null;
                        }

                        currentIdent = memberIdent; // El nuevo identificador actual es el miembro encontrado
                    }
                    // Acceso a array: arr[index]
                    else if (termNode.Symbol.Type == MiniCSharpLexer.LBRACK)
                    {
                        // Verificar que el identificador actual sea una variable de tipo array.
                        if (!(currentIdent is TablaSimbolos.VarIdent varIdent) || !varIdent.IsArray)
                        {
                            Errors.Add($"Error: Solo se pueden indexar arrays. '{currentIdent.GetName()}' no es un array (línea {termNode.Symbol.Line}).");
                            return null;
                        }

                        // Obtener la expresión del índice
                        MiniCSharpParser.ExprContext indexExprCtx = context.expr(exprIndex++);
                        if (indexExprCtx == null)
                        {
                            Errors.Add($"Error: Se esperaba una expresión de índice después de '[' (línea {termNode.Symbol.Line}).");
                            return null;
                        }

                        // Visitar la expresión para obtener su tipo.
                        object indexTypeResult = Visit(indexExprCtx);
                        int indexType = (indexTypeResult is int t) ? t : TablaSimbolos.UnknownType;

                        // Verificar que el tipo del índice sea int.
                        if (indexType != TablaSimbolos.IntType)
                        {
                            Errors.Add($"Error: El índice de un array debe ser una expresión de tipo 'int', no '{TablaSimbolos.TypeToString(indexType)}' (línea {indexExprCtx.Start.Line}).");
                            return null;
                        }
                        
                        // IMPORTANTE: Cuando se accede a un elemento de un array, el resultado ya no es un array.
                        // Si tu VarIdent tiene un campo para el "tipo base" del array, úsalo aquí.
                        // Aquí, asumo que `varIdent.Type` ya es el tipo base del array.
                        currentIdent = new TablaSimbolos.VarIdent(
                            varIdent.Token,
                            varIdent.Type, // El tipo de la variable original (ej: int si es int[]).
                            false,         // isArray: false, porque ya accedimos a un elemento.
                            varIdent.DeclCtx,
                            varIdent.Nivel
                        );
                    }
                    // Si llegamos a un token que no es DOT ni LBRACK aquí, y no es el primer ID,
                    // podría ser un RBRACK u otro token que no esperábamos.
                    // La estructura del bucle `foreach (var suffix in context.children.Skip(1))` es propensa a esto
                    // si la gramática de ANTLR no agrupa el DOT ID y LBRACK expr RBRACK como un único 'suffix' context.
                    // Por eso, la comprobación explícita `if (suffix is ITerminalNode termNode)` es crucial.
                }
                // Nota: No necesitamos manejar el RBRACK explícitamente como un sufijo en el bucle
                // porque ya se "consume" como parte de la regla LBRACK expr RBRACK.
            }

            // Devolvemos la entrada final resuelta del designador (el Ident de la variable, campo o elemento de array).
            return currentIdent;
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

        public override object VisitTerminal(Antlr4.Runtime.Tree.ITerminalNode node)
        {
            // Obtener el tipo de token (un número que representa el token del lexer, ej: MiniCSharpLexer.ID)
            int tokenType = node.Symbol.Type;
            // Obtener el texto del token (ej: "i", "limit", "Write")
            string tokenText = node.GetText();
            // Obtener la línea donde se encuentra el token en el código fuente
            int line = node.Symbol.Line;

            // *** ¡LÓGICA CRÍTICA! ***
            // Solo si el token es un 'ID' (un identificador genérico definido por el usuario),
            // debemos buscarlo en la tabla de símbolos.
            // Las palabras clave (como 'Write', 'if', 'for', 'class', etc.) tienen sus propios tipos de token
            // (ej: MiniCSharpLexer.WRITE, MiniCSharpLexer.IF) y NO deben ser buscadas aquí.
            if (tokenType == MiniCSharpLexer.ID) // <-- ¡Asegúrate que MiniCSharpLexer.ID es el tipo de token para tu regla 'ID' en el lexer!
            {
                // Buscar el identificador en la tabla de símbolos del checker.
                var entry = symbolTable.Buscar(tokenText); // Asumo que 'symbolTable' es tu instancia de TablaSimbolos.
                if (entry == null)
                {
                    // Si el identificador no se encuentra, reporta un error.
                    Errors.Add($"Error: El identificador '{tokenText}' no ha sido declarado (línea {line}).");
                }
            }

            // Para la mayoría de los terminales (incluyendo palabras clave que no son ID),
            // no necesitamos hacer nada más aquí, ya que su contexto lo manejan los Visit...Statement.
            // Este método solo se preocupa de validar que los ID genéricos estén declarados.

            return null; // Los nodos terminales no devuelven un tipo de valor al árbol sintáctico.
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