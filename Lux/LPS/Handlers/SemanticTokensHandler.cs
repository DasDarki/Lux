using Antlr4.Runtime;
using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LuxSymbolKind = Lux.IR.SymbolKind;

namespace Lux.LPS.Handlers;

public sealed class SemanticTokensHandler(LuxWorkspace workspace) : SemanticTokensHandlerBase
{
    private const int TK_KEYWORD = 0;
    private const int TK_STRING = 1;
    private const int TK_NUMBER = 2;
    private const int TK_COMMENT = 3;
    private const int TK_VARIABLE = 4;
    private const int TK_FUNCTION = 5;
    private const int TK_OPERATOR = 6;
    private const int TK_TYPE = 7;
    private const int TK_ENUM_MEMBER = 8;

    private static readonly string[] TokenTypes =
        ["keyword", "string", "number", "comment", "variable", "function", "operator", "type", "enumMember"];

    private static readonly string[] TokenModifiers = ["declaration", "readonly"];

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux"),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(
                    TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>(
                    TokenModifiers.Select(m => new SemanticTokenModifier(m)))
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = true
        };
    }

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams request,
        CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.CompletedTask;

        var semanticOverrides = BuildSemanticOverrides(result);

        result.TokenStream.Fill();
        var tokens = result.TokenStream.GetTokens();

        foreach (var token in tokens)
        {
            if (token.Type == -1) continue;
            if (token.Channel != 0 && token.Type != LuxLexer.LONG_COMMENT && token.Type != LuxLexer.LINE_COMMENT)
                continue;

            var line = token.Line - 1;
            var col = token.Column;
            var length = token.Text?.Length ?? 0;
            if (length <= 0) continue;

            var key = (line, col);
            if (semanticOverrides.TryGetValue(key, out var semType))
            {
                builder.Push(line, col, length, semType, 0);
                continue;
            }

            var typeIndex = ClassifyToken(token.Type);
            if (typeIndex < 0) continue;

            builder.Push(line, col, length, typeIndex, 0);
        }

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams request, CancellationToken ct)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
    }

    private static Dictionary<(int Line, int Col), int> BuildSemanticOverrides(AnalysisResult result)
    {
        var overrides = new Dictionary<(int, int), int>();
        var nameRefs = NodeFinder.CollectAllNameRefs(result.Hir);
        foreach (var nr in nameRefs)
        {
            if (nr.Sym == SymID.Invalid) continue;
            if (!result.Syms.GetByID(nr.Sym, out var sym)) continue;

            var tokenType = sym.Kind switch
            {
                LuxSymbolKind.Function => TK_FUNCTION,
                LuxSymbolKind.Enum => TK_TYPE,
                _ => -1
            };

            if (tokenType < 0) continue;

            var line = nr.Span.StartLn - 1;
            var col = nr.Span.StartCol - 1;
            overrides[(line, col)] = tokenType;
        }

        CollectEnumMemberTokens(result.Hir.Body, overrides);

        return overrides;
    }

    private static void CollectEnumMemberTokens(List<Stmt> stmts, Dictionary<(int, int), int> overrides)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is EnumDecl ed)
            {
                foreach (var m in ed.Members)
                {
                    var line = m.Name.Span.StartLn - 1;
                    var col = m.Name.Span.StartCol - 1;
                    overrides[(line, col)] = TK_ENUM_MEMBER;
                }
            }
            else if (stmt is ExportStmt exp)
            {
                CollectEnumMemberTokens([exp.Declaration], overrides);
            }
        }
    }

    private static int ClassifyToken(int type)
    {
        return type switch
        {
            LuxLexer.AND or LuxLexer.BREAK or LuxLexer.DO or LuxLexer.ELSE or LuxLexer.ELSEIF
                or LuxLexer.END or LuxLexer.FALSE or LuxLexer.FOR or LuxLexer.FUNCTION
                or LuxLexer.GOTO or LuxLexer.IF or LuxLexer.IN or LuxLexer.LOCAL or LuxLexer.NIL
                or LuxLexer.NOT or LuxLexer.OR or LuxLexer.REPEAT or LuxLexer.RETURN
                or LuxLexer.THEN or LuxLexer.TRUE or LuxLexer.UNTIL or LuxLexer.WHILE
                or LuxLexer.AS or LuxLexer.DECLARE or LuxLexer.EXPORT or LuxLexer.FROM
                or LuxLexer.ENUM or LuxLexer.IMPORT or LuxLexer.MODULE
                => 0, // keyword

            LuxLexer.NORMAL_STRING or LuxLexer.CHAR_STRING or LuxLexer.LONG_STRING
                => 1, // string

            LuxLexer.INT or LuxLexer.HEX or LuxLexer.FLOAT or LuxLexer.HEX_FLOAT
                => 2, // number

            LuxLexer.LONG_COMMENT or LuxLexer.LINE_COMMENT
                => 3, // comment

            LuxLexer.NAME => 4, // variable (default for identifiers)

            _ => -1
        };
    }
}
