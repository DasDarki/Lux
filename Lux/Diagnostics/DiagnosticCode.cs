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

    #endregion
}