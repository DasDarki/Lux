// =============================================================================
// Lux Grammar – Lua Base + Type System + Modules + Declarations
// =============================================================================
//
// Complete Lua 5.1–5.4 + LuaJIT grammar with optional type annotations,
// an import/export module system, and declare statements for .d.lux files.
//
// Type system:
//   - Primitive types via NAME (string, number, boolean, any, void, ...)
//   - Nullable:  string?  or  string | nil
//   - Unions:    string | number
//   - Arrays:    number[], string[][]
//   - Maps:      { [string]: number }
//   - Structs:   { x: number, y: string }
//   - Functions: (number, string) -> boolean
//   - Tuples:    (number, string)  (for multi-return)
//   - Grouping:  (string | number)[]
//   - Varargs:   ...: number
//   - All annotations are optional
//
// Module system:
//   - Named:      import { A, B } from "module"
//   - Default:    import A from "module"
//   - Namespace:  import * as utils from "module"
//   - Side-effect: import "module"
//   - Aliased:    import { A as B } from "module"
//   - Export:     export function / export local
//
// Declarations (.d.lux):
//   - declare function name(params): retType
//   - declare name: type
//   - declare module "name" ... end
//
// Operator precedence (low → high):
//   or → and → comparison → | → ~ → & → shift → .. → add → mul → unary → ^
//
// =============================================================================

grammar Lux;

// =============================================================================
//  PARSER RULES
// =============================================================================

// ─────────────────────────────────────────────────────────────────────────────
//  Program Structure
// ─────────────────────────────────────────────────────────────────────────────

script
    : block EOF
    ;

block
    : stmt* returnStat?
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Statements
// ─────────────────────────────────────────────────────────────────────────────

stmt
    : ';'                                                       # EmptyStat
    | varList ASSIGN exprList                                   # AssignStat
    | functionCall                                              # FunctionCallStat
    | incDecStat                                                # IncDecStat_
    | label                                                     # LabelStat
    | BREAK                                                     # BreakStat
    | GOTO NAME                                                 # GotoStat
    | doBlock                                                   # DoStat
    | whileLoop                                                 # WhileStat
    | repeatLoop                                                # RepeatStat
    | ifStat                                                    # IfStat_
    | numericFor                                                # NumericForStat
    | genericFor                                                # GenericForStat
    | functionDecl                                              # FunctionDeclStat
    | localFunctionDecl                                         # LocalFunctionDeclStat
    | localDecl                                                 # LocalDeclStat
    | enumDecl                                                  # EnumDeclStat
    | importStat                                                # ImportStat_
    | exportStat                                                # ExportStat_
    | declareStat                                               # DeclareStat_
    | matchStat                                                 # MatchStat_
    ;

// ─── Block Statements ───

doBlock
    : DO block END
    ;

whileLoop
    : WHILE expr DO block END
    ;

repeatLoop
    : REPEAT block UNTIL expr
    ;

// ─── If / ElseIf / Else ───

ifStat
    : IF expr THEN block
      elseIfClause*
      elseClause?
      END
    ;

elseIfClause
    : ELSEIF expr THEN block
    ;

elseClause
    : ELSE block
    ;

// ─── For Loops ───

numericFor
    : FOR NAME ASSIGN expr COMMA expr (COMMA expr)? DO block END
    ;

genericFor
    : FOR nameList IN exprList DO block END
    ;

// ─── Labels & Goto ───

label
    : DCOLON NAME DCOLON
    ;

// ─── Increment / Decrement Statements ───
// Examples:
//   x++
//   ++x
//   arr[i]~~
//   ~~obj.field

incDecStat
    : var INC                                                   # PostIncStat
    | var DEC                                                   # PostDecStat
    | INC var                                                   # PreIncStat
    | DEC var                                                   # PreDecStat
    ;

// ─── Return ───

returnStat
    : RETURN exprList? SEMI?
    ;

// ─── Function Declarations ───

functionDecl
    : FUNCTION funcName funcBody
    ;

