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
using System.Linq;
using System.Reflection;

namespace MiniCSharp.Grammar.encoder
{
    public class CodeGen : MiniCSharpParserBaseVisitor<object>
    {
        // --- CAMPOS DE LA CLASE (DECLARADOS AQUÍ) ---
        private readonly Dictionary<string, FieldBuilder> _classFields;
        private readonly string _outputFileName;
        private readonly AssemblyName _assemblyName;
        private readonly AssemblyBuilder _assemblyBuilder;
        private readonly ModuleBuilder _moduleBuilder;
        private TypeBuilder _typeBuilder;
        private ILGenerator _ilGenerator;
        private MethodBuilder _currentMethod;
        private readonly List<Dictionary<string, LocalBuilder>> _scopedLocalVariables;
        private readonly MethodInfo _consoleWriteLineIntMethod;
        private readonly MethodInfo _consoleWriteLineStringMethod;
        private readonly Stack<Label> _breakTargets;
        private readonly MethodInfo _consoleWriteStringMethod;
        private readonly MethodInfo _consoleReadLineMethod;
        // Referencia al constructor de la clase y al método Main
        private MethodBuilder _mainMethodBuilder;
        private Type _currentSwitchExprType;        // Diccionario para registrar variables locales
        private readonly Dictionary<ParserRuleContext, LocalBuilder> _localVariables;

        /// <summary>
        /// El constructor prepara los objetos base para crear un ensamblado (.exe) dinámico.
        /// </summary>
        public CodeGen()
        {
            _assemblyName = new AssemblyName("MiniCSharpOutput");
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.Run);
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule("MiniCSharpModule");

            // --- LÍNEAS DE INICIALIZACIÓN CORREGIDAS Y AÑADIDAS ---
            _consoleWriteLineIntMethod = typeof(Console).GetMethod("WriteLine", new[] { typeof(int) });
            _consoleWriteLineStringMethod = typeof(Console).GetMethod("WriteLine", new[] { typeof(string) });
            _consoleWriteStringMethod = typeof(Console).GetMethod("Write", new[] { typeof(string) });
            _consoleReadLineMethod = typeof(Console).GetMethod("ReadLine", Type.EmptyTypes);
            _classFields = new Dictionary<string, FieldBuilder>();
            // -----------------------------------------------------------

            _localVariables = new Dictionary<ParserRuleContext, LocalBuilder>(); // Lo dejé por si lo usas
            _scopedLocalVariables = new List<Dictionary<string, LocalBuilder>>();
            _breakTargets = new Stack<Label>();
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

        private object FindIdentifier(string name)
        {
            // 1. Busca en las variables locales, comenzando por el ámbito más reciente (el último en la lista)
            //    y retrocediendo hacia los ámbitos más globales.
            foreach (var scope in Enumerable.Reverse(_scopedLocalVariables))
            {
                if (scope.TryGetValue(name, out LocalBuilder local))
                {
                    // ¡Encontrado! Es una variable local. La devolvemos.
                    return local;
                }
            }

            // 2. Si el bucle termina y no se encontró la variable local,
            //    buscamos en el diccionario de campos de clase.
            if (_classFields.TryGetValue(name, out FieldBuilder field))
            {
                // ¡Encontrado! Es un campo de clase. Lo devolvemos.
                return field;
            }

            // 3. Si no se encontró en ningún lado, devolvemos null.
            //    (El checker semántico ya debería haber reportado un error de "variable no declarada").
            return null; 
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
            
            // Finalizamos la creación del tipo.
            Type? finalType = _typeBuilder.CreateType();

            // Devolvemos el ensamblado que contiene nuestro nuevo tipo.
            return finalType.Assembly;
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
            // 1. Determina el tipo de la variable (int, char, etc.) a partir de la regla 'type'.
            Type varType = GetTypeFromTypeName(context.type().GetText());

