// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using System.Net;
using System.Text;

internal static class HelpHtmlRenderer
{
    public static string Render(
        HelpArticle article,
        IReadOnlyList<HelpArticle> allArticles,
        HelpClientContext context,
        HelpActionResponse? response)
    {
        var body = new StringBuilder();

        body.Append("<main>");
        body.Append("<section class=\"hero\">");

        if (article.Id != HelpTopicIds.Home)
        {
            body.Append("<button class=\"back-button\" data-action=\"topic\" data-value=\"home\">← Back to Help &amp; Assistance</button>");
        }

        body.Append("<div class=\"kicker\">");
        body.Append(Encode(article.Kicker));
        body.Append("</div><h1>");
        body.Append(Encode(article.Title));
        body.Append("</h1><p class=\"summary\">");
        body.Append(Encode(article.Summary));
        body.Append("</p>");

        body.Append("</section>");

        if (article.Id == HelpTopicIds.Home)
        {
            RenderHome(body, allArticles);
        }
        else
        {
            RenderArticle(body, article, response);
        }

        body.Append("</main>");

        return string.Concat(
            "<!doctype html><html><head><meta charset=\"utf-8\">",
            "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">",
            "<style>",
            Css,
            "</style></head><body>",
            body,
            "<script>",
            Script,
            "</script></body></html>");
    }

    private static void RenderHome(
        StringBuilder body,
        IReadOnlyList<HelpArticle> allArticles)
    {
        body.Append("<section class=\"section\"><div class=\"section-heading\">");
        body.Append("<div><h2>Start with what you want to do</h2>");
        body.Append("<p>Client Manager is more than a launcher. Pick a destination below and Help will take you straight to the useful part.</p></div>");
        body.Append("</div><div class=\"cards\">");

        foreach (var feature in allArticles.Where(candidate => candidate.Featured))
        {
            body.Append("<button class=\"feature-card\" data-action=\"topic\" data-value=\"");
            body.Append(Attribute(feature.Id));
            body.Append("\"><span class=\"feature-kicker\">");
            body.Append(Encode(feature.Kicker));
            body.Append("</span><strong>");
            body.Append(Encode(feature.Title));
            body.Append("</strong><span>");
            body.Append(Encode(feature.Summary));
            body.Append("</span><em>Explore →</em></button>");
        }

        body.Append("</div></section>");
        body.Append("<section class=\"section assistance\"><div><div class=\"kicker\">ASSISTANCE</div>");
        body.Append("<h2>Something is not working?</h2><p>Start with automatic login or Addons. These checks inspect your current setup and explain the first concrete problem they find.</p></div>");
        body.Append("<div class=\"button-row\"><button class=\"primary\" data-action=\"topic\" data-value=\"auto-login\">Automatic login help</button>");
        body.Append("<button data-action=\"topic\" data-value=\"addons\">Addon help</button></div></section>");
    }

