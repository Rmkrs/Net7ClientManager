// ReSharper disable LocalizableElement
namespace Net7ClientManager.Forms;

using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;

public sealed partial class MainForm
{
    private HelpCenterForm? helpCenterForm;

    private void OpenHelpCenter(
        IWin32Window? owner,
        HelpLaunchRequest request)
    {
        if (this.IsDisposed || this.Disposing)
        {
            return;
        }

        if (this.InvokeRequired)
        {
            this.BeginInvoke(() => this.OpenHelpCenter(owner, request));
            return;
        }

        try
        {
            this.OpenHelpCenterCore(owner, request);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                string.Concat(
                    "Help & Assistance could not be opened.\n\n",
                    ex.Message),
                "Help & Assistance",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenHelpCenterCore(
        IWin32Window? owner,
        HelpLaunchRequest request)
    {
        if (this.helpCenterForm is { IsDisposed: false })
        {
            this.helpCenterForm.ShowTopic(
                request.TopicId,
                request.ProcessId);

            if (!this.helpCenterForm.Visible)
            {
                this.helpCenterForm.Show(this);
            }

            if (this.helpCenterForm.WindowState ==
                FormWindowState.Minimized)
            {
                this.helpCenterForm.WindowState =
                    FormWindowState.Normal;
            }

            this.helpCenterForm.BringToFront();
            this.helpCenterForm.Activate();
            return;
        }

        var form = new HelpCenterForm(
            this.clientManager,
            this.BuildHelpContexts,
            this.HandleHelpActionAsync,
            owner ?? this);

        this.helpCenterForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(this.helpCenterForm, form))
            {
                this.helpCenterForm = null;
            }
        };

        form.ShowTopic(request.TopicId, request.ProcessId);
        form.Show(this);
        form.Activate();
    }

    private void ShowHelpButton_OnClick(object? sender, EventArgs e)
    {
        this.OpenHelpCenter(
            this,
            new HelpLaunchRequest(HelpTopicIds.Home));
    }

    private void ShowMainScreenHelpTour()
    {
        _ = this.ShowFleetTourFromHelp();
    }

    private IReadOnlyList<HelpClientContext> BuildHelpContexts()
    {
        List<HelpClientContext> contexts =
        [
            new HelpClientContext(
                "general",
                "General help"),
        ];

        var clients = this.clientManager.Clients
            .OrderBy(client => client.ProcessId)
            .ToArray();
        var profile = this.clientManager.ActiveProfile;

        if (profile != null)
        {
            foreach (var slot in profile.Slots)
            {
                var runningClient = clients.FirstOrDefault(client =>
                    client.AssignedSlotId == slot.Id);
                var account = slot.AccountId is { } accountId
                    ? this.clientManager.FindConfiguredAccount(accountId)
                    : null;
                var character = slot.CharacterId is { } characterId
                    ? account?.Characters.FirstOrDefault(candidate =>
                        candidate.Id == characterId)
                    : null;
                var detail = !string.IsNullOrWhiteSpace(character?.Name)
                    ? character.Name.Trim()
                    : runningClient?.LiveCharacterIdentity.Name?.Trim();
                var display = string.IsNullOrWhiteSpace(detail)
                    ? string.Concat("Slot: ", slot.Name)
                    : string.Concat(
                        "Slot: ",
                        slot.Name,
                        " · ",
                        detail);

                contexts.Add(
                    new HelpClientContext(
                        string.Concat("slot:", slot.Id),
                        display,
                        runningClient?.ProcessId,
                        slot.Id));
            }
        }

        foreach (var client in clients.Where(client =>
                     client.AssignedSlotId == null ||
                     profile?.Slots.Any(slot =>
                         slot.Id == client.AssignedSlotId) != true))
        {
            var pilot = client.LiveCharacterIdentity.Name?.Trim();
            contexts.Add(
                new HelpClientContext(
                    string.Concat("process:", client.ProcessId),
                    string.IsNullOrWhiteSpace(pilot)
                        ? string.Concat(
                            "Running client · PID ",
                            client.ProcessId)
                        : string.Concat(
                            "Running client · ",
                            pilot),
                    client.ProcessId));
        }

        return contexts;
    }

