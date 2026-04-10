namespace Lux.Diagnostics;

/// <summary>
/// The actual diagnotics of the compiler. Each diagnostic has a code that is used to identify the specific diagnostic.
/// This code is used to provide more information about the diagnostic, such as a description of the issue and potential fixes.
/// This enum should set specific enum values instead of relying on the default values, as the values are used to identify the specific diagnostic and should not change.
/// </summary>
public enum DiagnosticCode
{
    #region Internal

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Internal)]
    [Format("Preparsing did not return a valid HIR file")]
    ErrPreparsingFailed = -0x0001,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Internal)]
    [Format("Declaring symbol {0} in non-existing scope {1} is not allowed")]
    ErrDeclaringInNonExistingScope = -0x0002,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Internal)]
    [Format("Declaring non-existing symbol {0} ({1}) is not allowed")]
    ErrDeclaringNonExistingSymbol = -0x0003,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Internal)]
    [Format("Looking up symbol {0} in non-existing scope {1} is not allowed")]
    ErrLookingUpInNonExistingScope = -0x0004,

    #endregion

    #region Syntax

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Unexpected end of file")]
    ErrUnexpectedEOF = 0x0001,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Unexpected token: {0}")]
    ErrUnexpectedToken = 0x0002,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Undefined token at lexing: {0}")]
    ErrLexerUndefinedToken = 0x0005,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Invalid operator '{0}'")]
    ErrInvalidOperator = 0x0003,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Invalid literal '{0}', expected {1}")]
    ErrInvalidLiteral = 0x0004,

    #endregion
    
    #region Semantic
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Symbol '{0}' is already declared in this scope ({1})")]
    ErrRedeclaration = 0x1001,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Symbol '{0}' is not declared in this scope")]
    ErrUndeclaredSymbol = 0x1002,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Top-level cycles are not allowed, but '{0}' is part of a cycle")]
    ErrTopLevelCycle = 0x1003,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Control flow depth invalid for {0}; it must be a non-negative (0 excluded) integer")]
    ErrInvalidControlFlowDepth = 0x1004,
    
    [Level(DiagnosticLevel.Warning)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Code is unreachable and will never be executed")]
    WrnUnreachableCode = 0x1005,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("String interpolation is disabled in the configuration. Enable [code] string_interpolation = true to use backtick strings.")]
    ErrStringInterpolationDisabled = 0x1006,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Syntax)]
    [Format("Alternative boolean operator '{0}' is disabled in the configuration. Enable [code] alt_boolean_operators = true to use it, or use '{1}' instead.")]
    ErrAltBooleanOperatorsDisabled = 0x0006,

    #endregion

    #region Type
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Type mismatch: expected '{0}', but got '{1}'")]
    ErrTypeMismatch = 0x2001,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Type '{0}' is not indexable")]
    ErrTypeNotIndexable = 0x2002,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Unknown type '{0}'")]
    ErrUnknownType = 0x2003,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Function parameter count mismatch: expected {0}, but got {1}")]
    ErrFuncParamMismatch = 0x2004,
    
    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Type inference failed for expression of type '{0}'")]
    ErrTypeInferenceFailed = 0x2005,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Expression of type '{0}' is possibly nil. Use '?.' to access fields safely or check for nil first.")]
    ErrPossiblyNil = 0x2006,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Non-exhaustive match on type '{0}': missing case(s) for {1}. Handle the missing case(s) explicitly.")]
    ErrNonExhaustiveMatch = 0x2007,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Type)]
    [Format("Invalid assignment target. Only variables, fields, and index expressions can be assigned to.")]
    ErrInvalidAssignTarget = 0x2008,

    #endregion

    #region Module

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Module '{0}' could not be found")]
    ErrModuleNotFound = 0x3001,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Symbol '{0}' is not exported from module '{1}'")]
    ErrSymbolNotExported = 0x3002,

    [Level(DiagnosticLevel.Error)]
    [Category(DiagnosticCategory.Semantic)]
    [Format("Symbol '{0}' does not exist in module '{1}'")]
    ErrSymbolNotFound = 0x3003,

    #endregion
}