namespace Net7ClientManager.Forms;

internal sealed record HelpClientContext(
    string Key,
    string DisplayName,
    int? ProcessId = null,
    Guid? SlotId = null)
{
    public override string ToString() => this.DisplayName;
}

internal sealed record HelpActionRequest(
    string Action,
    HelpClientContext Context);

internal enum HelpResultKind
{
    Information,
    Success,
    Warning,
    Error,
}

internal sealed record HelpActionResponse(
    HelpResultKind Kind,
    string Title,
    string Message,
    string? ShowMeAction = null,
    string ShowMeText = "Show me");

internal sealed record HelpArticleHighlight(
    string Text,
    string? Action = null,
    string ActionText = "Show me")
{
    public static implicit operator HelpArticleHighlight(string text) =>
        new(text);
}

internal sealed record HelpArticleSection(
    string Title,
    string Body,
    string? Action = null,
    string ActionText = "Show me");

internal sealed record HelpArticle(
    string Id,
    string Title,
    string Kicker,
    string Summary,
    IReadOnlyList<HelpArticleHighlight> Highlights,
    IReadOnlyList<HelpArticleSection> Sections,
    IReadOnlyList<string> Keywords,
    bool Featured = false,
    string? DiagnosticAction = null,
    string DiagnosticText = "Check my setup",
    string? OpenAction = null,
    string OpenActionText = "Open this feature");
