namespace Net7ClientManager.Addons.Development;

public enum AddonApiSymbolKind
{
    Keyword,
    Function,
    Table,
    Field,
    Event,
}

public sealed record AddonApiParameter
{
    public required string Name { get; init; }

    public string Type { get; init; } = "any";

    public string Description { get; init; } = "";

    public bool Optional { get; init; }
}

public sealed record AddonApiSymbol
{
    public required string Path { get; init; }

    public required AddonApiSymbolKind Kind { get; init; }

    public string Signature { get; init; } = "";

    public string ReturnType { get; init; } = "";

    public string Description { get; init; } = "";

    public IReadOnlyList<AddonApiParameter> Parameters { get; init; } = [];

    public string ParentPath => this.Path.Contains('.')
        ? this.Path[..this.Path.LastIndexOf('.')]
        : "";

    public string Name => this.Path.Contains('.')
        ? this.Path[(this.Path.LastIndexOf('.') + 1)..]
        : this.Path;
}
