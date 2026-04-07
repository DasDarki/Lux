using Lux.IR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Lux.LPS.Handlers;

public sealed class DefinitionHandler(LuxWorkspace workspace) : DefinitionHandlerBase
{
    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var result = workspace.GetResult(request.TextDocument.Uri.ToString());
        if (result == null) return Task.FromResult<LocationOrLocationLinks?>(null);

        var line = request.Position.Line + 1;
        var col = request.Position.Character + 1;

        var nameRef = NodeFinder.FindNameRef(result.Hir, line, col);
        if (nameRef == null || nameRef.Sym == SymID.Invalid)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        if (!result.Syms.GetByID(nameRef.Sym, out var sym))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        if (sym.DeclaringNode == NodeID.Invalid)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        if (!result.NodeRegistry.TryGetValue(sym.DeclaringNode, out var declNode))
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var location = new Location
        {
            Uri = DocumentUri.Parse(result.Uri),
            Range = LuxWorkspace.SpanToRange(declNode.Span)
        };

        return Task.FromResult<LocationOrLocationLinks?>(new LocationOrLocationLinks(location));
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability, ClientCapabilities clientCapabilities)
    {
        return new DefinitionRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("lux")
        };
    }
}
