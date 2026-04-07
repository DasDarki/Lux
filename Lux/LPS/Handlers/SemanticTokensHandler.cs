using Antlr4.Runtime;
using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Lux.LPS.Handlers;

public sealed class SemanticTokensHandler(LuxWorkspace workspace) : SemanticTokensHandlerBase
{
    private static readonly string[] TokenTypes =
        ["keyword", "string", "number", "comment", "variable", "function", "operator", "type"];

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

        result.TokenStream.Fill();
        var tokens = result.TokenStream.GetTokens();

        foreach (var token in tokens)
        {
            if (token.Type == -1) continue;
            if (token.Channel != 0 && token.Type != LuxLexer.LONG_COMMENT && token.Type != LuxLexer.LINE_COMMENT)
                continue;

            var typeIndex = ClassifyToken(token.Type);
            if (typeIndex < 0) continue;

            var line = token.Line - 1;
            var col = token.Column;
            var length = token.Text?.Length ?? 0;
            if (length <= 0) continue;

            builder.Push(line, col, length, typeIndex, 0);
        }

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams request, CancellationToken ct)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
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
                or LuxLexer.IMPORT or LuxLexer.MODULE
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