localFunctionDecl
    : LOCAL FUNCTION NAME funcBody
    ;

// ─── Local Declaration ───
// Examples:
//   local x = 5
//   local x: number = 5
//   local a: number, b: string = 1, "hello"
//   local x <const>: number = 42

localDecl
    : LOCAL MUT? attribNameList (ASSIGN exprList)?
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Import Statements
// ─────────────────────────────────────────────────────────────────────────────
//
// Examples:
//   import { Vector2, Rect } from "engine/math"
//   import { Vector2 as Vec2 } from "engine/math"
//   import Player from "entities/player"
//   import * as utils from "lib/utils"
//   import "polyfill"

importStat
    : IMPORT importBody FROM str                                # ImportFrom
    | IMPORT str                                                # ImportSideEffect
    ;

importBody
    : LBRACE importName (COMMA importName)* COMMA? RBRACE       # NamedImport
    | NAME                                                       # DefaultImport
    | STAR AS NAME                                               # NamespaceImport
    ;

importName
    : NAME (AS NAME)?
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Export Statements
// ─────────────────────────────────────────────────────────────────────────────
//
// Export wraps existing declaration forms. Semantic analysis enforces
// that exports only appear at the top level of a module.
//
// Examples:
//   export function foo(x: number): string ... end
//   export local name: string = "hello"
//   export local function bar() ... end

// ─── Enum Declarations ───

enumDecl
    : ENUM NAME enumMember+ END
    ;

enumMember
    : NAME (ASSIGN expr)?
    ;

exportStat
    : EXPORT functionDecl                                        # ExportFunction
    | EXPORT localFunctionDecl                                   # ExportLocalFunction
    | EXPORT localDecl                                           # ExportLocal
    | EXPORT enumDecl                                            # ExportEnum
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Match / Pattern Matching
// ─────────────────────────────────────────────────────────────────────────────

matchStat
    : MATCH expr matchArm+ END
    ;

matchExpr
    : MATCH expr matchExprArm+ END
    ;

matchArm
    : CASE matchPattern (WHEN expr)? THEN block
    ;

matchExprArm
    : CASE matchPattern (WHEN expr)? THEN expr
    ;

matchPattern
    : NAME typeAnnotation                                        # BindingPattern
    | expr                                                       # ValuePattern
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Declare Statements (.d.lux type definition files)
// ─────────────────────────────────────────────────────────────────────────────
//
// Type-only declarations for existing Lua code / libraries.
// No bodies, no values – just signatures and types.
//
// Examples:
//   declare function print(msg: string): void
//   declare function type(v: any): string
//   declare tostring: (any) -> string
//   declare math: { abs: (number) -> number, pi: number }
//
//   declare module "socket"
//       function tcp(): Socket
//       function udp(): Socket
//       gettime: () -> number
//   end

declareStat
    : DECLARE declareBody
    ;

declareBody
    : FUNCTION funcName funcSignature                            # DeclareFunction
    | NAME typeAnnotation                                        # DeclareVariable
    | MODULE str declareModuleBlock END                           # DeclareModule
    | ENUM NAME declareEnumMember+ END                           # DeclareEnum
    ;

// Function signature: params + optional return type. No body.
// Used in declare and inside declare module blocks.
// Examples:
//   (x: number, y: number): Vector2
//   (msg: string, ...: any): void
//   ()

funcSignature
    : LPAREN paramList? RPAREN typeAnnotation?
    ;

// ─── Declare Module Block ───
// Contains function signatures and typed variable declarations.
// No executable code, no values.
//
// Examples:
//   declare module "math"
//       function abs(x: number): number
//       function sqrt(x: number): number
//       pi: number
//       huge: number
//       maxinteger: number
//   end

declareModuleBlock
    : declareModuleMember*
    ;

declareEnumMember
    : NAME typeAnnotation?
    ;