    private Task<HelpActionResponse?> HandleHelpActionAsync(
        HelpActionRequest request)
    {
        var response = request.Action switch
        {
            "diagnose:auto-login" =>
                this.DiagnoseAutoLogin(),
            "show:auto-login" =>
                this.ShowAutoLoginIssueFromHelp(),
            "open:auto-login" =>
                this.OpenAutoLoginFromHelp(),
            "show:fleet-tour" =>
                this.ShowFleetTourFromHelp(),
            "diagnose:navigation" =>
                this.DiagnoseNavigation(request.Context),
            "open:navigation" =>
                this.OpenNavigationFromHelp(request.Context),
            "open:atlas" =>
                this.OpenAtlasFromHelp(request.Context),
            "open:finder" =>
                this.OpenFinderFromHelp(request.Context),
            "open:shopping" =>
                this.OpenShoppingFromHelp(request.Context),
            "open:archive" =>
                this.OpenArchiveFromHelp(request.Context),
            "open:builds" =>
                this.OpenBuildsFromHelp(request.Context, openForge: false),
            "open:social" =>
                this.OpenSocialFromHelp(request.Context),
            "show:ingame-menu" =>
                this.ShowInGameMenuFromHelp(request.Context),
            "open:ingame-options" =>
                this.OpenInGameOptionsFromHelp(request.Context),
            "open:game-settings" =>
                this.OpenGameSettingsFromHelp(),
            "diagnose:addons" =>
                this.DiagnoseAddons(request.Context),
            "open:addons" =>
                this.OpenAddonsFromHelp(request.Context),
            "open:forge-contributions" =>
                this.OpenForgeContributionsFromHelp(),
            _ => new HelpActionResponse(
                HelpResultKind.Information,
                "This action is not available yet",
                "The help article is ready, but this shortcut has not been connected in this build."),
        };

        return Task.FromResult<HelpActionResponse?>(response);
    }

    private HelpActionResponse DiagnoseAutoLogin()
    {
        var issue = this.EvaluateAutoLoginReadiness();

        return issue.Kind == AutoLoginReadinessIssueKind.Ready
            ? new HelpActionResponse(
                HelpResultKind.Success,
                issue.Title,
                issue.Message)
            : new HelpActionResponse(
                HelpResultKind.Warning,
                issue.Title,
                issue.Message,
                "show:auto-login");
    }

    private HelpActionResponse ShowAutoLoginIssueFromHelp()
    {
        var issue = this.EvaluateAutoLoginReadiness();

        if (issue.Kind == AutoLoginReadinessIssueKind.Ready)
        {
            return new HelpActionResponse(
                HelpResultKind.Success,
                issue.Title,
                issue.Message);
        }

        this.ShowAutoLoginReadinessIssue(issue);
        return new HelpActionResponse(
            HelpResultKind.Information,
            "The next step is highlighted",
            "Complete the highlighted field or option, then return here and check again.");
    }

    private HelpActionResponse OpenAutoLoginFromHelp()
    {
        this.AutoLoginReadinessButton_OnClick(this, EventArgs.Empty);
        return this.DiagnoseAutoLogin();
    }