    private static void RenderArticle(
        StringBuilder body,
        HelpArticle article,
        HelpActionResponse? response)
    {
        if (article.Highlights.Count > 0)
        {
            body.Append("<section class=\"highlight-grid\">");
            foreach (var highlight in article.Highlights)
            {
                var tag = string.IsNullOrWhiteSpace(highlight.Action)
                    ? "div"
                    : "button";
                body.Append("<");
                body.Append(tag);
                body.Append(" class=\"highlight");
                if (!string.IsNullOrWhiteSpace(highlight.Action))
                {
                    body.Append(" interactive\" data-action=\"");
                    body.Append(Attribute(highlight.Action));
                    body.Append("\"");
                }
                else
                {
                    body.Append("\"");
                }

                body.Append("><span class=\"highlight-mark\">✓</span><div class=\"highlight-copy\"><p>");
                body.Append(Encode(highlight.Text));
                body.Append("</p>");
                if (!string.IsNullOrWhiteSpace(highlight.Action))
                {
                    body.Append("<em>");
                    body.Append(Encode(highlight.ActionText));
                    body.Append(" →</em>");
                }

                body.Append("</div></");
                body.Append(tag);
                body.Append(">");
            }

            body.Append("</section>");
        }

        if (article.DiagnosticAction != null ||
            article.OpenAction != null)
        {
            body.Append("<section class=\"action-panel\"><div class=\"button-row\">");

            if (article.DiagnosticAction != null)
            {
                body.Append("<button class=\"primary\" data-action=\"");
                body.Append(Attribute(article.DiagnosticAction));
                body.Append("\">");
                body.Append(Encode(article.DiagnosticText));
                body.Append("</button>");
            }

            if (article.OpenAction != null)
            {
                body.Append("<button data-action=\"");
                body.Append(Attribute(article.OpenAction));
                body.Append("\">");
                body.Append(Encode(article.OpenActionText));
                body.Append("</button>");
            }

            body.Append("</div></section>");
        }

        if (response != null)
        {
            var kind = response.Kind.ToString().ToLowerInvariant();
            body.Append("<section class=\"result ");
            body.Append(Attribute(kind));
            body.Append("\"><div class=\"result-mark\">");
            body.Append(response.Kind switch
            {
                HelpResultKind.Success => "✓",
                HelpResultKind.Warning => "!",
                HelpResultKind.Error => "×",
                _ => "i",
            });
            body.Append("</div><div class=\"result-copy\"><div class=\"kicker\">CHECK RESULT</div><h2>");
            body.Append(Encode(response.Title));
            body.Append("</h2><p>");
            body.Append(Encode(response.Message));
            body.Append("</p>");

            if (!string.IsNullOrWhiteSpace(response.ShowMeAction))
            {
                body.Append("<button class=\"primary\" data-action=\"");
                body.Append(Attribute(response.ShowMeAction));
                body.Append("\">");
                body.Append(Encode(response.ShowMeText));
                body.Append("</button>");
            }

            body.Append("</div></section>");
        }

        foreach (var section in article.Sections)
        {
            var tag = string.IsNullOrWhiteSpace(section.Action)
                ? "section"
                : "button";
            body.Append("<");
            body.Append(tag);
            body.Append(" class=\"section prose");
            if (!string.IsNullOrWhiteSpace(section.Action))
            {
                body.Append(" interactive\" data-action=\"");
                body.Append(Attribute(section.Action));
                body.Append("\"");
            }
            else
            {
                body.Append("\"");
            }

            body.Append("><div class=\"section-copy\"><h2>");
            body.Append(Encode(section.Title));
            body.Append("</h2><p>");
            body.Append(Encode(section.Body));
            body.Append("</p></div>");
            if (!string.IsNullOrWhiteSpace(section.Action))
            {
                body.Append("<em>");
                body.Append(Encode(section.ActionText));
                body.Append(" →</em>");
            }

            body.Append("</");
            body.Append(tag);
            body.Append(">");
        }
    }

