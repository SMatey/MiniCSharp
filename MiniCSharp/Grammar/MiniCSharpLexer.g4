lexer grammar MiniCSharpLexer;

// Palabras clave
CLASS       : 'class';
VOID        : 'void';
IF          : 'if';
ELSE        : 'else';
FOR         : 'for';
WHILE       : 'while';
BREAK       : 'break';
RETURN      : 'return';
READ        : 'read';
WRITE       : 'write';
NEW         : 'new';
TRUE        : 'true';
FALSE       : 'false';
NULL        : 'null';

// Operadores
ASSIGN      : '=';
ADDOP       : '+' | '-';
MULOP       : '*' | '/' | '%';
RELOP       : '==' | '!=' | '<=' | '>=' | '<' | '>';
AND         : '&&';
OR          : '||';
INCREMENT   : '++';
DECREMENT   : '--';

// Delimitadores
LPAREN      : '(';
RPAREN      : ')';
LBRACE      : '{';
RBRACE      : '}';
LBRACK      : '[';
RBRACK      : ']';
SEMICOLON   : ';';
COMMA       : ',';
DOT         : '.';

// Literales
INTLIT      : '0' | [1-9][0-9]*;
DOUBLELIT   : [0-9]+ '.' [0-9]+;
CHARLIT     : '\'' ( ~[\r\n'] | '\\\'') '\'';
STRINGLIT   : '"' ( ~["\\\r\n] | '\\' . )* '"';

// Identificadores
ID          : [a-zA-Z_] [a-zA-Z_0-9]*;

// Comentarios
COMMENT     : '/*' .*? '*/' -> channel(HIDDEN);
LINECOMMENT : '//' ~[\r\n]* -> channel(HIDDEN);

// Espacios ignorados
WS          : [ \t\r\n]+ -> skip;