    private HelpActionResponse ShowFleetTourFromHelp()
    {
        var steps = new List<GuidedTourStep>
        {
            new(
                () => this.profileCard,
                "Choose a saved fleet profile",
                "The selector changes the complete saved arrangement shown below. New creates another profile; Rename and Duplicate help organise alternatives; Delete removes the selected profile."),
            new(
                () => this.accountsToolsCard,
                "Accounts and shared tools",
                "Manage stores game accounts, passwords, and characters. Game Settings applies supported presentation choices without logging into every pilot, while Pilot Archive opens the lasting records captured from your pilots."),
            new(
                () => this.autoLoginReadinessButton,
                "Check automatic-login readiness",
                "Check setup inspects the real account and slot configuration, then opens and highlights the first missing requirement. Use it whenever a client starts but does not sign in or enter the expected character."),
            new(
                () => this.quickLaunchCard,
                "Quick launch starts an unassigned client",
                "Use Quick launch for a temporary game window that is not tied to a profile slot. Choose the hosted size and game resolution, then start the client without account or character automation."),
            new(
                () => this.slotsSection,
                "Client slots are the managed fleet",
                "Every slot represents one repeatable game window. The area shows its configured account, character, placement, resolution, automation state, and current availability."),
            new(
                () => this.addSlotButton,
                "Add another managed client",
                "Add slot creates another place in the selected profile. The slot editor chooses its account, character, automatic-login behaviour, hosted size, game resolution, title-bar mode and hover delay, and screen position."),
            new(
                () => this.editLayoutButton,
                "Arrange the fleet visually",
                "Edit layout opens the monitor canvas. Move slot rectangles there instead of calculating coordinates by hand. A rectangle includes extra title-bar height only in Always show mode; Hide and Show on hover keep the game-sized footprint."),
            new(
                () => this.keepClientsAliveCheckBox,
                "Replace missing clients automatically",
                "Keep alive watches the selected profile and recreates a configured client when it closes or disappears. Leave it off when you want clients to remain closed."),
            new(
                () => this.createMissingClientsButton,
                "Launch the complete selected profile",
                "Create missing starts every configured slot that is not already running. Existing clients are left alone, so it can also rebuild only the missing parts of a fleet."),
        };

        var slotRuntime = this.GetFirstProfileSlotRuntime();
        if (slotRuntime != null)
        {
            steps.Add(
                new GuidedTourStep(
                    () => slotRuntime.Card,
                    "Read one configured slot",
                    "The card shows the chosen account and character, complete hosted size, game resolution, title-bar mode, screen position, automatic-login state, and whether the client is available, starting, running, or failed."));
            steps.Add(
                new GuidedTourStep(
                    () => slotRuntime.StartButton,
                    "Start only this client",
                    "Start launches this one configured account, character, and window arrangement. It does not start the other slots in the profile."));
            steps.Add(
                new GuidedTourStep(
                    () => slotRuntime.EditButton,
                    "Change this slot",
                    "Edit opens the slot configuration for its account, character, automatic-login options, title-bar mode and hover delay, resolution, and placement."));
            steps.Add(
                new GuidedTourStep(
                    () => slotRuntime.DeleteButton,
                    "Remove this slot from the profile",
                    "Delete removes this saved slot after confirmation. It does not delete the game account or character from Account Management."));
        }

        steps.Add(
            new GuidedTourStep(
                () => this.runningClientsSection,
                "See what is actually running",
                "Running clients reports the live game processes Client Manager can see. This is deliberately separate from the configured slots, so you can compare the planned fleet with what is really on screen."));

        this.ShowMainFormTour(steps);

        return new HelpActionResponse(
            HelpResultKind.Information,
            "Main screen tour opened",
            "Use Previous and Next to walk through every fleet control on the main screen.");
    }

    private ProfileSlotCardRuntime? GetFirstProfileSlotRuntime()
    {
        return this.slotsFlowPanel.Controls
            .OfType<DashboardCardPanel>()
            .Select(card => card.Tag)
            .OfType<ProfileSlotCardRuntime>()
            .FirstOrDefault();
    }

    private void ShowMainFormTour(
        IReadOnlyList<GuidedTourStep> steps)
    {
        var restoreHelpCenter = this.SuspendHelpCenterForTour();

        if (this.WindowState == FormWindowState.Minimized)
        {
            this.WindowState = FormWindowState.Normal;
        }

        if (!this.Visible)
        {
            this.Show();
        }

        this.BringToFront();
        this.Activate();
        this.BeginInvoke(
            () => GuidedTourOverlay.Show(
                this,
                steps,
                tourClosed: restoreHelpCenter));
    }

    private Action? SuspendHelpCenterForTour()
    {
        var form = this.helpCenterForm;
        if (form == null || form.IsDisposed || !form.Visible)
        {
            return null;
        }

        form.Hide();
        return () =>
        {
            if (this.IsDisposed || this.Disposing ||
                form.IsDisposed || form.Disposing)
            {
                return;
            }

            form.Show(this);
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.BringToFront();
            form.Activate();
        };
    }

