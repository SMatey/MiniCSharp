parser grammar MiniCSharpParser;

options {
    tokenVocab=MiniCSharpLexer;
}

program      : CLASS ID LBRACE (varDecl | classDecl | methodDecl)* RBRACE ;

varDecl      : type ID (COMMA ID)* SEMICOLON ;

classDecl    : CLASS ID LBRACE varDecl* RBRACE ;

methodDecl   : (type | VOID) ID LPAREN formPars? RPAREN block ;

formPars     : type ID (COMMA type ID)* ;

type         : ID (LBRACK RBRACK)? ;

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
    | SEMICOLON
    ;

block        : LBRACE (varDecl | statement)* RBRACE ;

actPars      : expr (COMMA expr)* ;

condition    : condTerm (OR condTerm)* ;

condTerm     : condFact (AND condFact)* ;

condFact     : expr relop expr ;

expr         : ADDOP? cast? term (ADDOP term)* ;

cast         : LPAREN type RPAREN ;
term         : factor (MULOP factor)* ;


factor
    : designator (LPAREN actPars? RPAREN)?
    | INTLIT
    | DOUBLELIT
    | CHARLIT
    | STRINGLIT
    | TRUE
    | FALSE
    | NEW ID (LBRACK expr RBRACK)?
    | LPAREN expr RPAREN
    ;

designator   : ID (DOT ID | LBRACK expr RBRACK)* ;

relop        : RELOP ;