            // 2. Itera sobre TODOS los identificadores declarados en la misma línea.
            //    Esto cumple con la regla gramatical: type ident ( "," ident )* ";"
            //    Por ejemplo, para "int x, y, z;", el bucle se ejecutará para x, y, y z.
            foreach (var idNode in context.ID())
            {
                string varName = idNode.GetText();

                // --- ESTA ES LA LÓGICA CLAVE DE DIFERENCIACIÓN ---

                // CASO A: Estamos DENTRO de un método (_ilGenerator existe).
                // Se debe crear una VARIABLE LOCAL.
                if (_ilGenerator != null)
                {
                    var local = DeclareLocal(varName, varType);
                }
                // CASO B: Estamos FUERA de un método, a nivel de clase (_ilGenerator es null).
                // Se debe crear un CAMPO DE CLASE (variable "global").
                else
                {
                    // Según la definición de tu lenguaje, los métodos son globales (como en un
                    // programa de consola), por lo que los campos que accedan también deben ser
                    // estáticos para poder ser usados directamente desde Main().
                    FieldBuilder fieldBuilder = _typeBuilder.DefineField(
                        varName,
                        varType,
                        FieldAttributes.Public | FieldAttributes.Static
                    );
                    
                    // Guardamos una referencia al campo recién creado para poder usarlo después
                    // al leerlo o asignarle valores.
                    _classFields[varName] = fieldBuilder;
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
            var paramInfo = new List<(Type type, string name)>();
            if (context.formPars() != null)
            {
                var result = Visit(context.formPars());
                if (result is List<(Type, string)> info)
                {
                    paramInfo = info;
                }
            }
            var paramTypes = paramInfo.Select(p => p.type).ToArray();

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
            OpenScope(); 

            for (int i = 0; i < paramInfo.Count; i++)
            {
                var (type, name) = paramInfo[i];
                methodBuilder.DefineParameter(i + 1, ParameterAttributes.None, name);
        
                var localBuilder = DeclareLocal(name, type);
                _ilGenerator.Emit(OpCodes.Ldarg, i);
                _ilGenerator.Emit(OpCodes.Stloc, localBuilder);
            }

            // --- 4. Visitar el Cuerpo del Método ---
            // AÑADIDO: Una comprobación de seguridad antes de visitar el cuerpo del método.
            if (context.block() != null)
            {
                Visit(context.block());
            }

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
             // 1. Usamos nuestro "GPS" para encontrar la variable.
            //    Visit(context.designator()) llamará a tu VisitDesignatorNode actualizado,
            //    que devolverá un LocalBuilder (para locales) o un FieldBuilder (para globales).
            object identifier = Visit(context.designator()); 

            // --- Caso A: Es una asignación (ej. x = 5;) ---
            if (context.ASSIGN() != null)
            {
                // 2. Generamos el código para la expresión del lado derecho (el valor a asignar).
                Visit(context.expr()); 

                // 3. Verificamos qué tipo de identificador encontramos y usamos la instrucción CIL correcta.
                if (identifier is LocalBuilder local)
                {
                    // Stloc: "Store Local". Guarda el valor de la pila en la variable local.
                    _ilGenerator.Emit(OpCodes.Stloc, local); 
                }
                else if (identifier is FieldBuilder field)
                {
                    // Stsfld: "Store Static Field". Guarda el valor de la pila en el campo de clase.
                    _ilGenerator.Emit(OpCodes.Stsfld, field); 
                }
            }
            // --- Caso B: Es un incremento o decremento (ej. i++;) ---
            else if (context.INCREMENT() != null || context.DECREMENT() != null)
            {
                // 2. Verificamos qué tipo de identificador es para saber de dónde leer y dónde guardar.
                if (identifier is LocalBuilder local)
                {
                    _ilGenerator.Emit(OpCodes.Ldloc, local);      // Ldloc: Cargar valor local
                    _ilGenerator.Emit(OpCodes.Ldc_I4_1);          // Cargar el número 1
                    _ilGenerator.Emit(context.INCREMENT() != null ? OpCodes.Add : OpCodes.Sub); // Sumar/Restar
                    _ilGenerator.Emit(OpCodes.Stloc, local);      // Stloc: Guardar valor local
                }
                else if (identifier is FieldBuilder field)
                {
                    _ilGenerator.Emit(OpCodes.Ldsfld, field);     // Ldsfld: Cargar valor del campo estático
                    _ilGenerator.Emit(OpCodes.Ldc_I4_1);          // Cargar el número 1
                    _ilGenerator.Emit(context.INCREMENT() != null ? OpCodes.Add : OpCodes.Sub); // Sumar/Restar
                    _ilGenerator.Emit(OpCodes.Stsfld, field);     // Stsfld: Guardar valor en campo estático
                }
            }
            // NOTA: Aquí también se manejaría una llamada a método como una sentencia, por ejemplo: MiMetodo();

            return null;
         }

        public override object VisitIfStatement(MiniCSharpParser.IfStatementContext context)
        {
            // Etiqueta para el inicio del bloque 'else' (o fin del 'if' si no hay 'else')
            Label elseLabel = _ilGenerator.DefineLabel();
            // Etiqueta para el final de toda la sentencia 'if-else'
            Label endIfLabel = _ilGenerator.DefineLabel();

            // 1. Evaluar la condición. Esto dejará un valor booleano (0 o 1) en la pila.
            Visit(context.condition());

            // 2. Si la condición es FALSE (0), saltar a elseLabel.
            _ilGenerator.Emit(OpCodes.Brfalse, elseLabel);

            // 3. Si la condición es TRUE, ejecutar el statement del 'if'.
            Visit(context.statement(0)); // statement(0) es el bloque 'then'

            // 4. Si hay una cláusula 'else', saltar al final del 'if' para evitar ejecutar el 'else' después del 'then'.
            if (context.ELSE() != null)
            {
                _ilGenerator.Emit(OpCodes.Br, endIfLabel); // Salta al final del 'if-else'
            }

            // 5. Marcar el inicio del bloque 'else' (o el fin si no hay 'else').
            _ilGenerator.MarkLabel(elseLabel);

            // 6. Si existe la cláusula 'else', ejecutar su statement.
            if (context.ELSE() != null)
            {
                Visit(context.statement(1)); // statement(1) es el bloque 'else'
            }

            // 7. Marcar el final de toda la sentencia 'if-else'.
            _ilGenerator.MarkLabel(endIfLabel);

            return null;
        }

        public override object VisitForStatement(MiniCSharpParser.ForStatementContext context)
        {
            // Paso 1: Abrir un nuevo ámbito para las variables declaradas en forInit
            OpenScope();

            // Paso 2: Crear etiquetas para el control del flujo del bucle
            // Etiqueta para la condición del bucle (Loop Condition)
            Label loopCondition = _ilGenerator.DefineLabel();
            // Etiqueta para el cuerpo del bucle (Loop Body)
            Label loopBody = _ilGenerator.DefineLabel();
            // Etiqueta para la sección de actualización del bucle (Loop Update)
            // Nota: Aunque no tengamos 'continue', 'loopUpdate' sigue siendo útil para la estructura interna del 'for'.
            Label loopUpdate = _ilGenerator.DefineLabel();
            // Etiqueta para el final del bucle (End Loop)
            Label endLoop = _ilGenerator.DefineLabel();

            // Guardar la etiqueta de 'break' para este bucle
            _breakTargets.Push(endLoop);
            // Ya no se maneja _continueTargets aquí, ya que 'continue' no está en la gramática.

            // Paso 3: Generar código para la inicialización del 'for' (forInit)
            // forInit se ejecuta solo una vez al principio
            if (context.forInit() != null)
            {
                Visit(context.forInit());
            }

            // Paso 4: Emitir salto incondicional a la condición del bucle para la primera evaluación
            _ilGenerator.Emit(OpCodes.Br, loopCondition);

            // Paso 5: Marcar el inicio del cuerpo del bucle
            _ilGenerator.MarkLabel(loopBody);

            // Paso 6: Generar código para el cuerpo del bucle (statement)
            Visit(context.statement());

            // Paso 7: Marcar la etiqueta para la sección de actualización
            _ilGenerator.MarkLabel(loopUpdate); // Aunque no haya 'continue', esta etiqueta es el punto de retorno para el ciclo normal del 'for'

            // Paso 8: Generar código para la actualización del 'for' (forUpdate)
            // forUpdate se ejecuta después de cada iteración del cuerpo del bucle
            if (context.forUpdate() != null)
            {
                Visit(context.forUpdate());
            }

            // Paso 9: Marcar la etiqueta para la condición del bucle
            _ilGenerator.MarkLabel(loopCondition);

            // Paso 10: Generar código para la condición del 'for'
            // Si la condición es nula (opcional), se asume 'true' (bucle infinito hasta un 'break')
            if (context.condition() != null)
            {
                Visit(context.condition()); // Esto debe dejar un valor booleano en la pila
                _ilGenerator.Emit(OpCodes.Brtrue, loopBody); // Si la condición es verdadera, saltar al cuerpo
            }
            else
            {
                // Si no hay condición, es un bucle infinito. Siempre saltar al cuerpo.
                _ilGenerator.Emit(OpCodes.Ldc_I4_1); // Empuja el valor 'true' (entero 1) a la pila
                _ilGenerator.Emit(OpCodes.Brtrue, loopBody); // Siempre salta al cuerpo si no hay condición
            }
            
            // Paso 11: Si la condición es falsa (o se sale del bucle), se marca el final del bucle
            _ilGenerator.MarkLabel(endLoop);

            // Paso 12: Eliminar la etiqueta de 'break' de la pila
            _breakTargets.Pop();
            // Ya no se hace Pop de _continueTargets.

            // Paso 13: Cerrar el ámbito del bucle 'for'
            CloseScope();

            return null;
        }
        public override object VisitWhileStatement(MiniCSharpParser.WhileStatementContext context)
        {
            // Etiqueta para el inicio del bucle (donde se reevalúa la condición)
            Label loopStart = _ilGenerator.DefineLabel();
            // Etiqueta para el final del bucle (donde se salta si la condición es falsa o por un 'break')
            Label loopEnd = _ilGenerator.DefineLabel();

            // Registrar esta etiqueta de fin de bucle para manejar 'break'
            _breakTargets.Push(loopEnd);

            // 1. Marcar el inicio del bucle.
            _ilGenerator.MarkLabel(loopStart);

            // 2. Evaluar la condición.
            Visit(context.condition());

            // 3. Si la condición es FALSE, saltar al final del bucle.
            _ilGenerator.Emit(OpCodes.Brfalse, loopEnd);

            // 4. Si la condición es TRUE, ejecutar el statement del bucle.
            Visit(context.statement());

            // 5. Saltamos incondicionalmente al inicio del bucle para reevaluar la condición.
            _ilGenerator.Emit(OpCodes.Br, loopStart);

            // 6. Marcar el final del bucle.
            _ilGenerator.MarkLabel(loopEnd);

            // Quitar la etiqueta de fin de bucle del stack
            _breakTargets.Pop();

            return null;
        }

        public override object VisitBreakStatement(MiniCSharpParser.BreakStatementContext context)
        {
            if (_breakTargets.Count > 0)
            {
                // Salta a la etiqueta de fin del bucle más interno.
                _ilGenerator.Emit(OpCodes.Br, _breakTargets.Peek());
            }
            else
            {
                // Esto debería ser un error capturado por el checker semántico,
                // pero es una buena defensa en la generación de código.
                throw new InvalidOperationException("Break statement found outside of a loop or switch statement.");
            }
            return null;
        }

        public override object VisitReturnStatement(MiniCSharpParser.ReturnStatementContext context)
        {
            if (context.expr() != null)
            {
                // Visita la expresión de retorno para dejar su valor en la pila.
                Visit(context.expr());
            }
            // Emite la instrucción de retorno.
            _ilGenerator.Emit(OpCodes.Ret);
            return null;
        }

        public override object VisitReadStatement(MiniCSharpParser.ReadStatementContext context)
        {
            // La variable a la que se asignará el valor leído.
            object designatorResult = Visit(context.designator());
            if (designatorResult is LocalBuilder local)
            {
                // MethodInfo para Console.ReadLine()
                MethodInfo consoleReadLineMethod = typeof(Console).GetMethod("ReadLine", Type.EmptyTypes);
                _ilGenerator.Emit(OpCodes.Call, consoleReadLineMethod); // Llama a ReadLine, el string resultante está en la pila

                // Convertir el string leído al tipo de la variable local
                Type targetType = local.LocalType;
                
                // Aquí necesitarás lógica de conversión basada en `targetType`
                if (targetType == typeof(int))
                {
                    MethodInfo parseIntMethod = typeof(int).GetMethod("Parse", new[] { typeof(string) });
                    _ilGenerator.Emit(OpCodes.Call, parseIntMethod);
                }
                else if (targetType == typeof(double))
                {
                    MethodInfo parseDoubleMethod = typeof(double).GetMethod("Parse", new[] { typeof(string), typeof(IFormatProvider) });
                    _ilGenerator.Emit(OpCodes.Ldtoken, typeof(CultureInfo));
                    _ilGenerator.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle", new[] { typeof(RuntimeTypeHandle) }));
                    _ilGenerator.Emit(OpCodes.Ldstr, "InvariantCulture");
                    _ilGenerator.Emit(OpCodes.Call, typeof(CultureInfo).GetMethod("GetProperty", new[] { typeof(string) }));
                    _ilGenerator.Emit(OpCodes.Callvirt, typeof(PropertyInfo).GetMethod("GetValue", new[] { typeof(object), typeof(object[]) }));
                    _ilGenerator.Emit(OpCodes.Castclass, typeof(IFormatProvider));
                    _ilGenerator.Emit(OpCodes.Call, parseDoubleMethod);
                }
                else if (targetType == typeof(char))
                {
                    MethodInfo parseCharMethod = typeof(char).GetMethod("Parse", new[] { typeof(string) });
                    _ilGenerator.Emit(OpCodes.Call, parseCharMethod);
                }
                // Si el tipo es string, no necesita conversión adicional
                // Si es bool, necesitarías Boolean.Parse
                // ...otros tipos
                else if (targetType != typeof(string))
                {
                    // Fallback para tipos no implementados explícitamente, intenta una conversión genérica
                    // Esto puede fallar en runtime si no hay un método Parse o constructor adecuado.
                    Console.WriteLine($"WARNING: Implicit conversion for 'read' to type {targetType.Name} not fully implemented. May cause runtime errors.");
                }

                // Guardar el valor convertido en la variable local.
                _ilGenerator.Emit(OpCodes.Stloc, local);
            }
            // TODO: Manejar lectura en campos de clase o elementos de array.
            return null;
        }

        public override object VisitWriteStatement(MiniCSharpParser.WriteStatementContext context)
        {
            // 1. Visita la expresión dentro de write().
            //    Esto calculará el valor y lo dejará en la cima de la pila de ejecución.
            //    El checker semántico ya se ha asegurado de que este valor es compatible con int.
            Visit(context.expr());

            // 2. Llama directamente a la versión de Console.WriteLine que acepta un entero.
            //    No hay necesidad de adivinar tipos ni hacer conversiones complejas.
            //    El valor que está en la pila se pasará como argumento.
            _ilGenerator.Emit(OpCodes.Call, _consoleWriteLineIntMethod);
    
            // La parte opcional de la gramática `(COMMA INTLIT)?` no se implementa,
            // tal como lo permite el enunciado del proyecto.

            return null;
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
            if (context.forTypeAndMultipleVars() != null)
            {
                // Si forInit es una declaración de variables (ej. int i = 0, j = 10)
                Visit(context.forTypeAndMultipleVars());
            }
            else if (context.expr() != null && context.expr().Length > 0)
            {
                // Si forInit es una lista de expresiones (ej. i = 0, j = 10)
                foreach (var exprCtx in context.expr())
                {
                    Visit(exprCtx);
                    // Si la expresión deja un valor en la pila que no se usa (ej. solo una asignación),
                    // podrías necesitar un Pop aquí. Asumo que VisitExpr maneja esto adecuadamente.
                }
            }
            return null;
        }
        public override object VisitForDeclaredVarPart(MiniCSharpParser.ForDeclaredVarPartContext context)
        {
            // Este método NO debería ser llamado directamente para una declaración de variable dentro de forInit.
            // Debería ser llamado a través de VisitForTypeAndMultipleVars pasando el tipo.
            // Sin el tipo explícito, no podemos declararla correctamente.
            // Para evitar un error si se llama accidentalmente:
            Console.WriteLine("WARNING: VisitForDeclaredVarPart called without explicit type. Variable declaration may be incorrect.");
            return base.VisitForDeclaredVarPart(context);
        }
        public object VisitForDeclaredVarPart(MiniCSharpParser.ForDeclaredVarPartContext context, Type varType)
        {
            string varName = context.ID().GetText();

            // Declarar la variable local en el ámbito actual del bucle for.
            LocalBuilder local = DeclareLocal(varName, varType);

            // Si hay una asignación inicial (ASSIGN expr)
            if (context.expr() != null)
            {
                Visit(context.expr()); // Esto evalúa la expresión y deja el valor en la pila
                
                // Asegurar que el tipo de la expresión coincida o sea convertible al tipo de la variable.
                // Esto es más un trabajo del checker, pero aquí manejamos la conversión simple si es necesaria.
                // Por ejemplo, int a = 3.5; necesitaría conversión de double a int.
                // Tu checker debería haber reportado esto. Aquí asumimos que los tipos son compatibles.

                _ilGenerator.Emit(OpCodes.Stloc, local); // Almacena el valor de la pila en la variable local
            }
            // Si no hay asignación, la variable local se inicializará a su valor por defecto (0, null, false)
            return null;
        }

        public override object VisitForTypeAndMultipleVars(MiniCSharpParser.ForTypeAndMultipleVarsContext context)
        {
            // Obtener el tipo de la declaración (ej. 'int')
            // Asume que VisitType devuelve el tipo como un System.Type o un valor que GetTypeFromTypeName puede usar.
            // Dada tu implementación de GetTypeFromTypeName, es probable que 'context.type().GetText()' sea suficiente.
            Type varDeclType = GetTypeFromTypeName(context.type().GetText());

            // Iterar sobre cada parte de la declaración de variable
            foreach (var declaredVarPartCtx in context.forDeclaredVarPart())
            {
                // Pasar el tipo a VisitForDeclaredVarPart para que sepa qué tipo de variable declarar.
                VisitForDeclaredVarPart(declaredVarPartCtx, varDeclType);
            }
            return null;
        }

        public override object VisitForUpdate(MiniCSharpParser.ForUpdateContext context)
        {
            // Según tu gramática, forUpdate es un único 'statement'.
            // Simplemente visitamos ese statement para generar su código CIL.
            // Esto asegura que si el forUpdate es "i++", "j--", "llamadaMetodo()", o un "block",
            // se visite correctamente la sub-regla que corresponda.
            Visit(context.statement());
            return null;
        }

        public override object VisitSwitchStat(MiniCSharpParser.SwitchStatContext context)
        {
            OpenScope();

            Label endSwitchLabel = _ilGenerator.DefineLabel();
            _breakTargets.Push(endSwitchLabel);

            // Evaluar la expresión del switch y guardar su valor temporalmente
            Visit(context.expr()); // La expresión deja su valor en la pila

            // Determinar el tipo de la expresión del switch.
            // ¡ADVERTENCIA! Esto sigue siendo una suposición simplificada.
            // Idealmente, el checker semántico habría anotado el tipo de context.expr()
            // en la tabla de símbolos o en el AST, y lo recuperarías de ahí.
            // Por ahora, asumimos int si los cases son INTLIT.
            _currentSwitchExprType = typeof(int);

            LocalBuilder switchValue = _ilGenerator.DeclareLocal(_currentSwitchExprType);
            _ilGenerator.Emit(OpCodes.Stloc, switchValue);

            var caseLabels = new List<(int value, Label label)>();
            Label defaultLabel = _ilGenerator.DefineLabel();
            bool hasDefault = false;

            // Acceder a las secciones del switch usando GetRuleContexts para mayor robustez
            foreach (var section in context.switchBlock().GetRuleContexts<MiniCSharpParser.SwitchSectionContext>())
            {
                // Acceder a las etiquetas de caso dentro de la sección usando GetRuleContexts
                foreach (var labelCtx in section.GetRuleContexts<MiniCSharpParser.SwitchLabelContext>())
                {
                    if (labelCtx is MiniCSharpParser.CaseLabelContext caseCtx)
                    {
                        // --- INICIO DE LA CORRECCIÓN DE COUNT Y FACTOR ---
                        if (caseCtx.expr() is MiniCSharpParser.ExpressionContext caseExpr &&
                            caseExpr.term().Length == 1 && // Esto es un método, debería estar bien.
                            // Usamos .Count() (método de extensión de LINQ) en lugar de .Count (propiedad)
                            // para mayor compatibilidad si hay alguna anomalía con la propiedad directa.
                            caseExpr.term(0).GetRuleContexts<MiniCSharpParser.FactorContext>().Count() == 1 && 
                            // Acceder al primer elemento de la colección de factores
                            caseExpr.term(0).GetRuleContexts<MiniCSharpParser.FactorContext>().First() is MiniCSharpParser.IntLitFactorContext intLitFactor) 
                        // --- FIN DE LA CORRECCIÓN DE COUNT Y FACTOR ---
                        {
                            int caseValue = int.Parse(intLitFactor.INTLIT().GetText());
                            caseLabels.Add((caseValue, _ilGenerator.DefineLabel()));
                        }
                        else
                        {
                            throw new NotSupportedException("Solo literales enteros son soportados como etiquetas de caso de switch por ahora.");
                        }
                    }
                    else if (labelCtx is MiniCSharpParser.DefaultLabelContext)
                    {
                        defaultLabel = _ilGenerator.DefineLabel();
                        hasDefault = true;
                    }
                }
            }

            foreach (var caseInfo in caseLabels)
            {
                _ilGenerator.Emit(OpCodes.Ldloc, switchValue);
                _ilGenerator.Emit(OpCodes.Ldc_I4, caseInfo.value);
                _ilGenerator.Emit(OpCodes.Beq, caseInfo.label);
            }

            if (hasDefault)
            {
                _ilGenerator.Emit(OpCodes.Br, defaultLabel);
            }
            else
            {
                _ilGenerator.Emit(OpCodes.Br, endSwitchLabel);
            }

            // Iterar sobre las secciones nuevamente para generar el código
            foreach (var section in context.switchBlock().GetRuleContexts<MiniCSharpParser.SwitchSectionContext>())
            {
                bool firstLabelOfSectionHandled = false;
                foreach (var labelCtx in section.GetRuleContexts<MiniCSharpParser.SwitchLabelContext>())
                {
                    if (labelCtx is MiniCSharpParser.CaseLabelContext caseCtx)
                    {
                        if (caseCtx.expr() is MiniCSharpParser.ExpressionContext caseExpr &&
                            caseExpr.term().Length == 1 &&
                            caseExpr.term(0).GetRuleContexts<MiniCSharpParser.FactorContext>().Count() == 1 &&
                            caseExpr.term(0).GetRuleContexts<MiniCSharpParser.FactorContext>().First() is MiniCSharpParser.IntLitFactorContext intLitFactor)
                        {
                            int caseValue = int.Parse(intLitFactor.INTLIT().GetText());
                            var currentCaseInfo = caseLabels.FirstOrDefault(cl => cl.value == caseValue);
                            if (currentCaseInfo.label != null && currentCaseInfo.label != default(Label) && !firstLabelOfSectionHandled)
                            {
                                _ilGenerator.MarkLabel(currentCaseInfo.label);
                                firstLabelOfSectionHandled = true;
                            }
                        }
                    }
                    else if (labelCtx is MiniCSharpParser.DefaultLabelContext)
                    {
                        if (!firstLabelOfSectionHandled && hasDefault)
                        {
                            _ilGenerator.MarkLabel(defaultLabel);
                            firstLabelOfSectionHandled = true;
                        }
                    }
                }

                // --- INICIO DE LA CORRECCIÓN DE STATEMENT ---
                // Acceder a las sentencias dentro de la sección usando GetRuleContexts
                foreach (var stmtCtx in section.GetRuleContexts<MiniCSharpParser.StatementContext>())
                {
                // --- FIN DE LA CORRECCIÓN DE STATEMENT ---
                    Visit(stmtCtx);
                }
                
                _ilGenerator.Emit(OpCodes.Br, endSwitchLabel);
            }

            _ilGenerator.MarkLabel(endSwitchLabel);
            _breakTargets.Pop();
            CloseScope();
            _currentSwitchExprType = null; // Limpiar el campo
            return null;
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
            // Una condición (condition) es una o más condTerm unidas por '||'.
            // condition : condTerm (OR condTerm)*

            // Si solo hay un condTerm, lo visitamos directamente.
            if (context.condTerm().Length == 1)
            {
                Visit(context.condTerm(0));
            }
            else
            {
                // Para OR, la lógica es:
                // Evaluar el primer condTerm. Si es verdadero, saltar al final (la condición completa es verdadera).
                // Si es falso, evaluar el siguiente condTerm, y así sucesivamente.
                // Si todos son falsos, la condición completa es falsa.

                Label endOfCondition = _ilGenerator.DefineLabel(); // Etiqueta para el final de la condición (si es verdadera)
                Label nextCondTerm = _ilGenerator.DefineLabel(); // Etiqueta para el siguiente condTerm

                Visit(context.condTerm(0));
                _ilGenerator.Emit(OpCodes.Brtrue, endOfCondition); // Si el primer condTerm es verdadero, saltar a endOfCondition

                for (int i = 1; i < context.condTerm().Length; i++)
                {
                    _ilGenerator.MarkLabel(nextCondTerm); // Marcamos el inicio del siguiente condTerm
                    Visit(context.condTerm(i));
                    _ilGenerator.Emit(OpCodes.Brtrue, endOfCondition); // Si este condTerm es verdadero, saltar a endOfCondition
                }

                // Si llegamos aquí, significa que todos los condTerms fueron falsos.
                // Cargar 0 (false) en la pila.
                _ilGenerator.Emit(OpCodes.Ldc_I4_0);
                _ilGenerator.Emit(OpCodes.Br, endOfCondition); // Saltar al final de la condición

                _ilGenerator.MarkLabel(endOfCondition); // Marcar el final de la condición
            }
            return null;
        }


        public override object VisitConditionTermNode(MiniCSharpParser.ConditionTermNodeContext context) // <-- ¡CAMBIO AQUÍ!
        {
            // Un condTerm es uno o más condFact unidos por '&&'.
            // condTerm : condFact (AND condFact)* # ConditionTermNode;

            // Si solo hay un condFact, lo visitamos directamente.
            if (context.condFact().Length == 1)
            {
                Visit(context.condFact(0));
            }
            else
            {
                // Para AND, la lógica es:
                // Evaluar el primer condFact. Si es falso, saltar al final (el condTerm completo es falso).
                // Si es verdadero, evaluar el siguiente condFact, y así sucesivamente.
                // Si todos son verdaderos, el condTerm completo es verdadero.

                Label endOfCondTerm = _ilGenerator.DefineLabel(); // Etiqueta para el final del condTerm (si es falso)
                Label nextCondFact = _ilGenerator.DefineLabel(); // Etiqueta para el siguiente condFact

                Visit(context.condFact(0));
                _ilGenerator.Emit(OpCodes.Brfalse, endOfCondTerm); // Si el primer condFact es falso, saltar a endOfCondTerm

                for (int i = 1; i < context.condFact().Length; i++)
                {
                    _ilGenerator.MarkLabel(nextCondFact); // Marcar el inicio del siguiente condFact
                    Visit(context.condFact(i));
                    _ilGenerator.Emit(OpCodes.Brfalse, endOfCondTerm); // Si este condFact es falso, saltar a endOfCondTerm
                }

                // Si llegamos aquí, significa que todos los condFacts fueron verdaderos.
                // Cargar 1 (true) en la pila.
                _ilGenerator.Emit(OpCodes.Ldc_I4_1);
                _ilGenerator.Emit(OpCodes.Br, endOfCondTerm); // Saltar al final del condTerm

                _ilGenerator.MarkLabel(endOfCondTerm); // Marcar el final del condTerm
            }
            return null;
        }

         public override object VisitConditionFactNode(MiniCSharpParser.ConditionFactNodeContext context)
        {
            // Un condFact es expr relop expr
            // 1. Visitar la expresión izquierda. Su valor queda en la pila.
            Visit(context.expr(0));

            // 2. Visitar la expresión derecha. Su valor queda en la pila.
            // Ahora la pila tiene: [valor_izquierdo, valor_derecho]
            Visit(context.expr(1));

            // 3. Obtener el operador de relación.
            string op = (string)Visit(context.relop()); // VisitRelationalOp devuelve el string del operador

            // 4. Emitir la instrucción de comparación CIL correspondiente.
            // Las instrucciones de comparación (Ceq, Cgt, Clt) dejan 1 (true) o 0 (false) en la pila.
            switch (op)
            {
                case "==":
                    _ilGenerator.Emit(OpCodes.Ceq); // Compara si son iguales
                    break;
                case "!=":
                    _ilGenerator.Emit(OpCodes.Ceq); // Compara si son iguales
                    _ilGenerator.Emit(OpCodes.Ldc_I4_0); // Carga 0 (false)
                    _ilGenerator.Emit(OpCodes.Ceq); // Si eran iguales (1), 1==0 es 0. Si eran diferentes (0), 0==0 es 1.
                                                    // Esto invierte el resultado del Ceq inicial.
                    break;
                case "<":
                    _ilGenerator.Emit(OpCodes.Clt); // Compara si el primero es menor que el segundo
                    break;
                case "<=":
                    // Para <= (a <= b) es !(a > b).
                    _ilGenerator.Emit(OpCodes.Cgt); // Compara si el primero es mayor que el segundo
                    _ilGenerator.Emit(OpCodes.Ldc_I4_0); // Carga 0 (false)
                    _ilGenerator.Emit(OpCodes.Ceq); // Invierte el resultado
                    break;
                case ">":
                    _ilGenerator.Emit(OpCodes.Cgt); // Compara si el primero es mayor que el segundo
                    break;
                case ">=":
                    // Para >= (a >= b) es !(a < b).
                    _ilGenerator.Emit(OpCodes.Clt); // Compara si el primero es menor que el segundo
                    _ilGenerator.Emit(OpCodes.Ldc_I4_0); // Carga 0 (false)
                    _ilGenerator.Emit(OpCodes.Ceq); // Invierte el resultado
                    break;
                default:
                    throw new NotSupportedException($"Operador relacional no soportado: {op}");
            }

            return null;
        }

        public override object VisitExpression(MiniCSharpParser.ExpressionContext context)
         {
             // La regla de la gramática es: ADDOP? cast? term (ADDOP term)*
             // Primero, visitamos el término inicial. Su valor quedará en la cima de la pila CIL.
             Visit(context.term(0));

             // Verificamos si hay un operador unario al principio (ej. -a + b)
             // Esto ocurre si hay más operadores ADDOP que términos binarios.
             bool hasUnaryOp = context.ADDOP().Length > context.term().Length - 1;
             if (hasUnaryOp)
             {
                 // Si el operador unario es '-', emitimos la instrucción de negación.
                 if (context.ADDOP(0).GetText() == "-")
                 {
                     _ilGenerator.Emit(OpCodes.Neg);
                 }
                 // Nota: El '+' unario no tiene una instrucción CIL, simplemente se ignora.
             }

             // Ahora procesamos las operaciones binarias (el resto de la expresión)
             int binaryOpCount = context.term().Length - 1;
             for (int i = 0; i < binaryOpCount; i++)
             {
                 // Visitamos el siguiente término (el operando derecho).
                 // Esto deja su valor en la pila. Ahora la pila tiene: [valor_izquierdo, valor_derecho]
                 Visit(context.term(i + 1));

                 // Determinamos qué operador es (+ o -)
                 // Si había un operador unario, los índices de los operadores binarios se desplazan.
                 int opIndex = hasUnaryOp ? i + 1 : i;
                 IToken op = context.ADDOP(opIndex).Symbol;

                 switch (op.Text)
                 {
                     case "+":
                         // Emite la instrucción de suma. Toma los dos valores de la pila,
                         // los suma y pone el resultado de vuelta en la cima de la pila.
                         _ilGenerator.Emit(OpCodes.Add);
                         break;
                     case "-":
                         // Emite la instrucción de resta.
                         _ilGenerator.Emit(OpCodes.Sub);
                         break;
                 }
             }

             // Al final, el resultado de toda la expresión está en la cima de la pila.
             return null;
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
            // 1. Usamos nuestro "GPS" para encontrar la variable.
            object identifier = Visit(context.designator());

            // 2. Nos aseguramos de que no es una llamada a método (ej. "MiMetodo()"),
            //    sino una simple variable (ej. "x").
            if (context.LPAREN() == null)
            {
                // 3. Verificamos qué tipo de identificador es y usamos la instrucción CIL correcta.
                if (identifier is LocalBuilder local)
                {
                    // Ldloc: "Load Local". Carga el valor de la variable local en la pila.
                    _ilGenerator.Emit(OpCodes.Ldloc, local);
                    return local.LocalType;
                }
                if (identifier is FieldBuilder field)
                {
                    // Ldsfld: "Load Static Field". Carga el valor del campo de clase en la pila.
                    _ilGenerator.Emit(OpCodes.Ldsfld, field); 
                    return field.FieldType;
                }
            }
    
            // NOTA: Aquí iría la lógica para manejar llamadas a métodos que devuelven un valor.
    
            return typeof(void); // Devolvemos un tipo por defecto en caso de error.
        }

        public override object VisitIntLitFactor(MiniCSharpParser.IntLitFactorContext context)
        {
            int value = int.Parse(context.INTLIT().GetText());
            _ilGenerator.Emit(OpCodes.Ldc_I4, value);
            return typeof(int); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitDoubleLitFactor(MiniCSharpParser.DoubleLitFactorContext context)
        {
            double value = double.Parse(context.DOUBLELIT().GetText(), CultureInfo.InvariantCulture);
            _ilGenerator.Emit(OpCodes.Ldc_R8, value);
            return typeof(double); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitCharLitFactor(MiniCSharpParser.CharLitFactorContext context)
        {
            string text = context.CHARLIT().GetText();
            char value = text.Substring(1, text.Length - 2)[0];
            _ilGenerator.Emit(OpCodes.Ldc_I4, (int)value);
            return typeof(char); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitStringLitFactor(MiniCSharpParser.StringLitFactorContext context)
        {
            string text = context.STRINGLIT().GetText();
            string value = text.Substring(1, text.Length - 2);
            _ilGenerator.Emit(OpCodes.Ldstr, value);
            return typeof(string); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitTrueLitFactor(MiniCSharpParser.TrueLitFactorContext context)
        {
            _ilGenerator.Emit(OpCodes.Ldc_I4_1);
            return typeof(bool); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitFalseLitFactor(MiniCSharpParser.FalseLitFactorContext context)
        {
            _ilGenerator.Emit(OpCodes.Ldc_I4_0);
            return typeof(bool); // ¡Cambio clave: devuelve el tipo!
        }

        public override object VisitNullLitFactor(MiniCSharpParser.NullLitFactorContext context)
        {
            _ilGenerator.Emit(OpCodes.Ldnull);
            return typeof(object); // ¡Cambio clave: devuelve el tipo! (null es compatible con cualquier tipo de referencia)
        }

        public override object VisitNewObjectFactor(MiniCSharpParser.NewObjectFactorContext context)
        {
            return base.VisitNewObjectFactor(context);
        }

        public override object VisitParenExpressionFactor(MiniCSharpParser.ParenExpressionFactorContext context)
        {
            return Visit(context.expr());
        }

        public override object VisitDesignatorNode(MiniCSharpParser.DesignatorNodeContext context)
        {
            // 1. Obtiene el nombre del identificador que se está buscando (ej. "x", "contador").
            //    Tu gramática asegura que siempre habrá al menos un ID.
            string varName = context.ID(0).GetText();

            // 2. Llama a nuestro nuevo método unificado "FindIdentifier" para encontrar la variable.
            //    Este método ya sabe buscar tanto en locales como en campos de clase.
            //    El resultado será un objeto LocalBuilder, un FieldBuilder, o null si no se encuentra.
            return FindIdentifier(varName); 
        }

        public override object VisitRelationalOp(MiniCSharpParser.RelationalOpContext context)
        {
            // Este método NO debe generar CIL. Su propósito es simplemente
            // devolver el texto del operador para que `VisitConditionFactNode`
            // sepa qué instrucción CIL de comparación emitir.
            return context.RELOP().GetText();
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