    private static string Encode(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Attribute(string? value) =>
        WebUtility.HtmlEncode(value ?? string.Empty)
            .Replace("'", "&#39;", StringComparison.Ordinal);

    private const string Css = """
        :root {
            color-scheme: dark;
            font-family: "Segoe UI", sans-serif;
            background: #0a0f16;
            color: #ebf2f7;
        }
        * { box-sizing: border-box; }
        html, body { margin: 0; min-height: 100%; background: #0a0f16; }
        body { overflow-y: scroll; }
        main { max-width: 1040px; margin: 0 auto; padding: 42px 48px 70px; }
        .hero { padding: 8px 0 34px; border-bottom: 1px solid #2d576c; }
        .kicker { color: #4dd3ef; font-size: 12px; font-weight: 800; letter-spacing: .14em; }
        h1 { margin: 8px 0 12px; font-size: 38px; line-height: 1.08; }
        h2 { margin: 4px 0 10px; font-size: 22px; }
        p { color: #99aebe; line-height: 1.62; margin: 0; }
        .summary { max-width: 800px; font-size: 17px; color: #c5d4df; }
        .back-button { margin: 0 0 22px; padding: 8px 12px; background: transparent; border-color: #2d576c; color: #9edff0; }
        .section { margin-top: 34px; padding: 26px; background: #121c27; border: 1px solid #2d576c; border-radius: 10px; }
        .section-heading { display: flex; justify-content: space-between; gap: 24px; align-items: end; }
        .cards { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 14px; margin-top: 22px; }
        .feature-card { display: flex; min-height: 170px; flex-direction: column; align-items: flex-start; text-align: left; padding: 20px; border: 1px solid #2d576c; border-radius: 9px; background: #172532; color: #ebf2f7; cursor: pointer; }
        .feature-card:hover { border-color: #33a6c7; background: #1e495e; transform: translateY(-1px); }
        .feature-card strong { font-size: 20px; margin: 5px 0 8px; }
        .feature-card span:not(.feature-kicker) { color: #aebfcb; line-height: 1.45; }
        .feature-card em { margin-top: auto; padding-top: 16px; color: #4dd3ef; font-style: normal; font-weight: 700; }
        .feature-kicker { color: #f2bc49; font-size: 10px; font-weight: 800; letter-spacing: .1em; }
        .assistance { display: flex; justify-content: space-between; align-items: center; gap: 28px; }
        .action-panel { display: flex; justify-content: flex-end; margin-top: 16px; }
        .button-row { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: 10px; }
        button { appearance: none; border: 1px solid #2d576c; border-radius: 5px; padding: 10px 15px; background: #14222e; color: #ebf2f7; font-weight: 700; cursor: pointer; }
        button:hover { background: #1e495e; border-color: #33a6c7; }
        button.primary { background: #184052; color: #4dd3ef; border-color: #33a6c7; }
        .highlight-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-top: 28px; }
        .highlight { display: grid; grid-template-columns: 18px minmax(0, 1fr); gap: 10px; min-height: 156px; padding: 16px; background: #121c27; border: 1px solid #2d576c; border-radius: 8px; text-align: left; align-items: start; }
        button.highlight { width: 100%; font: inherit; }
        .highlight.interactive:hover, .section.interactive:hover { border-color: #33a6c7; background: #172a38; transform: translateY(-1px); }
        .highlight-mark { color: #4dd58e; font-weight: 900; }
        .highlight-copy { min-width: 0; min-height: 122px; display: flex; flex-direction: column; align-items: flex-start; }
        .highlight p { width: 100%; color: #c5d4df; line-height: 1.42; }
        .highlight em, .section.interactive em { display: block; margin-top: auto; padding-top: 14px; color: #4dd3ef; font-style: normal; font-weight: 700; white-space: normal; }
        button.section { display: block; width: 100%; text-align: left; font: inherit; }
        .section-copy { min-width: 0; }
        .section.interactive em { margin-top: 14px; }
        .result { margin-top: 22px; padding: 22px; border: 1px solid #2d576c; border-radius: 9px; display: flex; gap: 18px; background: #121c27; }
        .result-mark { width: 42px; height: 42px; flex: 0 0 42px; display: grid; place-items: center; border-radius: 50%; font-size: 23px; font-weight: 900; color: #4dd3ef; border: 1px solid #33a6c7; }
        .result.success { border-color: #2e8b60; }
        .result.success .result-mark { color: #4dd58e; border-color: #4dd58e; }
        .result.warning { border-color: #9b762e; }
        .result.warning .result-mark { color: #f2bc49; border-color: #f2bc49; }
        .result.error { border-color: #9b3e45; }
        .result.error .result-mark { color: #ec5e5e; border-color: #ec5e5e; }
        .result-copy { flex: 1; }
        .result-copy button { margin-top: 16px; }
        .prose p { max-width: 850px; font-size: 15px; }
        @media (max-width: 820px) {
            main { padding: 30px 26px 60px; }
            .cards, .highlight-grid { grid-template-columns: 1fr; }
            .assistance { align-items: flex-start; flex-direction: column; }
            .action-panel { justify-content: flex-start; }
            .button-row { justify-content: flex-start; }
        }
        """;

    private const string Script = """
        document.addEventListener('click', event => {
            const target = event.target.closest('[data-action]');
            if (!target) return;
            event.preventDefault();
            window.chrome.webview.postMessage({
                action: target.dataset.action || '',
                value: target.dataset.value || ''
            });
        });
        """;
}
