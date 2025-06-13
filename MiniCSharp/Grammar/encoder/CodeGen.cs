using System;
using System.Collections.Generic;
using System.IO; // Necesario para la clase Path
using System.Reflection;
using System.Reflection.Emit;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Lokad.ILPack; // Necesario para el AssemblyGenerator
using parser.generated;

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

        // Diccionario para registrar variables locales
        private readonly Dictionary<ParserRuleContext, LocalBuilder> _localVariables;

        /// <summary>
        /// El constructor prepara los objetos base para crear un ensamblado (.exe) dinámico.
        /// </summary>
        public CodeGen(string outputFileName)
        {
            // --- INICIALIZACIÓN DE LOS CAMPOS EN EL CONSTRUCTOR ---
            this._outputFileName = outputFileName;
            _assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(outputFileName));
            
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.RunAndCollect);
            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(_outputFileName);
            
            // Inicializa el diccionario para las variables locales
            _localVariables = new Dictionary<ParserRuleContext, LocalBuilder>();
        }
        
        /// <summary>
        /// Método para guardar el ensamblado generado en un archivo.
        /// </summary>
        public void SaveAssembly()
        {
            try
            {
                // El objetivo es crear un archivo ejecutable que se pueda correr en consola
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
            // TODO: Aquí implementaremos la creación de la clase principal
            return base.VisitProg(context);
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
            return base.VisitVarDeclaration(context);
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