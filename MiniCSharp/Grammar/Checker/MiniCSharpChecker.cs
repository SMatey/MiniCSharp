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
        public MiniCsharpChecker()
        {
            this.symbolTable = new TablaSimbolos();
            this.Errors = new List<string>();
        }

        public override object VisitProg(MiniCSharpParser.ProgContext context)
        {
            symbolTable.OpenScope(); 
            
            foreach (var usingDirContext in context.usingDirective()) //
            {
                Visit(usingDirContext); 
            }
            
            IToken classNameToken = context.ID().Symbol; 
            foreach (var varDeclContext in context.varDecl()) 
            {
                Visit(varDeclContext); 
            }

            foreach (var classDeclContext in context.classDecl()) 
            {
                Visit(classDeclContext); 
            }
            
            
            foreach (var methodDeclContext in context.methodDecl()) 
            {
                Visit(methodDeclContext); 
            }
            
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
            // 1. Determinar el tipo de la declaración.
            // Visitamos el nodo 'type' que nos devolverá el código del tipo (int, char, etc.)
            // o el código de un tipo de clase si es un objeto.
            object typeResult = Visit(context.type()); // Esto llamará a VisitTypeIdent

            if (!(typeResult is int resolvedTypeCode))
            {
                // Si VisitTypeIdent no pudo resolver el tipo o devolvió algo inesperado.
                // VisitTypeIdent ya debería haber reportado un error específico.
                // No continuamos con la declaración de estas variables si el tipo es inválido.
                // Reportar un error genérico aquí podría ser redundante si VisitTypeIdent ya lo hizo.
                // Errors.Add($"Error: Tipo desconocido o inválido en la declaración de variable cerca de '{context.type().GetText()}' en línea {context.type().Start.Line}.");
                return null;
            }

            if (resolvedTypeCode == TablaSimbolos.UnknownType)
            {
                // VisitTypeIdent ya reportó el error específico.
                // No declaramos variables con un tipo desconocido.
                return null;
            }
            
            if (resolvedTypeCode == TablaSimbolos.VoidType && context.type().ID().GetText() == "void")
            {
                // No se pueden declarar variables de tipo 'void'.
                // VisitTypeIdent podría devolver VoidType si el ID es "void".
                Errors.Add($"Error: No se puede declarar una variable de tipo 'void' ('{context.GetText()}' en línea {context.Start.Line}).");
                return null;
            }

            // 2. Determinar si es un array.
            // La regla 'type' es: ID (LBRACK RBRACK)?
            bool isArray = context.type().LBRACK() != null; //

            // 3. Registrar cada identificador (variable) en la tabla de símbolos.
            // La regla 'varDecl' es: type ID (COMMA ID)* SEMICOLON
            // context.ID() devuelve una lista de todos los tokens ID.
            foreach (IToken idToken in context.ID())
            {
                string varName = idToken.Text;

                // Verificar si la variable ya ha sido declarada en el ámbito actual.
                if (symbolTable.BuscarNivelActual(varName) != null)
                {
                    Errors.Add($"Error: La variable '{varName}' ya está definida en este ámbito (línea {idToken.Line}).");
                }
                else
                {
                    // Insertar la variable en la tabla de símbolos.
                    symbolTable.InsertarVar(idToken, resolvedTypeCode, isArray, context);
                    // Console.WriteLine($"Checker DBG: Declarada variable '{varName}' de tipo {TablaSimbolos.TypeToString(resolvedTypeCode)}{(isArray ? "[]" : "")} en nivel {symbolTable.NivelActual}");
                }
            }

            return null;
        }

        public override object VisitClassDeclaration(MiniCSharpParser.ClassDeclarationContext context)
        {
            return base.VisitClassDeclaration(context);
        }

        public override object VisitMethodDeclaration(MiniCSharpParser.MethodDeclarationContext context)
        {
            return base.VisitMethodDeclaration(context);
        }

        public override object VisitFormalParams(MiniCSharpParser.FormalParamsContext context)
        {
            return base.VisitFormalParams(context);
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

        public override object VisitBlockNode(MiniCSharpParser.BlockNodeContext context)
        {
            return base.VisitBlockNode(context);
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
            return base.VisitTermNode(context);
        }

        public override object VisitDesignatorFactor(MiniCSharpParser.DesignatorFactorContext context)
        {
            return base.VisitDesignatorFactor(context);
        }

        public override object VisitIntLitFactor(MiniCSharpParser.IntLitFactorContext context)
        {
            return base.VisitIntLitFactor(context);
        }

        public override object VisitDoubleLitFactor(MiniCSharpParser.DoubleLitFactorContext context)
        {
            return base.VisitDoubleLitFactor(context);
        }

        public override object VisitCharLitFactor(MiniCSharpParser.CharLitFactorContext context)
        {
            return base.VisitCharLitFactor(context);
        }

        public override object VisitStringLitFactor(MiniCSharpParser.StringLitFactorContext context)
        {
            return base.VisitStringLitFactor(context);
        }

        public override object VisitTrueLitFactor(MiniCSharpParser.TrueLitFactorContext context)
        {
            return base.VisitTrueLitFactor(context);
        }

        public override object VisitFalseLitFactor(MiniCSharpParser.FalseLitFactorContext context)
        {
            return base.VisitFalseLitFactor(context);
        }

        public override object VisitNullLitFactor(MiniCSharpParser.NullLitFactorContext context)
        {
            return base.VisitNullLitFactor(context);
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