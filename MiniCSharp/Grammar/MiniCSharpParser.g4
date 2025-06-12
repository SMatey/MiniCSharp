parser grammar MiniCSharpParser;

options {
    tokenVocab=MiniCSharpLexer; 
}


program      : usingDirective* CLASS ID LBRACE (varDecl | classDecl | methodDecl)* RBRACE    # Prog;


usingDirective
    : USING qualifiedIdentifier SEMICOLON                                                   # UsingStat;


qualifiedIdentifier
    : ID (DOT ID)* # QualifiedIdent;


varDecl      : type ID (ASSIGN expr)? (COMMA ID (ASSIGN expr)?)* SEMICOLON # VarDeclaration;

classDecl    : CLASS ID LBRACE varDecl* RBRACE                                               # ClassDeclaration;


methodDecl   : (type | VOID) ID LPAREN formPars? RPAREN block                                # MethodDeclaration;


formPars     : type ID (COMMA type ID)* # FormalParams;


type         : ID (LBRACK RBRACK)?                                                           # TypeIdent;


statement
    : designator (ASSIGN expr | LPAREN actPars? RPAREN | INCREMENT | DECREMENT) SEMICOLON # DesignatorStatement
    | IF LPAREN condition RPAREN statement (ELSE statement)?             # IfStatement
    | FOR LPAREN forInit? SEMICOLON condition? SEMICOLON forUpdate? RPAREN statement # ForStatement
    | WHILE LPAREN condition RPAREN statement                            # WhileStatement
    | BREAK SEMICOLON                                                    # BreakStatement
    | RETURN expr? SEMICOLON                                             # ReturnStatement
    | READ LPAREN designator RPAREN SEMICOLON                            # ReadStatement
    | WRITE LPAREN expr (COMMA INTLIT)? RPAREN SEMICOLON                 # WriteStatement
    | block                                                              # BlockStatement
    | switchStatement                                                    # SwitchDispatchStatement
    | SEMICOLON                                                          # EmptyStatement
    ;
forVarDecl
    : type ID (LBRACK RBRACK)* (ASSIGN expr)?
    ;

// Modifica forInit para que pueda ser la nueva forVarDecl o una lista de expresiones.
forInit
    : forTypeAndMultipleVars // Permite "int i = 0, limit = 5" o "int i = 0"
    | expr (COMMA expr)* // Permite "i = 0, j = 10" o solo "i = 0"
    ;
forDeclaredVarPart
    : ID (LBRACK RBRACK)* (ASSIGN expr)?
    ;

// Modificación de forTypeAndMultipleVars para usar la nueva regla
forTypeAndMultipleVars
    : type forDeclaredVarPart (COMMA forDeclaredVarPart)*
    ;
    
// Tu regla forUpdate NO CAMBIA
forUpdate
    : statement
    ;

switchStatement
    : SWITCH LPAREN expr RPAREN LBRACE switchBlock RBRACE                # SwitchStat;


switchBlock
    : switchSection* # SwitchBlockContent;


switchSection
    : switchLabel+ statement* # SwitchCaseSection;


switchLabel
    : CASE expr COLON                                                    # CaseLabel
    | DEFAULT COLON                                                      # DefaultLabel
    ;


block        : LBRACE (varDecl | statement)* RBRACE                      # BlockNode;


actPars      : expr (COMMA expr)* # ActualParams;


condition    : condTerm (OR condTerm)* # ConditionNode;


condTerm     : condFact (AND condFact)* # ConditionTermNode;


condFact     : expr relop expr                                           # ConditionFactNode;


expr         : ADDOP? cast? term (ADDOP term)* # Expression;


cast         : LPAREN type RPAREN                                        # TypeCast;


term         : factor (MULOP factor)* # TermNode;

factor
    : designator (LPAREN actPars? RPAREN)? # DesignatorFactor 
    | INTLIT                               # IntLitFactor
    | DOUBLELIT                            # DoubleLitFactor
    | CHARLIT                              # CharLitFactor
    | STRINGLIT                            # StringLitFactor
    | TRUE                                 # TrueLitFactor
    | FALSE                                # FalseLitFactor
    | NULL                                 # NullLitFactor
    | NEW ID ( LPAREN actPars? RPAREN )?   # NewObjectFactor 
    | LPAREN expr RPAREN                   # ParenExpressionFactor
    ;


designator   : ID (DOT ID | LBRACK expr RBRACK)* # DesignatorNode;


relop        : RELOP                                                     # RelationalOp;
