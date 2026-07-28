// ReSharper disable LocalizableElement
// ReSharper disable AsyncVoidEventHandlerMethod
// ReSharper disable StringLiteralTypo
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
namespace Net7ClientManager.Forms;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Navigation;
using Net7ClientManager.Services;

internal enum MissionWikiContentState
{
    Loading,
    Article,
    Search,
    Failed,
}

internal sealed class MissionWikiWebViewForm : Form
{
    private const int WsExToolWindow = 0x00000080;

    private enum MissionWikiNavigationKind
    {
        Candidate,
        Session,
        Search,
        Job,
        User,
    }

    private static readonly JsonSerializerOptions webJsonOptions =
        new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    private readonly WebView2 webView = new();
    private readonly Label statusLabel = new();
    private readonly Func<
        MissionWikiLocationHint,
        NavigationDestination?> resolveNavigationDestination;
    private readonly Func<
        NavigationDestination,
        NavigationRouteCommandResult> setNavigationDestination;
    private readonly Dictionary<string, NavigationActionRegistration>
        navigationActions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Uri> missionSessionUris =
        new(StringComparer.Ordinal);

    private IReadOnlyList<string> pageTitleCandidates = [];
    private int candidateIndex;
    private int navigationGeneration;
    private ulong activeNavigationId;
    private MissionWikiNavigationKind activeNavigationKind =
        MissionWikiNavigationKind.User;
    private MissionWikiNavigationKind? pendingNavigationKind;
    private MissionJobGuidance? jobGuidance;
    private bool initialized;
    private bool disposed;

    public MissionWikiWebViewForm(
        Func<
            MissionWikiLocationHint,
            NavigationDestination?> resolveNavigationDestination,
        Func<
            NavigationDestination,
            NavigationRouteCommandResult> setNavigationDestination)
    {
        this.resolveNavigationDestination =
            resolveNavigationDestination ??
            throw new ArgumentNullException(
                nameof(resolveNavigationDestination));
        this.setNavigationDestination =
            setNavigationDestination ??
            throw new ArgumentNullException(
                nameof(setNavigationDestination));

        this.FormBorderStyle = FormBorderStyle.None;
        this.ShowInTaskbar = false;
        this.StartPosition = FormStartPosition.Manual;
        this.AutoScaleMode = AutoScaleMode.None;
        this.Padding = new Padding(1);
        this.BackColor = Color.FromArgb(42, 131, 168);

        this.webView.Dock = DockStyle.Fill;
        this.webView.DefaultBackgroundColor =
            Color.FromArgb(10, 17, 24);
        this.webView.Visible = false;

        this.statusLabel.Dock = DockStyle.Fill;
        this.statusLabel.BackColor =
            Color.FromArgb(10, 17, 24);
        this.statusLabel.ForeColor =
            Color.FromArgb(243, 246, 248);
        this.statusLabel.TextAlign =
            ContentAlignment.MiddleCenter;
        this.statusLabel.Padding = new Padding(24);
        this.statusLabel.Text =
            "Preparing Net-7 Wiki browser...";

        this.Controls.Add(this.webView);
        this.Controls.Add(this.statusLabel);
    }

    public AddonRuntimeState RuntimeState { get; private set; } =
        AddonRuntimeState.WaitingForContext;

    public string StatusText { get; private set; } =
        "Waiting for mission details.";

    public string? MissionName { get; private set; }

    public string? JobGuidanceFingerprint =>
        this.jobGuidance?.Fingerprint;

    public MissionWikiContentState ContentState { get; private set; } =
        MissionWikiContentState.Loading;

    public event EventHandler? ContentStateChanged;

    private CoreWebView2 BrowserCore =>
        this.webView.CoreWebView2 ??
        throw new InvalidOperationException(
            "The Mission Wiki browser is not initialized.");