declareModuleMember
    : FUNCTION funcName funcSignature                            # ModuleDeclareFunction
    | NAME typeAnnotation                                        # ModuleDeclareVariable
    | ENUM NAME declareEnumMember+ END                           # ModuleDeclareEnum
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Function Components
// ─────────────────────────────────────────────────────────────────────────────

funcName
    : NAME (DOT NAME)* (COLON NAME)?
    ;

// Function body with optional return type after closing paren.
// Examples:
//   function foo(x: number): string ... end
//   function bar(a, b) ... end
//   function baz(): (number, string) ... end   -- multi-return

funcBody
    : LPAREN paramList? RPAREN typeAnnotation? block END
    ;

// ─── Parameter List ───
// Supports optional type annotations on each param and on varargs.
// Examples:
//   (x: number, y: string)
//   (name, value)
//   (fmt: string, ...: any)
//   (...)

paramList
    : param (COMMA param)* (COMMA varargParam)?                 # ParamListWithNames
    | varargParam                                               # ParamListVararg
    ;

param
    : NAME typeAnnotation? (ASSIGN expr)?
    ;

varargParam
    : ELLIPSIS NAME? typeAnnotation?
    ;

// ─── Name / Variable Lists ───

varList
    : var (COMMA var)*
    ;

nameList
    : NAME (COMMA NAME)*
    ;

// ─── Lua 5.4 Attributes with optional type ───
// Examples:
//   x
//   x: number
//   x <const>
//   x <const>: number

attribNameList
    : attribName (COMMA attribName)*
    ;

attribName
    : NAME attrib? typeAnnotation?
    ;

attrib
    : LT NAME GT
    ;

// ─── Expression Lists ───

exprList
    : expr (COMMA expr)*
    ;


// ─────────────────────────────────────────────────────────────────────────────
//  Type Annotations
// ─────────────────────────────────────────────────────────────────────────────

typeAnnotation
    : COLON typeExpr
    ;


// ─────────────────────────────────────────────────────────────────────────────
//  Type Expressions
// ─────────────────────────────────────────────────────────────────────────────
//
// Precedence (low → high):
//   union |  →  suffixes [] ?  →  atoms
//
// Examples showing the full range:
//
//   number                              named type (primitive or custom)
//   nil                                 nil type
//   string?                             nullable   (sugar for string | nil)
//   string | number                     union
//   string | nil                         union with nil
//   number[]                            array of numbers
//   number[][]                          nested array
//   number[]?                           nullable array
//   string?[]                           array of nullable strings
//   string?[]?                          nullable array of nullable strings
//   (string | number)[]                 array of union (via grouping)
//   { x: number, y: number }            struct type
//   { [string]: number }                map type
//   (number, string) -> boolean         function type
//   () -> void                          no-param function
//   (number) -> (string, boolean)       function with multi-return
//   (string, number)                    tuple (used for multi-return)

typeExpr
    : typeSingle (PIPE typeSingle)*                             # UnionType
    ;

// Single type with zero or more postfix suffixes.
// Suffixes are applied left-to-right, so:
//   string?[]   = (string?) followed by [] = array of nullable string
//   number[]?   = (number[]) followed by ? = nullable array of number

typeSingle
    : typeAtom typeSuffix*                                      # PostfixType
    ;

typeSuffix
    : LBRACK RBRACK                                             # ArraySuffix
    | QMARK                                                     # NullableSuffix
    ;

// ─── Atomic Types ───

typeAtom
    : NIL                                                       # NilType
    | NAME                                                      # NamedType
    | functionType                                              # FuncType
    | tableType                                                 # TableType_
    | LPAREN typeExpr (COMMA typeExpr)* RPAREN                  # GroupedOrTupleType
    ;

// ─── Function Types ───
// The param list contains bare types (no names).
// Return type is a full typeExpr, so multi-return uses tuple grouping.
// Examples:
//   () -> void
//   (number) -> string
//   (number, string) -> boolean
//   () -> (number, string)             -- returns tuple

functionType
    : LPAREN typeList? RPAREN ARROW typeExpr
    ;

typeList
    : typeExpr (COMMA typeExpr)*
    ;

