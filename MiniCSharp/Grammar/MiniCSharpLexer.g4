lexer grammar MiniCSharpLexer;

// Keywords
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

// simbolos
ASSIGN      : '=';
ADDOP       : '+' | '-';
MULOP       : '*' | '/' | '%';
RELOP       : '==' | '!=' | '<=' | '>=' | '<' | '>';
AND         : '&&';
OR          : '||';
INCREMENT   : '++';
DECREMENT   : '--';
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

// ID
ID          : [a-zA-Z_] [a-zA-Z_0-9]*;

// comentarios
COMMENT     : '/*' .*? '*/' -> channel(HIDDEN);
LINECOMMENT : '//' ~[\r\n]* -> channel(HIDDEN);

// Espacios ignorados
WS          : [ \t\r\n]+ -> skip;

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
