parser grammar MiniCSharpParser;

options {
    tokenVocab=MiniCSharpLexer; 
}

// Regla inicial del programa
program      : usingDirective* CLASS ID LBRACE (varDecl | classDecl | methodDecl)* RBRACE ;

// Directiva 'using' para importar namespaces/clases
usingDirective
    : USING qualifiedIdentifier SEMICOLON
    ;

// Identificador calificado (ej. System.Collections)
qualifiedIdentifier
    : ID (DOT ID)*
    ;

// Declaracion de variable
varDecl      : type ID (COMMA ID)* SEMICOLON ;

// Declaracion de clase interna 
classDecl    : CLASS ID LBRACE varDecl* RBRACE ;

// Declaracion de metodo
methodDecl   : (type | VOID) ID LPAREN formPars? RPAREN block ;

// Parametros formales de un metodo
formPars     : type ID (COMMA type ID)* ;

// Definición de tipo (int, char, MiClase, int[])
type         : ID (LBRACK RBRACK)? ; 

// Sentencias
statement
    : designator (ASSIGN expr | LPAREN actPars? RPAREN | INCREMENT | DECREMENT) SEMICOLON 
    | IF LPAREN condition RPAREN statement (ELSE statement)?             
    | FOR LPAREN expr SEMICOLON condition? SEMICOLON statement? RPAREN statement 
    | WHILE LPAREN condition RPAREN statement                           
    | BREAK SEMICOLON                                                    
    | RETURN expr? SEMICOLON                                             
    | READ LPAREN designator RPAREN SEMICOLON                            
    | WRITE LPAREN expr (COMMA INTLIT)? RPAREN SEMICOLON                 
    | block                                                              
    | switchStatement                                                    
    | SEMICOLON                                                          
    ;

// Sentencia Switch
switchStatement
    : SWITCH LPAREN expr RPAREN LBRACE switchBlock RBRACE
    ;

// Bloque interno de un switch, contiene secciones de case/default
switchBlock
    : switchSection* // Puede tener cero o más secciones
    ;

// Sección de un switch, consiste en una o más etiquetas y luego sentencias
switchSection
    : switchLabel+ statement* 
    ;

// Etiqueta de un switch (case o default)
switchLabel
    : CASE expr COLON
    | DEFAULT COLON
    ;

// Bloque de codigo 
block        : LBRACE (varDecl | statement)* RBRACE ;

// Parametros actuales en una llamada a metodo
actPars      : expr (COMMA expr)* ;

// Condicion logica (usada en if, while, for)
condition    : condTerm (OR condTerm)* ;

// Termino de una condicion
condTerm     : condFact (AND condFact)* ;

// Factor de una condicion (comparación)
condFact     : expr relop expr ;

// Expresion general
expr         : ADDOP? cast? term (ADDOP term)* ; 

// Operacion de casting de tipo
cast         : LPAREN type RPAREN ;

// Termino en una expresion 
term         : factor (MULOP factor)* ;

// Factor, la unidad mas pequeña en una expresion
factor
    : designator (LPAREN actPars? RPAREN)? 
    | INTLIT                               
    | DOUBLELIT                           
    | CHARLIT                              
    | STRINGLIT                           
    | TRUE                                 
    | FALSE                              
    | NULL                                 
    | NEW ID ( (LBRACK RBRACK)       
             | (LBRACK expr RBRACK)  
             )?                     
    | LPAREN expr RPAREN                   
    ;

// Designador (identificador, acceso a miembro de clase, o acceso a elemento de array)
designator   : ID (DOT ID | LBRACK expr RBRACK)* ;

// Operador relacional (token definido en el lexer)
relop        : RELOP ;