// ─── Table Types ───
// Three distinct forms, disambiguated by first tokens after '{':
//   RBRACE           → empty table
//   LBRACK           → map type     { [K]: V }
//   NAME COLON       → struct type  { field: T, ... }

tableType
    : LBRACE RBRACE                                             # EmptyTableType
    | LBRACE LBRACK typeExpr RBRACK COLON typeExpr RBRACE       # MapType
    | LBRACE structField (COMMA structField)* COMMA? RBRACE     # StructType
    ;

structField
    : META? NAME COLON typeExpr
    ;


// ─────────────────────────────────────────────────────────────────────────────
//  Expressions
// ─────────────────────────────────────────────────────────────────────────────
//
// Precedence by alternative order (ANTLR4: earlier alternative = higher precedence / tighter binding).

expr
    : NIL                                                       # NilLiteral
    | TRUE                                                      # TrueLiteral
    | FALSE                                                     # FalseLiteral
    | number                                                    # NumberLiteral
    | str                                                       # StringLiteral
    | ELLIPSIS                                                  # VarargExpr
    | functionDef                                               # FunctionDefExpr
    | prefixExp                                                 # PrefixExpr
    | tableConstructor                                          # TableConstructorExpr
    | matchExpr                                                 # MatchExprExpr

    // ─── Power (tightest, right-associative) ───

    | <assoc=right> expr CARET expr                             # PowerExpr

    // ─── Unary prefix ───

    | unaryOp expr                                              # UnaryExpr
    | BANG expr                                                 # AltLogicalNotExpr
    | INC expr                                                  # PreIncExpr
    | DEC expr                                                  # PreDecExpr

    // ─── Postfix ───

    | expr BANG                                                 # NonNilAssertExpr
    | expr INC                                                  # PostIncExpr
    | expr DEC                                                  # PostDecExpr

    // ─── Binary Operators (high → low precedence) ───

    | expr multiplicativeOp expr                                # MultiplicativeExpr
    | expr additiveOp expr                                      # AdditiveExpr
    | <assoc=right> expr CONCAT expr                            # ConcatExpr
    | expr shiftOp expr                                         # BitShiftExpr
    | expr AMP expr                                             # BitwiseAndExpr
    | expr TILDE expr                                           # BitwiseXorExpr
    | expr PIPE expr                                            # BitwiseOrExpr

    // ─── Type check / cast (tighter than comparison, looser than bit ops) ───

    | expr IS typeExpr                                          # TypeCheckExpr
    | expr AS typeExpr                                          # TypeCastExpr

    | expr compareOp expr                                       # ComparisonExpr
    | expr AND expr                                             # LogicalAndExpr
    | expr ANDAND expr                                          # AltLogicalAndExpr
    | <assoc=right> expr QQ expr                                # NilCoalesceExpr
    | expr OR expr                                              # LogicalOrExpr
    | expr OROR expr                                            # AltLogicalOrExpr
    ;

// ─── Operator Groups ───

compareOp
    : LT                                                        # LtOp
    | GT                                                        # GtOp
    | LTE                                                       # LteOp
    | GTE                                                       # GteOp
    | NEQ                                                       # NeqOp
    | BANGEQ                                                    # AltNeqOp
    | EQ                                                        # EqOp
    ;

shiftOp
    : LSHIFT                                                    # LshiftOp
    | RSHIFT                                                    # RshiftOp
    ;

additiveOp
    : PLUS                                                      # AddOp
    | MINUS                                                     # SubOp
    ;

multiplicativeOp
    : STAR                                                      # MulOp
    | SLASH                                                     # DivOp
    | DSLASH                                                    # FloorDivOp
    | PERCENT                                                   # ModOp
    ;

unaryOp
    : NOT                                                       # LogicalNotOp
    | HASH                                                      # LengthOp
    | MINUS                                                     # NegateOp
    | TILDE                                                     # BitwiseNotOp
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Prefix Expressions
// ─────────────────────────────────────────────────────────────────────────────