    protected override bool ShowWithoutActivation =>
        true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow;
            return parameters;
        }
    }

    public void NavigateToMission(string selectedMissionName)
    {
        if (this.disposed ||
            string.IsNullOrWhiteSpace(selectedMissionName))
        {
            return;
        }

        var candidates =
            MissionWikiFeature.CreatePageTitleCandidates(
                selectedMissionName);

        var isCurrentMission = string.Equals(
            this.MissionName,
            selectedMissionName,
            StringComparison.Ordinal);

        if (isCurrentMission &&
            this.pageTitleCandidates.SequenceEqual(
                candidates,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        this.MissionName = selectedMissionName;
        this.jobGuidance = null;
        this.pageTitleCandidates = candidates;
        this.candidateIndex = 0;
        this.navigationActions.Clear();
        this.activeNavigationId = 0;
        this.pendingNavigationKind = null;
        var generation = ++this.navigationGeneration;

        this.RuntimeState = AddonRuntimeState.Loading;
        this.StatusText = string.Concat(
            "Opening Net-7 Wiki for ",
            selectedMissionName,
            "...");

        this.ShowStatus(this.StatusText);
        this.SetContentState(MissionWikiContentState.Loading);

        if (this.missionSessionUris.TryGetValue(
                selectedMissionName,
                out var sessionUri))
        {
            _ = this.NavigateUriAsync(
                sessionUri,
                MissionWikiNavigationKind.Session,
                generation,
                "Restoring your Net-7 Wiki page...");
            return;
        }

        if (this.pageTitleCandidates.Count > 0)
        {
            _ = this.NavigateCurrentCandidateAsync(generation);
            return;
        }

        _ = this.NavigateSearchAsync(generation);
    }

    public void NavigateToJob(MissionJobGuidance guidance)
    {
        ArgumentNullException.ThrowIfNull(guidance);

        if (this.disposed ||
            string.IsNullOrWhiteSpace(guidance.MissionName))
        {
            return;
        }

        var isCurrentJob =
            string.Equals(
                this.MissionName,
                guidance.MissionName,
                StringComparison.Ordinal) &&
            string.Equals(
                this.jobGuidance?.Fingerprint,
                guidance.Fingerprint,
                StringComparison.Ordinal);

        if (isCurrentJob)
        {
            return;
        }

        this.MissionName = guidance.MissionName;
        this.jobGuidance = guidance;
        this.pageTitleCandidates = [];
        this.candidateIndex = 0;
        this.navigationActions.Clear();
        this.activeNavigationId = 0;
        this.pendingNavigationKind = null;
        var generation = ++this.navigationGeneration;

        this.RuntimeState = AddonRuntimeState.Loading;
        this.StatusText = string.Concat(
            "Preparing job guidance for ",
            guidance.MissionName,
            "...");

        this.ShowStatus(this.StatusText);
        this.SetContentState(MissionWikiContentState.Loading);
        _ = this.NavigateToJobAsync(guidance, generation);
    }

    public void ReloadCurrentMission()
    {
        if (this.disposed ||
            string.IsNullOrWhiteSpace(this.MissionName))
        {
            return;
        }

        if (this.jobGuidance != null)
        {
            var guidance = this.jobGuidance;
            this.jobGuidance = null;
            this.NavigateToJob(guidance);
            return;
        }

        this.missionSessionUris.Remove(this.MissionName);
        this.candidateIndex = 0;
        this.navigationActions.Clear();
        this.activeNavigationId = 0;
        this.pendingNavigationKind = null;
        var generation = ++this.navigationGeneration;

        this.RuntimeState = AddonRuntimeState.Loading;
        this.StatusText = string.Concat(
            "Reloading Net-7 Wiki for ",
            this.MissionName,
            "...");

        this.ShowStatus(this.StatusText);
        this.SetContentState(MissionWikiContentState.Loading);

        if (this.pageTitleCandidates.Count > 0)
        {
            _ = this.NavigateCurrentCandidateAsync(generation);
            return;
        }

        _ = this.NavigateSearchAsync(generation);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            this.disposed = true;
            this.navigationGeneration++;
            this.navigationActions.Clear();
            this.missionSessionUris.Clear();

            var core = this.webView.CoreWebView2;

            if (core != null)
            {
                core.NavigationStarting -=
                    this.CoreWebView2_OnNavigationStarting;
                core.NavigationCompleted -=
                    this.CoreWebView2_OnNavigationCompleted;
                core.NewWindowRequested -=
                    this.CoreWebView2_OnNewWindowRequested;
                core.ProcessFailed -=
                    this.CoreWebView2_OnProcessFailed;
                core.WebMessageReceived -=
                    this.CoreWebView2_OnWebMessageReceived;
            }

            this.webView.Dispose();
            this.statusLabel.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task NavigateToJobAsync(
        MissionJobGuidance guidance,
        int generation)
    {
        try
        {
            await this.EnsureInitializedAsync()
                .ConfigureAwait(true);

            if (this.disposed ||
                generation != this.navigationGeneration)
            {
                return;
            }

            this.navigationActions.Clear();
            var action = this.CreateJobNavigationAction(
                guidance.Destination?.RouteDestination);
            var html = BuildJobHtml(guidance, action);

            this.RuntimeState = AddonRuntimeState.Loading;
            this.StatusText = string.Concat(
                "Opening job guidance for ",
                guidance.MissionName,
                "...");

            this.statusLabel.Visible = false;
            this.webView.Visible = true;
            this.SetContentState(MissionWikiContentState.Loading);
            this.activeNavigationId = 0;
            this.pendingNavigationKind =
                MissionWikiNavigationKind.Job;
            var core = this.BrowserCore;
            core.Stop();
            core.NavigateToString(html);
        }
        catch (Exception ex)
        {
            this.pendingNavigationKind = null;

            if (generation == this.navigationGeneration)
            {
                this.ShowFailure(
                    string.Concat(
                        "Job guidance could not be displayed: ",
                        ex.Message));
            }
        }
    }

    private NavigationActionPayload? CreateJobNavigationAction(
        NavigationDestination? destination)
    {
        if (destination == null)
        {
            return null;
        }

        const string domId = "job-route";
        var actionToken = Guid.NewGuid().ToString(
            "N",
            CultureInfo.InvariantCulture);

        this.navigationActions[actionToken] =
            new NavigationActionRegistration(
                domId,
                destination);

        return new NavigationActionPayload
        {
            DomId = domId,
            ActionToken = actionToken,
            SectorName = destination.SectorName,
            SystemName = destination.SystemName,
            TargetName = destination.TargetName,
            Context = destination.Kind == NavigationDestinationKind.Target
                ? "Destination resolved from the current objective."
                : "Sector resolved from the current objective.",
            MentionIds = [],
        };
    }

    private static string BuildJobHtml(
        MissionJobGuidance guidance,
        NavigationActionPayload? action)
    {
        static string Encode(string? value) =>
            WebUtility.HtmlEncode(value ?? "");

        var progress = guidance.Stage.HasValue
            ? guidance.StageCount.HasValue
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Step {guidance.Stage.Value} of {guidance.StageCount.Value}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Step {guidance.Stage.Value}")
            : "Current objective";
        var objective = string.IsNullOrWhiteSpace(guidance.Objective)
            ? string.IsNullOrWhiteSpace(guidance.Summary)
                ? "The current objective has not been revealed yet."
                : guidance.Summary
            : guidance.Objective;
        var jobBadge = guidance.JobCategory switch
        {
            MissionJournalJobCategory.Combat => "COMBAT JOB",
            MissionJournalJobCategory.Trade => "TRADE JOB",
            MissionJournalJobCategory.Explore => "EXPLORE JOB",
            _ => "JOB",
        };
        var acceptedLocation = string.Join(
            " / ",
            new[]
                {
                    guidance.AcceptedSystem,
                    guidance.AcceptedSector,
                    guidance.AcceptedStarbase,
                }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        var acceptedSection = guidance.AcceptedAt.HasValue ||
                              acceptedLocation.Length > 0
            ? string.Concat(
                "<section class=\"card metadata\"><h2>Accepted</h2><p>",
                guidance.AcceptedAt.HasValue
                    ? Encode(guidance.AcceptedAt.Value.ToLocalTime().ToString(
                        "g",
                        CultureInfo.CurrentCulture))
                    : "Time unavailable",
                acceptedLocation.Length > 0
                    ? string.Concat(" · ", Encode(acceptedLocation))
                    : "",
                "</p></section>")
            : "";
        var summarySection =
            string.IsNullOrWhiteSpace(guidance.Summary) ||
            string.Equals(
                guidance.Summary.Trim(),
                objective.Trim(),
                StringComparison.Ordinal)
                ? ""
                : string.Concat(
                    "<section class=\"card\"><h2>Job details</h2><p>",
                    Encode(guidance.Summary),
                    "</p></section>");
        var rewardSection = string.IsNullOrWhiteSpace(guidance.Reward)
            ? ""
            : string.Concat(
                "<section class=\"card\"><h2>Reward</h2><p>",
                Encode(guidance.Reward),
                "</p></section>");

        string destinationSection;
        var destination = guidance.Destination;

        if (action != null)
        {
            var tokenJson = JsonSerializer.Serialize(
                action.ActionToken);
            var destinationLocation =
                string.IsNullOrWhiteSpace(action.TargetName)
                    ? action.SystemName
                    : string.Concat(
                        action.SystemName,
                        " / ",
                        action.SectorName);
            destinationSection = string.Concat(
                "<section class=\"card destination\"><div><h2>Destination</h2>",
                "<div class=\"destination-name\">",
                Encode(action.TargetName ?? action.SectorName),
                "</div><div class=\"destination-location\">",
                Encode(destinationLocation),
                "</div></div><button id=\"",
                action.DomId,
                "\" type=\"button\">Set destination</button></section>",
                "<script>document.getElementById('",
                action.DomId,
                "').addEventListener('click',event=>{",
                "if(!event.isTrusted)return;",
                "const button=event.currentTarget;",
                "button.disabled=true;",
                "button.textContent='Setting...';",
                "window.chrome.webview.postMessage(JSON.stringify(",
                "{type:'set-destination',actionToken:",
                tokenJson,
                "}));});",
                "window.net7cmMissionNavigationResult=(id,ok,message)=>{",
                "const button=document.getElementById(id);",
                "if(!button)return;",
                "button.disabled=ok;",
                "button.textContent=ok?'Destination set':'Try again';",
                "button.title=message||'';",
                "button.classList.toggle('success',ok);",
                "button.classList.toggle('failure',!ok);",
                "};</script>");
        }
        else if (destination != null)
        {
            destinationSection = string.Concat(
                "<section class=\"card destination unresolved\"><div>",
                "<h2>Destination</h2><div class=\"destination-name\">",
                Encode(destination.SectorName),
                "</div><div class=\"destination-location\">",
                Encode(destination.SystemName),
                "</div></div></section>");
        }
        else
        {
            destinationSection = string.Concat(
                "<section class=\"card destination unresolved\"><div>",
                "<h2>Destination</h2><p>No destination could be found in ",
                "the current objective.</p>",
                "</div></section>");
        }

        var builder = new StringBuilder();
        builder.Append(
            "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<meta name=\"color-scheme\" content=\"dark\"><style>");
        builder.Append(
            """
            html,body{margin:0;min-height:100%;background:#0a1118;color:#f3f6f8;font-family:'Segoe UI',Arial,sans-serif}
            main{box-sizing:border-box;padding:20px 30px 28px;max-width:1100px}
            .eyebrow{display:inline-block;padding:6px 16px;border-radius:4px;
                background:#1763a5;color:#fff;font-weight:800;letter-spacing:.08em}
            h1{margin:16px 0 14px;font-size:28px;line-height:1.2;color:#fff}
            p{margin:0;line-height:1.55}
            .card{box-sizing:border-box;margin:12px 0;padding:16px;border:1px solid #2a83a8;border-radius:6px;background:#101b25}
            .card h2{margin:0 0 10px;font-size:17px;color:#fff}
            .objective{font-size:17px}
            .destination{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:18px;align-items:center}
            .destination-name{font-size:18px;font-weight:750;color:#fff}
            .destination-location{margin-top:4px;color:#aebdca}
            .destination-note{margin-top:12px;color:#aebdca}
            .unresolved{border-color:#516270}
            button{appearance:none;border:1px solid #2a83a8;border-radius:4px;
                background:#1d4667;color:#fff;cursor:pointer;font:inherit;
                font-weight:750;padding:10px 15px}
            button:hover{background:#275e88}
            button:disabled{cursor:default;opacity:.9}
            .success{border-color:#4cc97a;background:#205f3b}
            .failure{border-color:#dc6a6a;background:#6b2d2d}
            """);
        builder.Append("</style></head><body><main><span class=\"eyebrow\">");
        builder.Append(Encode(jobBadge));
        builder.Append("</span><h1>");
        builder.Append(Encode(guidance.MissionName));
        builder.Append("</h1>");
        builder.Append(acceptedSection);
        builder.Append("<section class=\"card\"><h2>");
        builder.Append(Encode(progress));
        builder.Append("</h2><p class=\"objective\">");
        builder.Append(Encode(objective));
        builder.Append("</p></section>");
        builder.Append(destinationSection);
        builder.Append(summarySection);
        builder.Append(rewardSection);
        builder.Append("</main></body></html>");
        return builder.ToString();
    }

    private async Task NavigateCurrentCandidateAsync(
        int generation)
    {
        if (this.candidateIndex < 0 ||
            this.candidateIndex >=
            this.pageTitleCandidates.Count)
        {
            await this.NavigateSearchAsync(generation)
                .ConfigureAwait(true);
            return;
        }

        var displayTitle =
            this.pageTitleCandidates[this.candidateIndex];
        var uri = MissionWikiFeature.BuildPageUri(
            displayTitle);

        await this.NavigateUriAsync(
                uri,
                MissionWikiNavigationKind.Candidate,
                generation,
                string.Concat(
                    "Navigating to ",
                    displayTitle,
                    "..."))
            .ConfigureAwait(true);
    }

    private async Task NavigateSearchAsync(int generation)
    {
        if (string.IsNullOrWhiteSpace(this.MissionName))
        {
            return;
        }

        var uri = MissionWikiFeature.BuildSearchUri(
            this.MissionName);

        await this.NavigateUriAsync(
                uri,
                MissionWikiNavigationKind.Search,
                generation,
                "Opening Net-7 Wiki search...")
            .ConfigureAwait(true);
    }

    private async Task NavigateUriAsync(
        Uri uri,
        MissionWikiNavigationKind navigationKind,
        int generation,
        string statusText)
    {
        try
        {
            await this.EnsureInitializedAsync()
                .ConfigureAwait(true);

            if (this.disposed ||
                generation != this.navigationGeneration)
            {
                return;
            }

            this.RuntimeState = AddonRuntimeState.Loading;
            this.StatusText = statusText;

            this.statusLabel.Visible = false;
            this.webView.Visible = true;
            this.navigationActions.Clear();
            this.SetContentState(MissionWikiContentState.Loading);

            // Stop any previous mission immediately. WebView2 can complete
            // superseded navigations later, so clear the active ID before
            // starting the new request and ignore their callbacks.
            this.activeNavigationId = 0;
            this.pendingNavigationKind = navigationKind;
            var core = this.BrowserCore;
            core.Stop();
            core.Navigate(uri.AbsoluteUri);
        }
        catch (Exception ex)
        {
            this.pendingNavigationKind = null;

            if (generation == this.navigationGeneration)
            {
                this.ShowFailure(
                    string.Concat(
                        "Mission Wiki browser failed: ",
                        ex.Message));
            }
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (this.initialized)
        {
            return;
        }

        var environment =
            await MissionWikiBrowserEnvironment.GetAsync()
                .ConfigureAwait(true);

        await this.webView
            .EnsureCoreWebView2Async(environment)
            .ConfigureAwait(true);

        if (this.disposed)
        {
            return;
        }

        var core = this.BrowserCore;

        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsBuiltInErrorPageEnabled = true;
        core.Settings.IsStatusBarEnabled = true;
        core.Settings.IsZoomControlEnabled = true;

        core.NavigationStarting +=
            this.CoreWebView2_OnNavigationStarting;
        core.NavigationCompleted +=
            this.CoreWebView2_OnNavigationCompleted;
        core.NewWindowRequested +=
            this.CoreWebView2_OnNewWindowRequested;
        core.ProcessFailed +=
            this.CoreWebView2_OnProcessFailed;
        core.WebMessageReceived +=
            this.CoreWebView2_OnWebMessageReceived;

        this.initialized = true;
    }

    private void CoreWebView2_OnNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(
                e.Uri,
                UriKind.Absolute,
                out var uri))
        {
            e.Cancel = true;
            return;
        }

        var isInternalDocumentUri =
            string.Equals(
                uri.AbsoluteUri,
                "about:blank",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                uri.Scheme,
                "data",
                StringComparison.OrdinalIgnoreCase);
        var isJobDocument =
            this.jobGuidance != null &&
            isInternalDocumentUri &&
            (this.pendingNavigationKind ==
                 MissionWikiNavigationKind.Job ||
             this.activeNavigationKind ==
                 MissionWikiNavigationKind.Job);

        if (isJobDocument ||
            MissionWikiFeature.IsAllowedTopLevelUri(uri))
        {
            var previousNavigationId = this.activeNavigationId;

            if (this.pendingNavigationKind.HasValue)
            {
                this.activeNavigationKind =
                    this.pendingNavigationKind.Value;
                this.pendingNavigationKind = null;
            }
            else if (!isJobDocument &&
                     e.NavigationId != previousNavigationId)
            {
                this.activeNavigationKind =
                    MissionWikiNavigationKind.User;
            }

            this.activeNavigationId = e.NavigationId;

            if (!isJobDocument)
            {
                this.navigationActions.Clear();
            }

            var isCloudflareChallengeHost =
                uri.Host.Equals(
                    "challenges.cloudflare.com",
                    StringComparison.OrdinalIgnoreCase);

            this.RuntimeState = isCloudflareChallengeHost
                ? AddonRuntimeState.Running
                : AddonRuntimeState.Loading;

            this.StatusText = isJobDocument
                ? "Loading job guidance..."
                : isCloudflareChallengeHost
                    ? "Cloudflare verification is loading."
                    : "Loading Net-7 Wiki...";

            if (!isCloudflareChallengeHost)
            {
                this.SetContentState(
                    MissionWikiContentState.Loading);
            }

            return;
        }

        e.Cancel = true;
        OpenExternalBrowser(uri);
    }

    private async void CoreWebView2_OnNavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        if (this.disposed ||
            e.NavigationId != this.activeNavigationId)
        {
            return;
        }

        var completedNavigationKind =
            this.activeNavigationKind;

        if (completedNavigationKind ==
            MissionWikiNavigationKind.Job)
        {
            if (!e.IsSuccess)
            {
                this.ShowFailure(
                    "Job guidance could not be displayed.");
                return;
            }

            this.RuntimeState = AddonRuntimeState.Running;
            this.StatusText = string.Concat(
                "Showing job guidance for ",
                this.MissionName ?? "this job",
                ".");
            this.SetContentState(MissionWikiContentState.Article);
            return;
        }

        if (!e.IsSuccess)
        {
            if (e.HttpStatusCode is 400 or 404)
            {
                await this.HandleMissingNavigationAsync(
                        completedNavigationKind)
                    .ConfigureAwait(true);
                return;
            }

            this.ShowFailure("Mission Wiki could not be loaded.");
            return;
        }

        var completedNavigationId = e.NavigationId;
        var core = this.BrowserCore;
        var sourceUri = TryGetWikiSourceUri(core.Source);

        if (sourceUri != null &&
            MissionWikiFeature.IsSearchUri(sourceUri))
        {
            this.RememberMissionUri(sourceUri);
            this.RuntimeState = AddonRuntimeState.Running;
            this.StatusText = string.Concat(
                "Searching Net-7 Wiki for ",
                this.MissionName ?? "this mission",
                ".");
            this.SetContentState(MissionWikiContentState.Search);
            return;
        }

        try
        {
            var probeJson =
                await core.ExecuteScriptAsync(
                        """
                        (() => {
                            const title = document.title || "";
                            const challenge =
                                title === "Just a moment..." ||
                                !!document.querySelector(
                                    'script[src*="/cdn-cgi/challenge-platform/"]');
                            const bodyText =
                                document.body?.innerText || "";
                            const invalidTitle =
                                title === "Bad title" ||
                                /requested page title contains invalid characters/i
                                    .test(bodyText);
                            const missing =
                                !!document.querySelector(
                                    "#noarticletext, .mw-noarticletext") ||
                                /there is currently no text in this page/i
                                    .test(bodyText);
                            const article =
                                !!document.querySelector(
                                    "#mw-content-text .mw-parser-output, .mw-parser-output");
                            return {
                                title,
                                challenge,
                                invalidTitle,
                                missing,
                                article
                            };
                        })()
                        """)
                    .ConfigureAwait(true);

            if (this.disposed ||
                completedNavigationId != this.activeNavigationId)
            {
                return;
            }

            var probe = JsonSerializer.Deserialize<PageProbe>(
                probeJson,
                webJsonOptions);

            if (probe?.Challenge == true)
            {
                this.RuntimeState = AddonRuntimeState.Running;
                this.StatusText =
                    "Cloudflare verification required. Complete it in the mission panel.";
                this.SetContentState(MissionWikiContentState.Article);
                return;
            }

            if (probe is { Missing: true, } or { InvalidTitle: true, })
            {
                await this.HandleMissingNavigationAsync(
                        completedNavigationKind)
                    .ConfigureAwait(true);
                return;
            }

            if (sourceUri != null)
            {
                this.RememberMissionUri(sourceUri);
            }

            if (probe?.Article == true)
            {
                await this.ApplyReaderModeAsync()
                    .ConfigureAwait(true);

                if (this.disposed ||
                    completedNavigationId != this.activeNavigationId)
                {
                    return;
                }

                await this.BuildMissionNavigationAsync()
                    .ConfigureAwait(true);

                if (this.disposed ||
                    completedNavigationId != this.activeNavigationId)
                {
                    return;
                }

                this.RuntimeState = AddonRuntimeState.Running;
                this.StatusText = string.Concat(
                    "Showing ",
                    probe.Title ?? this.MissionName ??
                    "Net-7 Wiki",
                    ".");
                this.SetContentState(MissionWikiContentState.Article);
                return;
            }

            this.RuntimeState = AddonRuntimeState.Running;
            this.StatusText = string.Concat(
                "Showing ",
                probe?.Title ?? this.MissionName ??
                "Net-7 Wiki",
                ".");
            this.SetContentState(MissionWikiContentState.Article);
        }
        catch (Exception ex)
        {
            if (sourceUri != null)
            {
                this.RememberMissionUri(sourceUri);
            }

            this.RuntimeState = AddonRuntimeState.Running;
            this.StatusText = string.Concat(
                "Net-7 Wiki loaded; enhanced reader tools were unavailable: ",
                ex.Message);
            this.SetContentState(MissionWikiContentState.Article);
        }
    }

    private async Task HandleMissingNavigationAsync(
        MissionWikiNavigationKind navigationKind)
    {
        var generation = this.navigationGeneration;

        switch (navigationKind)
        {
            case MissionWikiNavigationKind.Candidate:
                await this.TryNextCandidateOrOpenSearchAsync()
                    .ConfigureAwait(true);
                return;

            case MissionWikiNavigationKind.Session:
                if (!string.IsNullOrWhiteSpace(this.MissionName))
                {
                    this.missionSessionUris.Remove(
                        this.MissionName);
                }

                this.candidateIndex = 0;

                if (this.pageTitleCandidates.Count > 0)
                {
                    await this.NavigateCurrentCandidateAsync(
                            generation)
                        .ConfigureAwait(true);
                    return;
                }

                await this.NavigateSearchAsync(generation)
                    .ConfigureAwait(true);
                return;

            case MissionWikiNavigationKind.Search:
                this.ShowFailure(
                    "Net-7 Wiki search could not be loaded.");
                return;

            case MissionWikiNavigationKind.Job:
                this.ShowFailure(
                    "Job guidance could not be displayed.");
                return;

            case MissionWikiNavigationKind.User:
                await this.NavigateSearchAsync(generation)
                    .ConfigureAwait(true);
                return;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(navigationKind),
                    navigationKind,
                    message: null);
        }
    }

    private async Task TryNextCandidateOrOpenSearchAsync()
    {
        var generation = this.navigationGeneration;

        if (this.candidateIndex + 1 <
            this.pageTitleCandidates.Count)
        {
            this.candidateIndex++;
            await this.NavigateCurrentCandidateAsync(generation)
                .ConfigureAwait(true);
            return;
        }

        await this.NavigateSearchAsync(generation)
            .ConfigureAwait(true);
    }

    private void RememberMissionUri(Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(this.MissionName) &&
            MissionWikiFeature.IsAllowedArticleUri(uri))
        {
            this.missionSessionUris[this.MissionName] = uri;
        }
    }

    private static Uri? TryGetWikiSourceUri(string? source)
    {
        return Uri.TryCreate(
                   source,
                   UriKind.Absolute,
                   out var uri) &&
               MissionWikiFeature.IsAllowedArticleUri(uri)
            ? uri
            : null;
    }

    private void CoreWebView2_OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!Uri.TryCreate(
                e.Uri,
                UriKind.Absolute,
                out var uri))
        {
            return;
        }

        if (MissionWikiFeature.IsAllowedTopLevelUri(uri))
        {
            this.pendingNavigationKind =
                MissionWikiNavigationKind.User;
            this.BrowserCore.Navigate(uri.AbsoluteUri);
            return;
        }

        OpenExternalBrowser(uri);
    }

    private void CoreWebView2_OnProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs e)
    {
        this.ShowFailure(
            string.Concat(
                "Mission Wiki browser process failed: ",
                e.ProcessFailedKind));
    }

    private async void CoreWebView2_OnWebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (this.disposed ||
            !Uri.TryCreate(
                e.Source,
                UriKind.Absolute,
                out var sourceUri))
        {
            return;
        }

        var isJobDocument =
            this.jobGuidance != null &&
            this.activeNavigationKind ==
                MissionWikiNavigationKind.Job &&
            (string.Equals(
                 sourceUri.AbsoluteUri,
                 "about:blank",
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(
                 sourceUri.Scheme,
                 "data",
                 StringComparison.OrdinalIgnoreCase));

        if (!isJobDocument &&
            !MissionWikiFeature.IsAllowedArticleUri(sourceUri))
        {
            return;
        }

        string messageJson;

        try
        {
            messageJson = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        NavigationRequest? request;

        try
        {
            request = JsonSerializer.Deserialize<NavigationRequest>(
                messageJson,
                webJsonOptions);
        }
        catch (JsonException)
        {
            return;
        }

        if (request is null ||
            !string.Equals(
                request.Type,
                "set-destination",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.ActionToken) ||
            !this.navigationActions.TryGetValue(
                request.ActionToken,
                out var registration))
        {
            return;
        }

        var destination = registration.Destination;
        NavigationRouteCommandResult result;

        try
        {
            result = this.setNavigationDestination(destination);
        }
        catch (Exception ex)
        {
            result = NavigationRouteCommandResult.Failure(
                ex.Message);
        }

        var message = result.Succeeded
            ? string.Concat(
                "Destination set: ",
                destination.SectorName)
            : string.IsNullOrWhiteSpace(result.Error)
                ? "Destination could not be set."
                : result.Error;

        this.StatusText = message;

        await this.NotifyNavigationResultAsync(
                registration.DomId,
                result.Succeeded,
                message)
            .ConfigureAwait(true);
    }

    private async Task ApplyReaderModeAsync()
    {
        _ = await this.BrowserCore.ExecuteScriptAsync(
            """
            (() => {
                const styleId = "net7cm-mission-wiki-reader";
                const content =
                    document.querySelector("#content, main.mw-body");

                if (content && content.parentElement !== document.body) {
                    document.body.replaceChildren(content);
                }

                document.querySelectorAll(`
                    #siteSub,
                    #contentSub,
                    #jump-to-nav,
                    .mw-jump-link,
                    .mw-editsection,
                    .mw-indicators,
                    .printfooter,
                    #catlinks,
                    .vector-page-toolbar,
                    .vector-column-start,
                    .vector-column-end,
                    .vector-sticky-header,
                    .vector-page-tools,
                    .vector-toc,
                    .mw-footer,
                    footer
                `).forEach(element => element.remove());

                let style = document.getElementById(styleId);

                if (!style) {
                    style = document.createElement("style");
                    style.id = styleId;
                    document.head.appendChild(style);
                }

                style.textContent = `
                    html, body {
                        background: #0a1118 !important;
                        color: #f3f6f8 !important;
                        min-height: 100% !important;
                    }

                    body {
                        margin: 0 !important;
                        padding: 0 !important;
                    }

                    #content,
                    .mw-body {
                        box-sizing: border-box !important;
                        display: block !important;
                        margin: 0 !important;
                        border: 0 !important;
                        width: 100% !important;
                        max-width: none !important;
                        min-height: 100vh !important;
                        padding: 12px 18px 28px 18px !important;
                        background: #0a1118 !important;
                        color: #f3f6f8 !important;
                    }

                    .mw-body-header {
                        display: block !important;
                        margin: 0 0 12px 0 !important;
                        padding: 0 !important;
                    }

                    #firstHeading {
                        display: block !important;
                        margin: 0 !important;
                        padding: 0 !important;
                        color: #ffffff !important;
                        font-size: 24px !important;
                        line-height: 1.2 !important;
                        border: 0 !important;
                    }

                    #bodyContent,
                    #mw-content-text,
                    .mw-parser-output {
                        box-sizing: border-box !important;
                        margin-top: 0 !important;
                        padding-top: 0 !important;
                        color: #f3f6f8 !important;
                    }

                    a,
                    a:visited {
                        color: #55d7ff !important;
                    }

                    h1, h2, h3, h4, h5, h6 {
                        color: #ffffff !important;
                        border-color: #2a83a8 !important;
                    }

                    table,
                    .wikitable {
                        background: #101b25 !important;
                        color: #f3f6f8 !important;
                        border-color: #2a83a8 !important;
                    }

                    table th,
                    .wikitable th {
                        background: #183247 !important;
                        color: #ffffff !important;
                    }

                    table td,
                    table th,
                    .wikitable td,
                    .wikitable th {
                        border-color: #2a83a8 !important;
                    }

                    code,
                    pre {
                        background: #111d27 !important;
                        color: #f3f6f8 !important;
                    }

                    #net7cm-mission-navigation {
                        box-sizing: border-box;
                        margin: 12px 0 18px 0;
                        padding: 12px;
                        border: 1px solid #2a83a8;
                        border-radius: 6px;
                        background: #101b25;
                    }

                    #net7cm-mission-navigation h2 {
                        margin: 0 0 8px 0 !important;
                        padding: 0 !important;
                        border: 0 !important;
                        font-size: 18px !important;
                    }

                    .net7cm-navigation-row {
                        display: grid;
                        grid-template-columns: minmax(0, 1fr) auto;
                        gap: 8px 12px;
                        align-items: center;
                        padding: 8px 0;
                        border-top: 1px solid #203544;
                    }

                    .net7cm-navigation-row:first-of-type {
                        border-top: 0;
                    }

                    .net7cm-navigation-title {
                        font-weight: 700;
                        color: #ffffff;
                    }

                    .net7cm-navigation-context {
                        grid-column: 1;
                        color: #b9c9d4;
                        font-size: 13px;
                        line-height: 1.35;
                    }

                    .net7cm-navigation-button,
                    .net7cm-inline-route {
                        appearance: none;
                        border: 1px solid #2a83a8;
                        border-radius: 4px;
                        background: #1d4667;
                        color: #ffffff;
                        cursor: pointer;
                        font: inherit;
                        font-weight: 700;
                    }

                    .net7cm-navigation-button {
                        grid-column: 2;
                        grid-row: 1 / span 2;
                        padding: 7px 11px;
                    }

                    .net7cm-inline-route {
                        margin-left: 6px;
                        padding: 2px 6px;
                        font-size: 12px;
                        vertical-align: baseline;
                    }

                    .net7cm-navigation-button:hover,
                    .net7cm-inline-route:hover {
                        background: #275e88;
                    }

                    .net7cm-navigation-button:disabled,
                    .net7cm-inline-route:disabled {
                        cursor: default;
                        opacity: 0.8;
                    }

                    .net7cm-route-success {
                        border-color: #4cc97a !important;
                        background: #205f3b !important;
                    }

                    .net7cm-route-failure {
                        border-color: #dc6a6a !important;
                        background: #6b2d2d !important;
                    }
                `;

                window.scrollTo(0, 0);
                return true;
            })()
            """)
            .ConfigureAwait(true);
    }

    private async Task BuildMissionNavigationAsync()
    {
        var linksJson =
            await this.BrowserCore.ExecuteScriptAsync(
                """
                (() => {
                    const root = document.querySelector(
                        "#mw-content-text .mw-parser-output, .mw-parser-output");

                    if (!root) {
                        return [];
                    }

                    root.querySelectorAll("[data-net7cm-mention-id]")
                        .forEach(element =>
                            element.removeAttribute(
                                "data-net7cm-mention-id"));

                    const mentions = [];
                    let mentionNumber = 0;

                    for (const anchor of root.querySelectorAll("a[href]")) {
                        const linkText =
                            (anchor.textContent || "")
                                .replace(/\s+/g, " ")
                                .trim();

                        if (!linkText) {
                            continue;
                        }

                        let url;

                        try {
                            url = new URL(anchor.href, location.href);
                        } catch {
                            continue;
                        }

                        if (url.hostname !== location.hostname) {
                            continue;
                        }

                        let pageTitle =
                            url.searchParams.get("title");

                        if (!pageTitle &&
                            url.pathname.startsWith("/wiki/")) {
                            pageTitle = decodeURIComponent(
                                url.pathname.substring(6));
                        }

                        if (!pageTitle) {
                            continue;
                        }

                        pageTitle = pageTitle
                            .replaceAll("_", " ")
                            .trim();

                        if (!pageTitle ||
                            pageTitle.startsWith("Special:")) {
                            continue;
                        }

                        let listItem = anchor.closest("li");
                        let topStep = listItem;

                        while (topStep) {
                            const parentItem =
                                topStep.parentElement?.closest("li");

                            if (!parentItem) {
                                break;
                            }

                            topStep = parentItem;
                        }

                        let stepNumber = null;

                        if (topStep?.parentElement?.tagName === "OL") {
                            const siblings =
                                Array.from(
                                    topStep.parentElement.children)
                                    .filter(element =>
                                        element.tagName === "LI");
                            const index = siblings.indexOf(topStep);

                            if (index >= 0) {
                                stepNumber = index + 1;
                            }
                        }

                        const contextElement =
                            topStep ||
                            anchor.closest("li, p, dd, td") ||
                            anchor.parentElement;

                        const context =
                            (contextElement?.innerText || linkText)
                                .replace(/\s+/g, " ")
                                .trim()
                                .slice(0, 320);

                        const coordinateMatch = context.match(
                            /(-?\d+(?:\.\d+)?)\s*x\b[\s,;/]*(?:and\s*)?(-?\d+(?:\.\d+)?)\s*y\b/i);

                        const coordinates = coordinateMatch
                            ? `${coordinateMatch[1]}x, ${coordinateMatch[2]}y`
                            : "";

                        const mentionId =
                            `net7cm-${mentionNumber++}`;

                        anchor.dataset.net7cmMentionId =
                            mentionId;

                        mentions.push({
                            mentionId,
                            linkText,
                            pageTitle,
                            stepNumber,
                            context,
                            coordinates
                        });
                    }

                    return mentions;
                })()
                """)
                .ConfigureAwait(true);

        var mentions =
            JsonSerializer.Deserialize<List<PageLinkMention>>(
                linksJson,
                webJsonOptions) ?? [];

        var actions = this.ResolveNavigationActions(mentions);

        if (actions.Count == 0)
        {
            return;
        }

        var actionsJson = JsonSerializer.Serialize(
            actions,
            webJsonOptions);

        _ = await this.BrowserCore.ExecuteScriptAsync(
            $$"""
            (() => {
                const actions = {{actionsJson}};
                const existing =
                    document.getElementById(
                        "net7cm-mission-navigation");

                existing?.remove();

                document.querySelectorAll(
                    ".net7cm-inline-route")
                    .forEach(element => element.remove());

                if (!actions.length) {
                    return false;
                }

                const createButton = (action, className, text) => {
                    const button =
                        document.createElement("button");
                    button.type = "button";
                    button.className = className;
                    button.textContent = text;
                    button.dataset.net7cmActionId =
                        action.domId;
                    button.addEventListener("click", event => {
                        event.preventDefault();
                        event.stopPropagation();

                        if (!event.isTrusted) {
                            return;
                        }

                        document.querySelectorAll(
                            `[data-net7cm-action-id="${action.domId}"]`)
                            .forEach(candidate => {
                                candidate.disabled = true;
                                candidate.textContent =
                                    "Setting...";
                            });

                        window.chrome.webview.postMessage(
                            JSON.stringify({
                                type: "set-destination",
                                actionToken: action.actionToken
                            }));
                    });
                    return button;
                };

                const navigation =
                    document.createElement("section");
                navigation.id =
                    "net7cm-mission-navigation";

                const heading =
                    document.createElement("h2");
                heading.textContent =
                    "Mission navigation";
                navigation.appendChild(heading);

                for (const action of actions) {
                    const row =
                        document.createElement("div");
                    row.className =
                        "net7cm-navigation-row";

                    const title =
                        document.createElement("div");
                    title.className =
                        "net7cm-navigation-title";
                    title.textContent =
                        `${action.stepNumber
                            ? `Step ${action.stepNumber} · `
                            : ""}${action.targetName
                                ? `${action.targetName} · `
                                : ""}${action.systemName} / ${action.sectorName}`;

                    const context =
                        document.createElement("div");
                    context.className =
                        "net7cm-navigation-context";
                    context.textContent =
                        action.coordinates
                            ? `${action.coordinates} · ${action.context}`
                            : action.context;

                    row.appendChild(title);
                    row.appendChild(context);
                    row.appendChild(
                        createButton(
                            action,
                            "net7cm-navigation-button",
                            "Set destination"));
                    navigation.appendChild(row);

                    for (const mentionId of action.mentionIds) {
                        const anchor =
                            document.querySelector(
                                `[data-net7cm-mention-id="${mentionId}"]`);

                        if (!anchor ||
                            anchor.parentElement?.querySelector(
                                `.net7cm-inline-route[data-net7cm-action-id="${action.domId}"]`)) {
                            continue;
                        }

                        anchor.insertAdjacentElement(
                            "afterend",
                            createButton(
                                action,
                                "net7cm-inline-route",
                                "Route"));
                    }
                }

                const stepsAnchor =
                    document.getElementById("Steps");
                const stepsHeading =
                    stepsAnchor?.closest(
                        "h2, h3, h4, h5, h6");
                const insertionPoint =
                    stepsHeading ||
                    document.querySelector(
                        "#mw-content-text .mw-parser-output, .mw-parser-output");

                if (stepsHeading?.parentElement) {
                    stepsHeading.parentElement.insertBefore(
                        navigation,
                        stepsHeading);
                } else if (insertionPoint) {
                    insertionPoint.prepend(navigation);
                }

                window.net7cmMissionNavigationResult =
                    (actionId, succeeded, message) => {
                        document.querySelectorAll(
                            `[data-net7cm-action-id="${actionId}"]`)
                            .forEach(button => {
                                button.disabled = succeeded;
                                button.textContent = succeeded
                                    ? "Destination set"
                                    : "Try again";
                                button.title = message || "";
                                button.classList.toggle(
                                    "net7cm-route-success",
                                    succeeded);
                                button.classList.toggle(
                                    "net7cm-route-failure",
                                    !succeeded);
                            });
                    };

                return true;
            })()
            """)
            .ConfigureAwait(true);
    }

    private IReadOnlyList<NavigationActionPayload>
        ResolveNavigationActions(
            IReadOnlyList<PageLinkMention> mentions)
    {
        this.navigationActions.Clear();

        List<NavigationActionPayload> actions = [];
        Dictionary<string, int> actionIndexByDestination =
            new(StringComparer.Ordinal);

        foreach (var mention in mentions)
        {
            if (string.IsNullOrWhiteSpace(mention.MentionId))
            {
                continue;
            }

            var destination =
                this.resolveNavigationDestination(
                    new MissionWikiLocationHint
                    {
                        PageTitle = mention.PageTitle,
                        LinkText = mention.LinkText,
                        Context = mention.Context,
                    });

            if (destination == null)
            {
                continue;
            }

            var destinationIdentity =
                CreateDestinationIdentity(destination);

            if (actionIndexByDestination.TryGetValue(
                    destinationIdentity,
                    out var existingIndex))
            {
                var existing = actions[existingIndex];

                if (!existing.MentionIds.Contains(
                        mention.MentionId,
                        StringComparer.Ordinal))
                {
                    actions[existingIndex] = existing with
                    {
                        MentionIds =
                        [
                            .. existing.MentionIds,
                            mention.MentionId,
                        ],
                    };
                }

                continue;
            }

            var domId = string.Concat(
                "mission-route-",
                this.navigationGeneration.ToString(
                    CultureInfo.InvariantCulture),
                "-",
                actions.Count.ToString(
                    CultureInfo.InvariantCulture));
            var actionToken = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

            this.navigationActions[actionToken] =
                new NavigationActionRegistration(
                    domId,
                    destination);
            actionIndexByDestination[destinationIdentity] =
                actions.Count;

            actions.Add(
                new NavigationActionPayload
                {
                    DomId = domId,
                    ActionToken = actionToken,
                    SectorName = destination.SectorName,
                    SystemName = destination.SystemName,
                    TargetName = destination.Kind ==
                        NavigationDestinationKind.Target
                            ? destination.TargetName
                            : null,
                    StepNumber = mention.StepNumber,
                    Context = string.IsNullOrWhiteSpace(
                        mention.Context)
                        ? mention.LinkText
                        : mention.Context,
                    Coordinates = mention.Coordinates,
                    MentionIds = [mention.MentionId],
                });
        }

        return actions;
    }

    private static string CreateDestinationIdentity(
        NavigationDestination destination)
    {
        return destination.Kind == NavigationDestinationKind.Target &&
               !string.IsNullOrWhiteSpace(destination.TargetKey)
            ? string.Concat("target:", destination.TargetKey)
            : string.Concat("sector:", destination.SectorKey);
    }

    private async Task NotifyNavigationResultAsync(
        string actionId,
        bool succeeded,
        string message)
    {
        var core = this.webView.CoreWebView2;

        if (this.disposed || core is null)
        {
            return;
        }

        var actionIdJson = JsonSerializer.Serialize(actionId);
        var messageJson = JsonSerializer.Serialize(message);
        var succeededJson = succeeded ? "true" : "false";

        try
        {
            _ = await core.ExecuteScriptAsync(
                    string.Concat(
                        "window.net7cmMissionNavigationResult?.(",
                        actionIdJson,
                        ",",
                        succeededJson,
                        ",",
                        messageJson,
                        ");"))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or
                  ObjectDisposedException)
        {
        }
    }

    private void ShowStatus(string text)
    {
        this.statusLabel.Text = text;
        this.statusLabel.Visible = true;
        this.statusLabel.BringToFront();
        this.webView.Visible = false;
    }

    private void ShowFailure(string text)
    {
        this.RuntimeState = AddonRuntimeState.Failed;
        this.StatusText = text;
        this.ShowStatus(text);
        this.SetContentState(MissionWikiContentState.Failed);
    }

    private void SetContentState(MissionWikiContentState state)
    {
        if (this.ContentState == state)
        {
            return;
        }

        this.ContentState = state;
        this.ContentStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OpenExternalBrowser(Uri uri)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
            when (ex is InvalidOperationException or
                  System.ComponentModel.Win32Exception)
        {
        }
    }

    private sealed record PageProbe
    {
        public string? Title { get; init; }

        public bool Challenge { get; init; }

        public bool InvalidTitle { get; init; }

        public bool Missing { get; init; }

        public bool Article { get; init; }
    }

    private sealed record PageLinkMention
    {
        // These properties are populated by System.Text.Json from the
        // page-extraction script. They must remain settable during
        // deserialization even though production code never assigns them.
        public string MentionId { get; init; } = "";

        public string LinkText { get; init; } = "";

        public string PageTitle { get; init; } = "";

        public int? StepNumber { get; init; }

        public string Context { get; init; } = "";

        public string Coordinates { get; init; } = "";
    }

    private sealed record NavigationActionPayload
    {
        public required string DomId { get; init; }

        public required string ActionToken { get; init; }

        public required string SectorName { get; init; }

        public required string SystemName { get; init; }

        public string? TargetName { get; init; }

        public int? StepNumber { get; init; }

        public required string Context { get; init; }

        public string Coordinates { get; init; } = "";

        public IReadOnlyList<string> MentionIds { get; init; } = [];
    }

    private sealed record NavigationRequest
    {
        public string Type { get; init; } = "";

        public string ActionToken { get; init; } = "";
    }

    private sealed record NavigationActionRegistration(
        string DomId,
        NavigationDestination Destination);
}
