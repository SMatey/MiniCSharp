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
USING       : 'using';
SWITCH      : 'switch';
CASE        : 'case';
DEFAULT     : 'default';

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
COLON       : ':';

// Literales
INTLIT      : '0' | [1-9][0-9]*;
DOUBLELIT   : [0-9]+ '.' [0-9]+;


CHARLIT     : '\'' CharContent '\'' ;

STRINGLIT   : '"' ( ~["\\\r\n] | '\\' . )*? '"'; 

// Identificadores
ID          : [a-zA-Z_] [a-zA-Z_0-9]*;

// Comentarios
COMMENT     : '/*' .*? '*/' -> channel(HIDDEN);
LINECOMMENT : '//' ~[\r\n]* -> channel(HIDDEN);

// Espacios ignorados
WS          : [ \t\r\n]+ -> skip;

// --- Fragment rules para CHARLIT ---
// Un fragmento define una parte de una regla léxica, no un token por sí mismo.

// Contenido de un literal de carácter
fragment CharContent
    : CharEscapeSequence 
    | ~['\\\r\n]       
    ;

// Secuencias de escape válidas dentro de un CHARLIT
fragment CharEscapeSequence
    : '\\' 
      ( 'b'             
      | 't'             
      | 'n'             
      | 'f'             
      | 'r'             
      | '"'             
      | '\''            
      | '\\'            
      | UnicodeSequence 
      )
    ;

// Secuencia de escape Unicode
fragment UnicodeSequence
    : 'u' HEX_DIGIT HEX_DIGIT HEX_DIGIT HEX_DIGIT
    ;

// Dígito hexadecimal
fragment HEX_DIGIT : [0-9a-fA-F] ;