prefixExp
    : varOrExp suffix*
    ;

varOrExp
    : NAME                                                      # NameVarOrExp
    | LPAREN expr RPAREN                                        # ParenVarOrExp
    ;

suffix
    : DOT NAME                                                  # DotSuffix
    | QDOT NAME                                                 # OptDotSuffix
    | LBRACK expr RBRACK                                        # IndexSuffix
    | COLON NAME args                                           # MethodCallSuffix
    | args                                                      # CallSuffix
    | QMARK args                                                # OptCallSuffix
    ;

// ─── Variables (assignment targets) ───

var
    : NAME                                                      # NameVar
    | varOrExp suffix* DOT NAME                                 # FieldVar
    | varOrExp suffix* LBRACK expr RBRACK                       # IndexVar
    ;

// ─── Function Calls ───

functionCall
    : varOrExp suffix* args                                     # DirectCall
    | varOrExp suffix* COLON NAME args                          # MethodCall
    ;

// ─── Call Arguments ───

args
    : LPAREN exprList? RPAREN                                   # ParenArgs
    | tableConstructor                                          # TableArgs
    | str                                                       # StringArgs
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Function Definitions (anonymous)
// ─────────────────────────────────────────────────────────────────────────────

functionDef
    : FUNCTION funcBody
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Table Constructors
// ─────────────────────────────────────────────────────────────────────────────

tableConstructor
    : LBRACE fieldList? RBRACE
    ;

fieldList
    : field (fieldSep field)* fieldSep?
    ;

field
    : LBRACK expr RBRACK ASSIGN expr                            # BracketField
    | NAME ASSIGN expr                                          # NameField
    | expr                                                      # ValueField
    ;

fieldSep
    : COMMA
    | SEMI
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Literal Helpers
// ─────────────────────────────────────────────────────────────────────────────

number
    : INT                                                       # IntLit
    | HEX                                                       # HexLit
    | FLOAT                                                     # FloatLit
    | HEX_FLOAT                                                 # HexFloatLit
    ;

str
    : NORMAL_STRING                                             # DoubleQuotedStr
    | CHAR_STRING                                               # SingleQuotedStr
    | LONG_STRING                                               # LongStr
    | INTERP_STRING                                             # InterpolatedStr
    ;


// =============================================================================
//  LEXER RULES
// =============================================================================

// ─────────────────────────────────────────────────────────────────────────────
//  Keywords
// ─────────────────────────────────────────────────────────────────────────────

AND      : 'and';
BREAK    : 'break';
DO       : 'do';
ELSE     : 'else';
ELSEIF   : 'elseif';
END      : 'end';
FALSE    : 'false';
FOR      : 'for';
FUNCTION : 'function';
GOTO     : 'goto';
IF       : 'if';
IN       : 'in';
LOCAL    : 'local';
NIL      : 'nil';
NOT      : 'not';
OR       : 'or';
REPEAT   : 'repeat';
RETURN   : 'return';
THEN     : 'then';
TRUE     : 'true';
UNTIL    : 'until';
WHILE    : 'while';

// ─── Lux Keywords ───

AS       : 'as';
DECLARE  : 'declare';
ENUM     : 'enum';
EXPORT   : 'export';
FROM     : 'from';
IMPORT   : 'import';
IS       : 'is';
CASE     : 'case';
MATCH    : 'match';
META     : 'meta';
MODULE   : 'module';
MUT      : 'mut';
WHEN     : 'when';

// ─────────────────────────────────────────────────────────────────────────────
//  Operators & Punctuation
// ─────────────────────────────────────────────────────────────────────────────

// Multi-character (longest match first)

ELLIPSIS : '...';
CONCAT   : '..';
DCOLON   : '::';
ARROW    : '->';
LSHIFT   : '<<';
RSHIFT   : '>>';
DSLASH   : '//';
EQ       : '==';
NEQ      : '~=';
LTE      : '<=';
GTE      : '>=';
QQ       : '??';
QDOT     : '?.';
INC      : '++';
DEC      : '~~';
ANDAND   : '&&';
OROR     : '||';
BANGEQ   : '!=';