    private HelpActionResponse DiagnoseNavigation(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client?.HostForm == null)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a running client",
                "Start or select a hosted client, then Help can open its built-in Navigation companion.");
        }

        if (!resolved.Client.HostForm.ShowNavigationCompanionForHelp())
        {
            return new HelpActionResponse(
                HelpResultKind.Error,
                "Navigation could not open",
                "The hosted client is no longer available. Start the client again and retry.");
        }

        return new HelpActionResponse(
            HelpResultKind.Success,
            "Navigation companion opened",
            "Move it beside the game, below it, or to another monitor. Its position and size are remembered for this client slot.");
    }

    private HelpActionResponse OpenNavigationFromHelp(
        HelpClientContext context)
    {
        var processId = this.ResolveHelpContext(context).Client?.ProcessId ??
                        context.ProcessId;
        this.ShowNavigationPlanner(processId);
        var form = this.navigationPlannerForm;
        form?.BeginInvoke(() => form.ShowHelpTour());
        return new HelpActionResponse(
            HelpResultKind.Information,
            "Route Planner opened",
            "Choose a destination or inspect the selected pilot's active route.");
    }

    private HelpActionResponse OpenAtlasFromHelp(
        HelpClientContext context)
    {
        var processId = this.ResolveHelpContext(context).Client?.ProcessId ??
                        context.ProcessId;
        this.ShowGalaxyAtlas(processId);
        var form = this.galaxyAtlasForm;
        form?.BeginInvoke(() => form.ShowHelpTour());
        return new HelpActionResponse(
            HelpResultKind.Information,
            "Galaxy Atlas opened",
            "Explore the galaxy or set a destination for the selected pilot.");
    }

    private HelpActionResponse OpenFinderFromHelp(
        HelpClientContext context)
    {
        var processId = this.ResolveHelpContext(context).Client?.ProcessId ??
                        context.ProcessId;
        this.ShowWorldFind(processId);
        var form = this.worldFindForm;
        form?.BeginInvoke(() => form.ShowFinderHelpTour());
        return new HelpActionResponse(
            HelpResultKind.Information,
            "Galaxy Finder opened",
            "Search places, NPCs, mobs, resources, or items.");
    }

    private HelpActionResponse OpenShoppingFromHelp(
        HelpClientContext context)
    {
        var processId = this.ResolveHelpContext(context).Client?.ProcessId ??
                        context.ProcessId;
        this.ShowWorldFind(processId);
        var form = this.worldFindForm;
        form?.ShowShoppingListFromHelp();
        form?.BeginInvoke(() => form.ShowShoppingHelpTour());
        return new HelpActionResponse(
            HelpResultKind.Information,
            "Shopping List opened",
            "Add requested outputs or inspect the acquisition plan for an existing list.");
    }

    private HelpActionResponse OpenArchiveFromHelp(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);
        this.clientManager.OpenPilotArchiveForHelp(
            this,
            resolved.Client?.LiveCharacterIdentity.CharacterObjectId);
        return new HelpActionResponse(
            HelpResultKind.Information,
            "Pilot Archive opened",
            "Choose an archived pilot to inspect equipment, missions, activity, combat, reputation, and builds.");
    }

    private HelpActionResponse OpenBuildsFromHelp(
        HelpClientContext context,
        bool openForge)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client?.HostForm == null ||
            resolved.Client.LifecycleState != ClientLifecycleState.InGame)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a pilot who is in game",
                "Builds use the selected pilot's live equipment and skills. Start a client, enter the game, then select it under Help for.");
        }

        if (!resolved.Client.HostForm.ShowBuildBoardForHelp(openForge))
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Build data is still loading",
                "Wait until the pilot is fully in game and Client Manager has read its equipment and skills, then try again.");
        }

        return new HelpActionResponse(
            HelpResultKind.Information,
            openForge ? "Forge build search opened" : "Build Board opened",
            openForge
                ? "Search for a published guide, inspect it, and choose the version you want to follow."
                : "The introduction shows milestones, missing equipment, skill targets, and requirements.");
    }

    private HelpActionResponse OpenSocialFromHelp(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client == null ||
            !this.clientManager.OpenSocialForHelp(
                resolved.Client.ProcessId,
                this))
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a pilot who is in game",
                "Social settings belong to the active pilot. Start a client, enter the game, then select it under Help for.");
        }

        return new HelpActionResponse(
            HelpResultKind.Information,
            "Social opened",
            "The introduction shows presence, location privacy, Looking for Guild, and recruitment.");
    }

    private HelpActionResponse ShowInGameMenuFromHelp(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client?.HostForm == null ||
            resolved.Client.LifecycleState != ClientLifecycleState.InGame)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a pilot who is in game",
                "The Client Manager menu lives inside a hosted game window. Start a client, enter the game, then select it under Help for.");
        }

        if (!resolved.Client.HostForm.ShowBuiltInMenuGuidance("menu"))
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "The in-game menu is not ready yet",
                "Wait for the hosted game view to finish loading, then try again.");
        }

        return new HelpActionResponse(
            HelpResultKind.Information,
            "The Client Manager menu is highlighted",
            "Use this menu to open Navigation, Atlas, Finder, Social, Pilot Archive, Builds, Addon Center, Help, and Options.");
    }

    private HelpActionResponse OpenInGameOptionsFromHelp(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client?.HostForm == null ||
            resolved.Client.LifecycleState != ClientLifecycleState.InGame)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a pilot who is in game",
                "In-game Options belong to a hosted client that has reached the game.");
        }

        this.BeginInvoke(() => this.OpenInGameOptions(
            resolved.Client.ProcessId,
            resolved.Client.HostForm,
            showTour: true));

        return new HelpActionResponse(
            HelpResultKind.Information,
            "In-game Options opening",
            "The introduction will show the menu, Command Palette, histories, helpers, and enhanced tooltips.");
    }

    private HelpActionResponse OpenGameSettingsFromHelp()
    {
        var restoreHelpCenter = this.SuspendHelpCenterForTour();
        this.BeginInvoke(() =>
        {
            try
            {
                this.OpenGameSettings(showTour: true);
            }
            finally
            {
                restoreHelpCenter?.Invoke();
            }
        });

        return new HelpActionResponse(
            HelpResultKind.Information,
            "Game settings opening",
            "The introduction will show how to compare and synchronize settings across your fleet.");
    }

    private HelpActionResponse OpenForgeContributionsFromHelp()
    {
        var restoreHelpCenter = this.SuspendHelpCenterForTour();

        try
        {
            this.clientManager.OpenForgeContributionsForHelp(
                this,
                restoreHelpCenter);
        }
        catch
        {
            restoreHelpCenter?.Invoke();
            throw;
        }

        return new HelpActionResponse(
            HelpResultKind.Information,
            "Forge Contributions opening",
            "The introduction will show contribution choices, privacy, shared world-data updates, and the activity overview.");
    }

    private HelpActionResponse DiagnoseAddons(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client == null)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a running client",
                "Addon enablement belongs to a client slot. Select a running client or slot in Help for.");
        }

        var statuses = this.clientManager.GetAddonStatuses(
            resolved.Client.ProcessId);
        var enabled = statuses.Count(status => status.IsEnabled);
        var running = statuses.Count(status =>
            status.State == AddonRuntimeState.Running);
        var failed = statuses.Count(status =>
            status.State is AddonRuntimeState.Failed or
                AddonRuntimeState.Unavailable);

        if (this.clientManager.AddonsSuspendedForSession)
        {
            return new HelpActionResponse(
                HelpResultKind.Warning,
                "Addons are temporarily suspended",
                "Resume addons in Addon Center to let enabled addons run again.",
                "open:addons",
                "Open Addon Center");
        }

        if (failed > 0)
        {
            return new HelpActionResponse(
                HelpResultKind.Warning,
                "Some addons need attention",
                string.Concat(
                    failed,
                    failed == 1
                        ? " addon could not start."
                        : " addons could not start."),
                "open:addons",
                "Open Addon Center");
        }

        return new HelpActionResponse(
            HelpResultKind.Success,
            "Addon setup looks healthy",
            string.Concat(
                enabled,
                " enabled · ",
                running,
                " running for the selected client."));
    }

    private HelpActionResponse OpenAddonsFromHelp(
        HelpClientContext context)
    {
        var resolved = this.ResolveHelpContext(context);

        if (resolved.Client == null)
        {
            return new HelpActionResponse(
                HelpResultKind.Information,
                "Choose a running client",
                "Select the client whose Addon Center should be opened.");
        }

        this.clientManager.OpenAddonCenterForHelp(
            resolved.Client.ProcessId,
            this,
            addonId: null,
            discover: false);

        return new HelpActionResponse(
            HelpResultKind.Information,
            "Addon Center opened",
            "Install, enable, update, or inspect addons for the selected client.");
    }

    private ResolvedHelpContext ResolveHelpContext(
        HelpClientContext context)
    {
        var clients = this.clientManager.Clients;
        var client = context.ProcessId is { } processId
            ? clients.FirstOrDefault(candidate =>
                candidate.ProcessId == processId)
            : context.SlotId is { } slotId
                ? clients.FirstOrDefault(candidate =>
                    candidate.AssignedSlotId == slotId)
                : clients.Count == 1
                    ? clients.First()
                    : null;
        var slot = context.SlotId is { } explicitSlotId
            ? this.clientManager.ActiveProfile?.Slots.FirstOrDefault(
                candidate => candidate.Id == explicitSlotId)
            : client == null
                ? null
                : this.clientManager.GetAssignedSlot(client);

        return new ResolvedHelpContext(client, slot);
    }

    private sealed record ResolvedHelpContext(
        ClientInstance? Client,
        ClientSlot? Slot);

}