// Single-character

PLUS     : '+';
MINUS    : '-';
STAR     : '*';
SLASH    : '/';
PERCENT  : '%';
CARET    : '^';
HASH     : '#';
AMP      : '&';
TILDE    : '~';
PIPE     : '|';
LT       : '<';
GT       : '>';
ASSIGN   : '=';
QMARK    : '?';
BANG     : '!';

// Delimiters

LPAREN   : '(';
RPAREN   : ')';
LBRACE   : '{';
RBRACE   : '}';
LBRACK   : '[';
RBRACK   : ']';
SEMI     : ';';
COLON    : ':';
COMMA    : ',';
DOT      : '.';

// ─────────────────────────────────────────────────────────────────────────────
//  String Literals
// ─────────────────────────────────────────────────────────────────────────────

NORMAL_STRING
    : '"' (EscapeSequence | ~["\\\r\n])* '"'
    ;

CHAR_STRING
    : '\'' (EscapeSequence | ~['\\\r\n])* '\''
    ;

LONG_STRING
    : LONG_BRACKET_OPEN .*? LONG_BRACKET_CLOSE
    ;

INTERP_STRING
    : '`' (InterpEscape | InterpBraceGroup | ~[`\\])* '`'
    ;

fragment InterpEscape
    : '\\' [`{}abfnrtvz"'\\]
    ;

fragment InterpBraceGroup
    : '{' (InterpBraceGroup | ~[{}])* '}'
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Numeric Literals
// ─────────────────────────────────────────────────────────────────────────────

INT
    : Digit+
    ;

HEX
    : '0' [xX] HexDigit+
    ;

FLOAT
    : Digit+ '.' Digit* ExponentPart?
    | '.' Digit+ ExponentPart?
    | Digit+ ExponentPart
    ;

HEX_FLOAT
    : '0' [xX] HexDigit+ '.' HexDigit* HexExponentPart?
    | '0' [xX] '.' HexDigit+ HexExponentPart?
    | '0' [xX] HexDigit+ HexExponentPart
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Identifiers
// ─────────────────────────────────────────────────────────────────────────────

NAME
    : [a-zA-Z_] [a-zA-Z_0-9]*
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Comments & Whitespace
// ─────────────────────────────────────────────────────────────────────────────

LONG_COMMENT
    : '--' LONG_BRACKET_OPEN .*? LONG_BRACKET_CLOSE -> channel(HIDDEN)
    ;

LINE_COMMENT
    : '--' ~[\r\n]* -> channel(HIDDEN)
    ;

WS
    : [ \t\r\n\u000C]+ -> skip
    ;

SHEBANG
    : '#!' ~[\r\n]* -> channel(HIDDEN)
    ;

// ─────────────────────────────────────────────────────────────────────────────
//  Lexer Fragments
// ─────────────────────────────────────────────────────────────────────────────

fragment Digit
    : [0-9]
    ;

fragment HexDigit
    : [0-9a-fA-F]
    ;

fragment ExponentPart
    : [eE] [+-]? Digit+
    ;

fragment HexExponentPart
    : [pP] [+-]? Digit+
    ;

fragment EscapeSequence
    : '\\' [abfnrtvz"'\\]
    | '\\' '\r'? '\n'
    | DecimalEscape
    | HexEscape
    | UnicodeEscape
    ;

fragment DecimalEscape
    : '\\' Digit
    | '\\' Digit Digit
    | '\\' [0-2] Digit Digit
    ;

fragment HexEscape
    : '\\' 'x' HexDigit HexDigit
    ;

fragment UnicodeEscape
    : '\\' 'u' '{' HexDigit+ '}'
    ;

fragment LONG_BRACKET_OPEN
    : '[' EQUALS '['
    ;

fragment LONG_BRACKET_CLOSE
    : ']' EQUALS ']'
    ;

fragment EQUALS
    : '='*
    ;
