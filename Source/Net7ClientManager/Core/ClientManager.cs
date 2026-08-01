// ReSharper disable StringLiteralTypo
// ReSharper disable LocalizableElement
// ReSharper disable CommentTypo
namespace Net7ClientManager.Core;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Net7ClientManager.Addons.Contracts;
using Net7ClientManager.Addons.Development;
using Net7ClientManager.Addons.Runtime;
using Net7ClientManager.Addons.Registry;
using Net7ClientManager.ActivityJournal;
using Net7ClientManager.CombatJournal;
using Net7ClientManager.Contributions;
using Net7ClientManager.Forms;
using Net7ClientManager.GalaxyKnowledge;
using Net7ClientManager.Models;
using Net7ClientManager.MissionJournal;
using Net7ClientManager.Navigation;
using Net7ClientManager.Observations;
using Net7ClientManager.Observations.Models;
using Net7ClientManager.PilotArchive;
using Net7ClientManager.Services;
using Net7ClientManager.Shopping;
using Net7ClientManager.SkillPlanning;
using Net7ClientManager.Social;
using Net7ClientManager.Win32;

public sealed class ClientManager : IDisposable
{
    private const string UiCommandArgument = "uiCommand";
    private const string GroupSkillsUiCommand = "group-skills";
    private const string FleetLootUiCommand = "fleet-loot";

    private readonly System.Threading.Lock lockObject = new();
    private readonly System.Threading.Lock settingsSaveLock = new();
    private readonly Dictionary<int, ClientInstance> clients = [];
    private readonly ClientProcessWatcher clientProcessWatcher;
    private readonly ClientWindowFinder clientWindowFinder = new();
    private readonly ClientDockingService clientDockingService = new();
    private readonly System.Windows.Forms.Timer clientWindowTimer;
    private readonly System.Windows.Forms.Timer hostedClientActivationTimer;
    private bool hostedClientMouseWasDown;
    private readonly SettingsStore settingsStore = new();
    private readonly GameRenderResolutionOverrideCoordinator
        gameRenderResolutionOverrideCoordinator;
    private readonly AppSettings settings;
    private LauncherAutomationSession? launcherSession;
    private int? gameRenderResolutionOverrideProcessId;
    private DateTimeOffset? gameRenderResolutionRestoreDeadline;
    private DateTimeOffset? expectManagerStartedClientUntil;
    private static readonly TimeSpan managerStartedClientDetectionWindow = TimeSpan.FromSeconds(60);

    // LaunchNet7 is effectively singleton. A newly started relay process can
    // exit after handing control to the already-running launcher instance.
    private static readonly TimeSpan launcherSingletonRebindGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan launcherWindowTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan launcherReadinessTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan launcherReadyStabilityDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan launcherPlayInvocationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan launcherPlayAcceptanceDelay = TimeSpan.FromSeconds(10);
    private const int LauncherPlayAttemptLimit = 2;
    private static readonly TimeSpan introSkipClickInterval = TimeSpan.FromMilliseconds(750);

    private static readonly TimeSpan navigationDataAutomaticCheckInterval =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan navigationDataStartupCheckDelay =
        TimeSpan.FromSeconds(30);

    // CharacterSelection means the screen exists, but the UI still needs a
    // brief settle before selecting a slot. After selection, the character
    // performs a presentation animation before Enter Game becomes reliable.
    // These are bounded input-pacing delays, not lifecycle detection.
    private static readonly TimeSpan characterSelectionScreenSettleDelay =
        TimeSpan.FromMilliseconds(2000);

    private static readonly TimeSpan characterSelectionAnimationDelay =
        TimeSpan.FromMilliseconds(1250);

    // ForegroundLeftClick can confirm only that Windows emitted the input. It
    // cannot confirm that the game accepted a click while the button was still
    // disabled. Retry only Enter Game while CharacterSelection remains visible.
    private static readonly TimeSpan enterGameRetryInterval =
        TimeSpan.FromSeconds(1);

    private static readonly TimeSpan enterGameAttemptTimeout =
        TimeSpan.FromSeconds(12);

    private static readonly TimeSpan groupSkillsInputSettleDelay =
        TimeSpan.FromMilliseconds(75);

    private static readonly TimeSpan groupSkillsTargetVerifyTimeout =
        TimeSpan.FromMilliseconds(750);

    private static readonly TimeSpan groupSkillsTargetVerifyPollInterval =
        TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan fleetLootTargetVerifyTimeout =
        TimeSpan.FromMilliseconds(1000);

    private static readonly TimeSpan fleetLootTargetVerifyPollInterval =
        TimeSpan.FromMilliseconds(50);

    // Target acquisition updates the observed target before the game's verb
    // button is reliably ready. This is the same old-client settle boundary
    // proven by Auto Pilot for Dock/Gate/Land interactions.
    private static readonly TimeSpan fleetLootTargetVerbSettleDelay =
        TimeSpan.FromMilliseconds(1000);

    private static readonly TimeSpan fleetLootWindowHydrationTimeout =
        TimeSpan.FromMilliseconds(2500);

    // The loot-session observer can become active slightly before the game has
    // finished laying out clickable loot rows. Only the first item of a newly
    // opened corpse pays this small UI settle cost.
    private static readonly TimeSpan fleetLootWindowInputSettleDelay =
        TimeSpan.FromMilliseconds(1000);

    private static readonly TimeSpan fleetLootWindowHydrationPollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan fleetLootTractorStartTimeout =
        TimeSpan.FromMilliseconds(1200);

    private static readonly TimeSpan fleetLootTractorCompleteTimeout =
        TimeSpan.FromSeconds(12);

    private static readonly TimeSpan fleetLootTractorPollInterval =
        TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan groupSkillsShortcutBankSettleTimeout =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan groupSkillsShortcutBankSettlePollInterval =
        TimeSpan.FromMilliseconds(25);

    private static readonly TimeSpan fleetFormationMenuSettleDelay =
        TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan fleetFormationSelectionSettleDelay =
        TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan fleetFormationObservationTimeout =
        TimeSpan.FromMilliseconds(2500);

    private static readonly TimeSpan fleetFormationJoinTimeout =
        TimeSpan.FromSeconds(4);

    private static readonly TimeSpan fleetFormationPollInterval =
        TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan fleetFormationJoinStaggerDelay =
        TimeSpan.FromMilliseconds(250);
    private readonly GameAccountStore gameAccountStore = new();
    private List<GameAccount> accounts;
    private bool createMissingClientsRequested;
    private DateTimeOffset? nextMissingClientStartAllowedAt;
    private IWin32Window? automationOwner;
    private static readonly TimeSpan missingClientStartCooldown = TimeSpan.FromSeconds(2);
    private const string LoginScreenUsernameClickActionName = "Login Screen Username";

    private readonly FleetCommandService fleetCommandService;
    private readonly SemaphoreSlim fleetFormationGate = new(1, 1);
    private readonly ClientObservationCoordinator clientObservationCoordinator = new();
    private readonly ForegroundInputCoordinator foregroundInputCoordinator = new();
    private readonly GameKeyMapLocator gameKeyMapLocator;
    private readonly HashSet<int> gameInstallationObservedProcessIds = [];
    private readonly SkillIniCatalogService skillIniCatalogService;
    private readonly SkillPlannerCatalogService skillPlannerCatalogService;
    private readonly SkillBuildHullCatalogService skillBuildHullCatalogService;
    private readonly SkillBuildLocalStore skillBuildLocalStore;
    private readonly SkillBuildLocalWorkspace skillBuildLocalWorkspace;
    private readonly SkillBuildBoardPresentationBuilder
        pilotArchiveBuildPresentationBuilder;
    private readonly GameKeyBindingResolver gameKeyBindingResolver;
    private readonly CommandPaletteHotKeyValidator commandPaletteHotKeyValidator;
    private readonly GameCommandCoordinator gameCommandCoordinator;
    private readonly GameShortcutPaletteService gameShortcutPaletteService;
    private readonly GameIconService gameIconService;
    private readonly GameShortcutInvocationService gameShortcutInvocationService;
    private readonly NearbyTargetSelectionService nearbyTargetSelectionService;
    private readonly NavigationTargetSelectionService navigationTargetSelectionService;
    private readonly NavigationAutoPilotCoordinator navigationAutoPilotCoordinator;
    private readonly NavigationDataStore navigationDataStore = new();
    private readonly GalaxyKnowledgeCoordinator galaxyKnowledgeCoordinator;
    private readonly ForgeNavigationDataClient forgeNavigationDataClient = new();
    private readonly ForgeProductionRecipeCatalogStore
        forgeProductionRecipeCatalogStore = new();
    private ForgeProductionRecipeCatalogSnapshot
        forgeProductionRecipeCatalog =
            ForgeProductionRecipeCatalogSnapshot.Unavailable();
    private readonly ForgeMissionCatalogStore
        forgeMissionCatalogStore = new();
    private ForgeMissionCatalogSnapshot forgeMissionCatalog =
        ForgeMissionCatalogSnapshot.Unavailable();
    private readonly ForgeNavigationUpdatePackageReader forgeNavigationUpdatePackageReader = new();
    private readonly CancellationTokenSource navigationUpdateCancellation = new();
    private readonly System.Threading.Lock navigationUpdateTaskLock = new();
    private Task? navigationUpdateTask;
    private bool navigationUpdateCheckRequested;
    private DateTimeOffset nextAutomaticNavigationUpdateCheckAt =
        DateTimeOffset.MaxValue;
    private readonly AddonRuntimeCoordinator addonRuntimeCoordinator;
    private readonly Dictionary<int, AddonCenterForm> addonCenterForms = [];
    private readonly Dictionary<int, GroupSkillsForm> groupSkillsForms = [];
    private readonly Dictionary<int, FleetLootWindowForm> fleetLootWindowForms = [];
    private readonly Dictionary<int, int> sessionLootOwnerByLeaderProcessId = [];
    private readonly Dictionary<int, bool> roundRobinLootEnabledByLeaderProcessId = [];
    private readonly Dictionary<int, FleetLootCorpseAssignment> activeLootAssignmentByLeaderProcessId = [];
    private readonly ForgeContributionCoordinator forgeContributionCoordinator;
    private readonly MissionJournalCoordinator missionJournalCoordinator;
    private readonly ActivityJournalCoordinator activityJournalCoordinator;
    private readonly CombatJournalCoordinator combatJournalCoordinator;
    private readonly SocialCoordinator socialCoordinator;
    private ForgeContributionStatisticsSnapshot?
        addonLastForgeContributionSession;
    private SocialForm? socialForm;
    private ForgeContributionsForm? forgeContributionsForm;
    private readonly PilotArchiveStore pilotArchiveStore;
    private readonly PilotArchiveCoordinator pilotArchiveCoordinator;
    private readonly ShoppingListStore shoppingListStore;
    private readonly ShoppingListCoordinator shoppingListCoordinator;
    private readonly VendorShoppingCompanionCoordinator
        vendorShoppingCompanionCoordinator = new();
    private PilotArchiveForm? pilotArchiveForm;
    private SkillBuildBoardForm? pilotArchiveBuildBoardForm;
    private ManagedClientLaunchRequest? pendingManagedClientLaunch;

    // A failed Keep Alive/Create Missing launch must not silently loop. The
    // slot remains retryable by an explicit Start/Create Missing action.
    private readonly Dictionary<Guid, string> managedClientLaunchFailures = [];
    private readonly LayoutProfile noProfile = new()
    {
        Id = Guid.Empty,
        Name = "No Profile",
    };
    private bool addonsSuspendedForSession;

    public ClientManager()
    {
        this.gameRenderResolutionOverrideCoordinator = new();
        this.gameKeyMapLocator = new GameKeyMapLocator();
        this.skillIniCatalogService =
            new SkillIniCatalogService(this.gameKeyMapLocator);

        this.skillPlannerCatalogService =
            new SkillPlannerCatalogService();
        this.skillBuildHullCatalogService =
            new SkillBuildHullCatalogService();
        this.skillBuildLocalStore = new SkillBuildLocalStore();
        this.skillBuildLocalStore.Initialize();
        this.skillBuildLocalWorkspace =
            new SkillBuildLocalWorkspace(
                this.skillPlannerCatalogService.GetCatalog(),
                this.skillBuildHullCatalogService.GetCatalog(),
                this.skillBuildLocalStore);
        this.pilotArchiveBuildPresentationBuilder =
            new SkillBuildBoardPresentationBuilder(
                this.skillPlannerCatalogService.GetCatalog());

#if DEBUG
        SkillBuildLocalStoreScenarios.Validate();
        SkillBuildBoardScenarios.Validate(
            this.skillPlannerCatalogService.GetCatalog(),
            this.skillBuildHullCatalogService.GetCatalog());
        ShoppingListScenarios.Validate();
#endif

        this.gameKeyBindingResolver =
            new GameKeyBindingResolver(this.gameKeyMapLocator);

        this.commandPaletteHotKeyValidator =
            new CommandPaletteHotKeyValidator(this.gameKeyMapLocator);

        this.gameCommandCoordinator =
            new GameCommandCoordinator(
                this.gameKeyBindingResolver,
                this.foregroundInputCoordinator);

        this.gameShortcutPaletteService =
            new GameShortcutPaletteService(
                this.gameKeyMapLocator,
                this.skillIniCatalogService);

        this.gameIconService =
            new GameIconService(this.gameKeyMapLocator);

        this.gameShortcutInvocationService =
            new GameShortcutInvocationService(
                this.gameCommandCoordinator,
                this.clientObservationCoordinator);

        this.fleetCommandService =
            new FleetCommandService(
                this.gameCommandCoordinator,
                this.foregroundInputCoordinator,
                this.clientObservationCoordinator);

        var navigationLoad = this.navigationDataStore.InitializeAndLoad();
        this.NavigationData = navigationLoad.DataSet;
        this.forgeProductionRecipeCatalog =
            this.forgeProductionRecipeCatalogStore.Load();
        this.forgeMissionCatalog =
            this.forgeMissionCatalogStore.Load();
        this.galaxyKnowledgeCoordinator =
            new GalaxyKnowledgeCoordinator(
                this.NavigationData,
                this.forgeProductionRecipeCatalog,
                this.forgeMissionCatalog);
        this.galaxyKnowledgeCoordinator.SnapshotChanged +=
            this.GalaxyKnowledgeCoordinator_OnSnapshotChanged;

        this.NavigationRoutes =
            new NavigationRouteCoordinator(
                this.NavigationData.Topology,
                this.NavigationData.Catalog);

        this.nearbyTargetSelectionService =
            new NearbyTargetSelectionService(
                this.clientObservationCoordinator,
                this.foregroundInputCoordinator);

        this.navigationTargetSelectionService =
            new NavigationTargetSelectionService(
                this.clientObservationCoordinator,
                this.NavigationRoutes,
                this.gameCommandCoordinator);

        var navigationWormholeAutomationService =
            new NavigationWormholeAutomationService(
                this.clientObservationCoordinator,
                this.gameShortcutPaletteService,
                this.foregroundInputCoordinator);

        this.navigationAutoPilotCoordinator =
            new NavigationAutoPilotCoordinator(
                this.clientObservationCoordinator,
                this.NavigationRoutes,
                this.navigationTargetSelectionService,
                this.gameCommandCoordinator,
                this.foregroundInputCoordinator,
                navigationWormholeAutomationService);

        this.addonRuntimeCoordinator =
            new AddonRuntimeCoordinator(
                this.ExecuteAddonActionAsync);

        this.settings = this.settingsStore.Load();
        this.pilotArchiveStore = new PilotArchiveStore();
        this.pilotArchiveStore.Initialize();
        this.pilotArchiveCoordinator = new PilotArchiveCoordinator(
            this.pilotArchiveStore);
        this.shoppingListStore = new ShoppingListStore();
        this.shoppingListStore.Initialize();
        this.shoppingListCoordinator = new ShoppingListCoordinator(
            this.shoppingListStore,
            this.pilotArchiveStore,
            () => this.GalaxyKnowledge,
            this.GetClientObservationSnapshots);
        var missionJournalStore = new MissionJournalStore();
        missionJournalStore.Initialize();
        this.missionJournalCoordinator = new MissionJournalCoordinator(
            missionJournalStore,
            this.settings.History.RecordMissionHistory);
        var activityJournalStore = new ActivityJournalStore();
        activityJournalStore.Initialize();
        this.activityJournalCoordinator = new ActivityJournalCoordinator(
            activityJournalStore,
            this.settings.History.RecordActivityHistory);
        this.activityJournalCoordinator.EntryRecorded +=
            this.ActivityJournalCoordinator_OnEntryRecorded;
        var combatJournalStore = new CombatJournalStore();
        combatJournalStore.Initialize();
        this.combatJournalCoordinator = new CombatJournalCoordinator(
            combatJournalStore,
            this.settings.History.RecordCombatHistory,
            this.settings.History.RecordActivityHistory);
        this.missionJournalCoordinator.LifecycleEvent +=
            this.MissionJournalCoordinator_OnLifecycleEvent;
        this.combatJournalCoordinator.EncounterEnded +=
            this.CombatJournalCoordinator_OnEncounterEnded;
        this.forgeContributionCoordinator =
            new ForgeContributionCoordinator(
                this.settings.ForgeContributions,
                this.NavigationData,
                this.ResolveAnyLiveForgePilotName,
                this.IsForgePilotLive,
                this.SaveSettings);
        this.forgeContributionCoordinator.RevisionPublished +=
            this.ForgeContributionCoordinator_OnRevisionPublished;
        this.forgeContributionCoordinator.StatisticsChanged +=
            this.ForgeContributionCoordinator_OnStatisticsChanged;
        this.addonLastForgeContributionSession =
            this.forgeContributionCoordinator.GetStatistics().Session;
        this.socialCoordinator = new SocialCoordinator(
            this.settings.Social,
            this.NavigationData,
            this.forgeContributionCoordinator.EnsureMutationIdentityAsync,
            () => this.forgeContributionCoordinator.HasIdentity,
            this.SaveSettings);
        this.socialCoordinator.SnapshotRefreshed +=
            this.SocialCoordinator_OnSnapshotRefreshed;
        this.accounts = this.gameAccountStore.Load();

        this.clientProcessWatcher = new ClientProcessWatcher(this.ClientProcessStarted, this.ClientProcessStopped);

        this.clientWindowTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
        };

        this.clientWindowTimer.Tick += this.ClientWindowTimer_OnTick;

        this.hostedClientActivationTimer = new System.Windows.Forms.Timer
        {
            Interval = 16,
        };

        this.hostedClientActivationTimer.Tick +=
            this.HostedClientActivationTimer_OnTick;

        this.clientObservationCoordinator.ChatMessageObserved +=
            this.ClientObservationCoordinator_OnChatMessageObserved;

        this.clientObservationCoordinator.SnapshotChanged +=
            this.ClientObservationCoordinator_OnSnapshotChanged;

        this.clientObservationCoordinator.MissionPresentationChanged +=
            this.ClientObservationCoordinator_OnMissionPresentationChanged;

        this.clientObservationCoordinator.FactionPresentationChanged +=
            this.ClientObservationCoordinator_OnFactionPresentationChanged;

        this.clientObservationCoordinator.SkillPresentationChanged +=
            this.ClientObservationCoordinator_OnSkillPresentationChanged;

        this.clientObservationCoordinator.InventoryPresentationChanged +=
            this.ClientObservationCoordinator_OnInventoryPresentationChanged;

        this.clientObservationCoordinator.TooltipHoverChanged +=
            this.ClientObservationCoordinator_OnTooltipHoverChanged;

        this.addonRuntimeCoordinator.UiCommandEmitted +=
            this.AddonRuntimeCoordinator_OnUiCommandEmitted;

        this.NavigationRoutes.RouteChanged +=
            this.NavigationRouteCoordinator_OnRouteChanged;

        this.navigationAutoPilotCoordinator.StateChanged +=
            this.NavigationAutoPilotCoordinator_OnStateChanged;
    }

    public IReadOnlyCollection<ClientInstance> Clients
    {
        get
        {
            lock (this.lockObject)
            {
                return [.. this.clients.Values];
            }
        }
    }

    public LayoutProfile? ActiveProfile => this.settings.GetCurrentProfile();

    public LayoutProfile CurrentProfile => this.ActiveProfile ?? this.noProfile;

    public bool HasActiveProfile => this.ActiveProfile != null;

    /// <summary>
    /// Optional pre-game account and character-selection configuration.
    /// Never use this collection as runtime character identity.
    /// </summary>
    public IReadOnlyList<GameAccount> ConfiguredAccounts => this.accounts;

    public bool KeepClientsAlive =>
        this.ActiveProfile?.KeepClientsAlive == true;

    public bool IsManagedClientLaunchInProgress =>
        this.IsWaitingForExpectedManagerStartedClient() ||
        this.launcherSession != null ||
        this.pendingManagedClientLaunch != null ||
        this.gameRenderResolutionOverrideCoordinator.HasActiveOverride;

    public MainWindowSettings MainWindowSettings =>
        this.settings.MainWindow;

    public QuickLaunchSettings QuickLaunchSettings =>
        this.settings.QuickLaunch;

    public FleetCommandSettings FleetCommandSettings => this.settings.FleetCommands;

    public NavigationPlannerSettings NavigationPlannerSettings =>
        this.settings.NavigationPlanner;

    public GalaxyAtlasSettings GalaxyAtlasSettings =>
        this.settings.GalaxyAtlas;

    public WorldFindSettings WorldFindSettings =>
        this.settings.WorldFind;

    public ForgeContributionSettings ForgeContributionSettings =>
        this.settings.ForgeContributions;

    internal ForgeIdentityStatusSnapshot GetForgeIdentityStatus() =>
        this.forgeContributionCoordinator.GetIdentityStatus();

    internal Task<ForgeIdentityStatusSnapshot> BeginForgeIdentityRecoveryAsync(
        string livePilotName,
        CancellationToken cancellationToken = default) =>
        this.forgeContributionCoordinator.BeginIdentityRecoveryAsync(
            livePilotName,
            cancellationToken);

    internal Task<ForgeIdentityStatusSnapshot> RefreshForgeIdentityRecoveryAsync(
        CancellationToken cancellationToken = default) =>
        this.forgeContributionCoordinator.RefreshIdentityRecoveryAsync(
            cancellationToken);

    internal IReadOnlyList<string> GetLiveForgePilots()
    {
        lock (this.lockObject)
        {
            return [.. this.clients.Values
                .Where(client =>
                    client.LifecycleState == ClientLifecycleState.InGame &&
                    !string.IsNullOrWhiteSpace(client.LiveCharacterIdentity.Name))
                .Select(client => client.LiveCharacterIdentity.Name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public PilotArchiveSettings PilotArchiveSettings =>
        this.settings.PilotArchive;

    public HistorySettings HistorySettings =>
        this.settings.History;

    public GameItemToolTipSettings GameItemToolTipSettings =>
        this.settings.GameItemToolTips;

    public GameSettingsEditorSettings GameSettingsEditorSettings =>
        this.settings.GameSettingsEditor;

    public SocialSettings SocialSettings =>
        this.settings.Social;

    public SocialDataSnapshot GetSocialSnapshot()
    {
        return this.socialCoordinator.GetSnapshot();
    }

    public IReadOnlyList<SocialLocalPilot> GetSocialLocalPilots()
    {
        return this.socialCoordinator.GetLocalPilots();
    }

    public SocialPresenceFreshness GetSocialPresenceFreshness(
        DateTimeOffset updatedAtUtc)
    {
        return this.socialCoordinator.GetFreshness(updatedAtUtc);
    }

    public SocialPilotSettings GetSocialPilotSettings(string pilotName)
    {
        return this.socialCoordinator.GetOrCreatePilotSettings(pilotName);
    }

    public SocialGuildRecruitmentSettings GetSocialGuildSettings(
        string guildName,
        string publishingPilotName)
    {
        return this.socialCoordinator.GetOrCreateGuildSettings(
            guildName,
            publishingPilotName);
    }

    public Task SaveSocialPilotSettingsAsync(
        string pilotName,
        bool publishLookingForGuild,
        CancellationToken cancellationToken = default)
    {
        return this.socialCoordinator.SavePilotAsync(
            pilotName,
            publishLookingForGuild,
            cancellationToken);
    }

    public Task SaveSocialGuildSettingsAsync(
        string guildName,
        CancellationToken cancellationToken = default)
    {
        return this.socialCoordinator.SaveGuildAsync(
            guildName,
            cancellationToken);
    }

    public Task RefreshSocialAsync(
        CancellationToken cancellationToken = default)
    {
        return this.socialCoordinator.RefreshAsync(cancellationToken);
    }

    public IReadOnlyList<WorldSearchMatch> SearchSocial(
        string query,
        WorldSearchKind? kind,
        int maximumResults)
    {
        return SocialWorldSearch.Search(
            this.socialCoordinator.GetSnapshot(),
            this.NavigationData,
            query,
            kind,
            updatedAtUtc => this.socialCoordinator.GetFreshness(updatedAtUtc),
            maximumResults);
    }

    public PilotArchiveStore PilotArchive =>
        this.pilotArchiveStore;

    public IReadOnlyList<ShoppingListSummary> GetShoppingLists() =>
        this.shoppingListCoordinator.GetLists();

    public ShoppingListDocument? GetShoppingList(string listId) =>
        this.shoppingListCoordinator.GetList(listId);

    public ShoppingListDocument CreateShoppingList(string name) =>
        this.shoppingListCoordinator.Create(name);

    public ShoppingListDocument? GetPreferredShoppingList() =>
        this.shoppingListCoordinator.GetPreferredList();

    public ShoppingListDocument AddShoppingListRequestedOutput(
        int itemTemplateId,
        long quantity = 1,
        string? listId = null) =>
        this.shoppingListCoordinator.AddRequestedOutput(
            itemTemplateId,
            quantity,
            listId);

    public ShoppingListDocument SaveShoppingList(
        ShoppingListDocument document) =>
        this.shoppingListCoordinator.Save(document);

    public bool DeleteShoppingList(string listId) =>
        this.shoppingListCoordinator.Delete(listId);

    public string? GetActiveShoppingListId() =>
        this.shoppingListCoordinator.GetActiveListId();

    public void SetActiveShoppingList(string? listId) =>
        this.shoppingListCoordinator.SetActiveList(listId);

    public ShoppingPlanSnapshot? BuildShoppingPlan(
        string listId,
        uint? activeCharacterId = null,
        int? activeProcessId = null) =>
        this.shoppingListCoordinator.BuildPlan(
            listId,
            activeCharacterId,
            activeProcessId);

    public ShoppingPlanSnapshot? BuildActiveShoppingPlan(
        uint? activeCharacterId = null,
        int? activeProcessId = null) =>
        this.shoppingListCoordinator.BuildActivePlan(
            activeCharacterId,
            activeProcessId);

    public event EventHandler<ShoppingListsChangedEventArgs>?
        ShoppingListsChanged
    {
        add => this.shoppingListCoordinator.Changed += value;
        remove => this.shoppingListCoordinator.Changed -= value;
    }

    public IReadOnlyList<MissionJournalEntry> GetMissionHistory(
        uint characterId,
        int maximumResults = 1000) =>
        this.missionJournalCoordinator.GetHistory(
            characterId,
            maximumResults);

    public DateTimeOffset? GetMissionHistoryLastObservedAt(
        uint characterId) =>
        this.missionJournalCoordinator.GetLastObservedAt(characterId);

    public event EventHandler<MissionJournalChangedEventArgs>?
        MissionJournalChanged
    {
        add => this.missionJournalCoordinator.JournalChanged += value;
        remove => this.missionJournalCoordinator.JournalChanged -= value;
    }

    public IReadOnlyList<ActivityJournalEntry> GetActivityHistory(
        uint characterId,
        int maximumResults = 5000) =>
        this.activityJournalCoordinator.GetHistory(
            characterId,
            maximumResults);

    public IReadOnlyList<ActivityJournalEntry> GetActivityHistory(
        uint characterId,
        ActivityJournalCategory categories,
        int maximumResults = 5000) =>
        this.activityJournalCoordinator.GetHistory(
            characterId,
            categories,
            maximumResults);

    public DateTimeOffset? GetActivityHistoryLastRecordedAt(
        uint characterId) =>
        this.activityJournalCoordinator.GetLastRecordedAt(characterId);

    public IReadOnlyList<ReputationJournalEntry> GetReputationHistory(
        uint characterId,
        string factionKey,
        int maximumResults = 2000) =>
        this.activityJournalCoordinator.GetReputationHistory(
            characterId,
            factionKey,
            maximumResults);

    public LootJournalSession? GetLootSession(string sessionId) =>
        this.activityJournalCoordinator.GetLootSession(sessionId);

    public event EventHandler<ActivityJournalChangedEventArgs>?
        ActivityJournalChanged
    {
        add => this.activityJournalCoordinator.JournalChanged += value;
        remove => this.activityJournalCoordinator.JournalChanged -= value;
    }

    public IReadOnlyList<CombatJournalEncounter> GetCombatHistory(
        uint characterId,
        int maximumResults = 1000) =>
        this.combatJournalCoordinator.GetHistory(
            characterId,
            maximumResults);

    public CombatJournalEncounter? GetCombatEncounter(string encounterId) =>
        this.combatJournalCoordinator.GetEncounter(encounterId);

    public DateTimeOffset? GetCombatHistoryLastRecordedAt(
        uint characterId) =>
        this.combatJournalCoordinator.GetLastRecordedAt(characterId);

    public event EventHandler<CombatJournalChangedEventArgs>?
        CombatJournalChanged
    {
        add => this.combatJournalCoordinator.JournalChanged += value;
        remove => this.combatJournalCoordinator.JournalChanged -= value;
    }


    public event EventHandler<PilotArchiveChangedEventArgs>? PilotArchiveChanged
    {
        add => this.pilotArchiveCoordinator.ArchiveChanged += value;
        remove => this.pilotArchiveCoordinator.ArchiveChanged -= value;
    }

    public event EventHandler? SkillBuildLibraryChanged
    {
        add => this.skillBuildLocalWorkspace.Changed += value;
        remove => this.skillBuildLocalWorkspace.Changed -= value;
    }

    public string? LocateGameOutputDirectory()
    {
        foreach (var client in this.Clients)
        {
            _ = this.TryObserveGameInstallation(client);
        }

        var savedDirectory =
            this.settings.GameSettingsEditor.OutputDirectory;

        return !string.IsNullOrWhiteSpace(savedDirectory) &&
               Directory.Exists(savedDirectory)
            ? savedDirectory
            : null;
    }

    private string? TryObserveGameInstallation(ClientInstance client)
    {
        if (this.gameInstallationObservedProcessIds.Contains(client.ProcessId))
        {
            return null;
        }

        var outputDirectory =
            this.gameKeyMapLocator.LocateOutputDirectory(client);

        if (outputDirectory == null ||
            !Directory.Exists(outputDirectory))
        {
            return null;
        }

        this.gameInstallationObservedProcessIds.Add(client.ProcessId);

        if (!string.Equals(
                this.settings.GameSettingsEditor.OutputDirectory,
                outputDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            this.settings.GameSettingsEditor.OutputDirectory =
                outputDirectory;
            this.SaveSettings();
        }

        return outputDirectory;
    }

    public void SetGameSettingsChatFontResolution(string? resolution)
    {
        this.settings.GameSettingsEditor.ChatFontResolution =
            string.IsNullOrWhiteSpace(resolution)
                ? null
                : resolution.Trim();
        this.SaveSettings();
    }

    public (ForgeContributionStatisticsSnapshot Session,
        ForgeContributionStatisticsSnapshot Lifetime)
        GetForgeContributionStatistics()
    {
        return this.forgeContributionCoordinator.GetStatistics();
    }

    public void SaveMainWindowSettings()
    {
        this.SaveSettings();
    }

    public void SaveQuickLaunchSettings()
    {
        this.SaveSettings();
    }

    public void SaveWorldFindSettings()
    {
        this.SaveSettings();
    }

    public WindowPlacementBinding BindGlobalWindowPlacement(
        Form form,
        string windowId,
        IWin32Window? preferredOwner = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);

        var preferredScreen = ResolvePreferredScreen(preferredOwner);

        return new WindowPlacementBinding(
            form,
            _ => new WindowPlacementContext(
                string.Concat("global:", windowId),
                preferredScreen),
            key => this.LoadWindowPlacement(windowId, key),
            this.SaveWindowPlacement,
            initialProcessId: null,
            centerOnPreferredScreenWhenMissing: false);
    }

    public WindowPlacementBinding BindClientWindowPlacement(
        Form form,
        string windowId,
        int? initialProcessId)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);

        return new WindowPlacementBinding(
            form,
            processId => this.ResolveClientWindowPlacementContext(
                windowId,
                processId),
            key => this.LoadWindowPlacement(windowId, key),
            this.SaveWindowPlacement,
            initialProcessId,
            centerOnPreferredScreenWhenMissing: true);
    }

    public void SaveForgeContributionSettings()
    {
        this.settings.ForgeContributions.EnsureDefaults();
        this.SaveSettings();
        this.forgeContributionCoordinator.SettingsChanged();
    }

    internal Task<string?> ValidateCommandPaletteHotKeyAsync(
        Keys hotKey,
        CancellationToken cancellationToken)
    {
        var runningClients = this.Clients
            .Where(client =>
                client.HostForm != null &&
                client.GameWindowHandle != IntPtr.Zero)
            .ToArray();

        return this.commandPaletteHotKeyValidator.ValidateAsync(
            runningClients,
            hotKey,
            cancellationToken);
    }

    internal bool IsMissionWikiFeatureEnabled(int processId)
    {
        lock (this.lockObject)
        {
            return this.clients.TryGetValue(processId, out var client) &&
                   this.GetEnabledAddonIds(client).Contains(
                       MissionWikiFeature.AddonId,
                       StringComparer.Ordinal);
        }
    }

    internal void SetMissionWikiFeatureEnabled(
        int processId,
        bool enabled,
        bool persist = true)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(processId, out client);
        }

        if (client != null)
        {
            this.SetMissionWikiFeatureEnabled(
                client,
                enabled,
                persist);
        }
    }

    public void ResetForgeContributionSessionStatistics()
    {
        this.forgeContributionCoordinator.ResetSessionStatistics();
    }

    public GalaxyDataSet NavigationData { get; private set; }

    public GalaxyKnowledgeSnapshot GalaxyKnowledge =>
        this.galaxyKnowledgeCoordinator.Current;

    public string GalaxyKnowledgeStatus =>
        this.galaxyKnowledgeCoordinator.Status;

    public NavigationRouteCoordinator NavigationRoutes { get; }

    public event EventHandler<NavigationPlannerRequestedEventArgs>?
        NavigationPlannerRequested;

    public event EventHandler<GalaxyAtlasRequestedEventArgs>?
        GalaxyAtlasRequested;

    public event EventHandler<WorldFindRequestedEventArgs>?
        WorldFindRequested;

    public event EventHandler<InGameOptionsRequestedEventArgs>?
        InGameOptionsRequested;

    public event EventHandler<HelpRequestedEventArgs>?
        HelpRequested;

    public event EventHandler? NavigationDataChanged;

    public event EventHandler<GalaxyKnowledgeSnapshotChangedEventArgs>?
        GalaxyKnowledgeChanged;

    public NavigationDataUpdateStatus GetNavigationDataUpdateStatus()
    {
        bool isChecking;

        lock (this.navigationUpdateTaskLock)
        {
            isChecking = this.navigationUpdateTask is
                { IsCompleted: false };
        }

        DateTimeOffset nextAutomaticCheck;

        lock (this.navigationUpdateTaskLock)
        {
            nextAutomaticCheck = this.nextAutomaticNavigationUpdateCheckAt;
        }

        return this.navigationDataStore.GetUpdateStatus(
            this.NavigationData.Revision,
            isChecking) with
        {
            AutomaticUpdatesEnabled = true,
            AutomaticCheckInterval = navigationDataAutomaticCheckInterval,
            NextAutomaticCheck = nextAutomaticCheck == DateTimeOffset.MaxValue
                ? null
                : nextAutomaticCheck,
        };
    }

    public Task CheckForNavigationDataUpdateNowAsync()
    {
        return this.QueueNavigationDataUpdateCheck();
    }

    public bool TryActivatePendingNavigationData(out string message)
    {
        var activeAutoPilot = this.Clients
            .Select(client =>
                this.navigationAutoPilotCoordinator.GetSnapshot(
                    client.ProcessId))
            .FirstOrDefault(snapshot => snapshot.IsActive);

        if (activeAutoPilot != null)
        {
            message =
                "Stop Fleet Auto Pilot before activating new Forge data.";
            return false;
        }

        if (!this.forgeContributionCoordinator.CanActivateDataSet(
                out message))
        {
            return false;
        }

        if (!this.navigationDataStore.TryActivatePending(
                out var loadResult,
                out message))
        {
            return false;
        }

        var previousRevision = this.NavigationData.Revision;
        this.NavigationData = loadResult.DataSet;
        this.NavigationRoutes.UpdateData(
            this.NavigationData.Topology,
            this.NavigationData.Catalog);
        this.forgeContributionCoordinator.UpdateDataSet(
            this.NavigationData);
        this.socialCoordinator.UpdateDataSet(this.NavigationData);
        this.galaxyKnowledgeCoordinator.UpdateNavigationData(
            this.NavigationData);
        this.NavigationDataChanged?.Invoke(this, EventArgs.Empty);
        this.addonRuntimeCoordinator.PublishGlobalEvent(
            "forge.dataset_update_activated",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scope"] = "installation",
                ["from_revision"] = previousRevision,
                ["to_revision"] = this.NavigationData.Revision,
                ["dataset_epoch"] = this.NavigationData.DatasetEpoch,
            },
            DateTimeOffset.UtcNow);
        return true;
    }

    public IReadOnlyList<ClientObservationSnapshot> GetClientObservationSnapshots()
    {
        return this.clientObservationCoordinator.GetSnapshots();
    }

    public ClientChatInputState ReadChatInputState(
        int processId,
        out string status)
    {
        return this.clientObservationCoordinator.ReadChatInputState(
            processId,
            out status);
    }

    public NavigationRouteSnapshot GetNavigationRouteSnapshot(
        int processId)
    {
        return this.NavigationRoutes.GetSnapshot(processId);
    }

    public NavigationDestination? GetNavigationCurrentLocation(
        int processId)
    {
        return this.NavigationRoutes.GetCurrentLocation(processId);
    }

    public NavigationAutoPilotSnapshot GetNavigationAutoPilotSnapshot(
        int processId)
    {
        return this.navigationAutoPilotCoordinator.GetSnapshot(processId);
    }

    internal AddonNavigationRouteSnapshot
        GetNavigationPresentationSnapshot(int processId)
    {
        var route = this.NavigationRoutes.GetSnapshot(processId);

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                processId,
                out var observation))
        {
            return new AddonNavigationRouteSnapshot
            {
                IsAvailable = false,
                Status = "unavailable",
                StatusText =
                    "Navigation is waiting for live game state.",
                HasRoute = route.HasRoute,
                Journey = new AddonNavigationJourneySnapshot
                {
                    State = "unavailable",
                    StatusText =
                        "Navigation is waiting for live game state.",
                },
            };
        }

        return this.BuildAddonNavigationRouteSnapshot(
            route,
            observation);
    }

    internal void RequestNavigationPlanner(int processId)
    {
        this.NavigationPlannerRequested?.Invoke(
            this,
            new NavigationPlannerRequestedEventArgs(processId));
    }

    internal void SetNavigationPresentationMode(
        int processId,
        NavigationPresentationMode mode)
    {
        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(processId, out var client))
            {
                return;
            }

            var slot = this.GetAssignedSlot(client);

            if (slot == null || slot.NavigationPresentationMode == mode)
            {
                return;
            }

            slot.NavigationPresentationMode = mode;
            this.SaveSettings();
        }
    }


    internal void SetMissionWikiPresentationMode(
        int processId,
        MissionWikiPresentationMode mode)
    {
        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(processId, out var client))
            {
                return;
            }

            var slot = this.GetAssignedSlot(client);

            if (slot == null || slot.MissionWikiPresentationMode == mode)
            {
                return;
            }

            slot.MissionWikiPresentationMode = mode;
            this.SaveSettings();
        }
    }

    internal void SetMissionWikiPaneRatios(
        int processId,
        double leftPaneRatio,
        double missionListPaneRatio,
        double detailsPaneRatio)
    {
        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(processId, out var client))
            {
                return;
            }

            var slot = this.GetAssignedSlot(client);

            if (slot == null)
            {
                return;
            }

            leftPaneRatio = Math.Clamp(
                leftPaneRatio,
                0.30,
                0.62);
            missionListPaneRatio = Math.Clamp(
                missionListPaneRatio,
                0.18,
                0.60);
            detailsPaneRatio = Math.Clamp(
                detailsPaneRatio,
                0.18,
                0.60);

            if (missionListPaneRatio + detailsPaneRatio > 0.82)
            {
                detailsPaneRatio = 0.82 - missionListPaneRatio;
            }

            if (Math.Abs(
                    slot.MissionWikiLeftPaneRatio -
                    leftPaneRatio) < 0.001 &&
                Math.Abs(
                    slot.MissionWikiListPaneRatio -
                    missionListPaneRatio) < 0.001 &&
                Math.Abs(
                    slot.MissionWikiDetailsPaneRatio -
                    detailsPaneRatio) < 0.001)
            {
                return;
            }

            slot.MissionWikiLeftPaneRatio = leftPaneRatio;
            slot.MissionWikiListPaneRatio = missionListPaneRatio;
            slot.MissionWikiDetailsPaneRatio = detailsPaneRatio;
            this.SaveSettings();
        }
    }

    public NavigationAutoPilotCommandResult StartNavigationAutoPilot(
        int processId,
        uint? expectedSectorId = null)
    {
        ClientInstance? client;
        ClientHostForm? hostForm;
        ClientInstance[] managedClients;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(processId, out client);
            hostForm = client?.HostForm;
            managedClients = [.. this.clients.Values];
        }

        if (client == null || hostForm == null)
        {
            return NavigationAutoPilotCommandResult.Failure(
                "The selected client is no longer hosted.");
        }

        return this.navigationAutoPilotCoordinator.Start(
            client,
            hostForm,
            managedClients,
            expectedSectorId);
    }

    public NavigationAutoPilotCommandResult StopNavigationAutoPilot(
        int processId)
    {
        return this.navigationAutoPilotCoordinator.Stop(processId);
    }

    public NavigationRoutePlanResult PreviewNavigationRoute(
        int processId,
        NavigationDestination destination)
    {
        return this.NavigationRoutes.PreviewRoute(
            processId,
            destination);
    }

    public GalaxyRouteDistanceResult GetNavigationRouteDistances(
        int processId)
    {
        return this.NavigationRoutes.GetDistances(processId);
    }

    public NavigationRouteCommandResult SetNavigationDestination(
        int processId,
        NavigationDestination destination)
    {
        if (this.navigationAutoPilotCoordinator
            .GetSnapshot(processId)
            .IsActive)
        {
            return NavigationRouteCommandResult.Failure(
                "Stop Auto Pilot before changing the active route.");
        }

        return this.NavigationRoutes.SetDestination(
            processId,
            destination);
    }

    public NavigationRouteCommandResult PlanNavigationReturnTrip(
        int processId)
    {
        var autoPilot = this.navigationAutoPilotCoordinator
            .GetSnapshot(processId);

        if (autoPilot.IsActive)
        {
            return NavigationRouteCommandResult.Failure(
                "Stop Auto Pilot before planning a return trip.");
        }

        var route = this.NavigationRoutes.GetSnapshot(processId).Route;
        var autoPilotConfirmedArrival =
            route != null &&
            autoPilot.RouteId == route.RouteId &&
            autoPilot.State == NavigationAutoPilotState.Arrived;

        return this.NavigationRoutes.PlanReturnTrip(
            processId,
            autoPilotConfirmedArrival);
    }

    public NavigationRouteCommandResult ClearNavigationRoute(
        int processId)
    {
        if (this.navigationAutoPilotCoordinator
            .GetSnapshot(processId)
            .IsActive)
        {
            return NavigationRouteCommandResult.Failure(
                "Stop Auto Pilot before clearing the active route.");
        }

        return this.NavigationRoutes.ClearRoute(processId);
    }

    public async Task<NavigationTargetSelectionResult>
        SelectNextNavigationTargetAsync(
            int processId,
            CancellationToken cancellationToken = default)
    {
        ClientInstance? client;
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(processId, out client);
            hostForm = client?.HostForm;
        }

        if (client == null || hostForm == null)
        {
            return NavigationTargetSelectionResult.Failure(
                "The selected client is no longer hosted.");
        }

        if (this.navigationAutoPilotCoordinator
            .GetSnapshot(processId)
            .IsActive)
        {
            return NavigationTargetSelectionResult.Failure(
                "Auto Pilot already owns route-target selection for this client.");
        }

        return await this.navigationTargetSelectionService
            .SelectNextTargetAsync(
                client,
                hostForm,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public string AddonDirectory =>
        this.addonRuntimeCoordinator.AddonDirectory;

    public IReadOnlyList<AddonRegistrySummary> AddonRegistry =>
        this.addonRuntimeCoordinator.RegistryAddons;

    public string AddonRegistryError =>
        this.addonRuntimeCoordinator.RegistryError;

    public DateTimeOffset? AddonRegistryFetchedAt =>
        this.addonRuntimeCoordinator.RegistryFetchedAt;

    public IReadOnlyList<AddonInstallationInfo> GetAddonInstallations()
    {
        return this.addonRuntimeCoordinator.GetInstallations();
    }

    public IReadOnlyList<AddonDevelopmentWorkspace> GetAddonDevelopmentWorkspaces()
    {
        return this.addonRuntimeCoordinator.GetDevelopmentWorkspaces();
    }

    public AddonDevelopmentWorkspace CreateAddonDevelopmentWorkspace(
        string addonId,
        string name,
        string? author = null)
    {
        return this.addonRuntimeCoordinator.CreateDevelopmentWorkspace(
            addonId,
            name,
            author);
    }

    public Task<AddonCommandResult>
        CreateAddonDevelopmentWorkspaceFromInstalledAsync(
            string addonId,
            CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator
            .CreateDevelopmentWorkspaceFromInstalledAddonAsync(
                addonId,
                cancellationToken);
    }

    public Task<AddonCommandResult>
        CreateAddonDevelopmentWorkspaceFromRegistryAsync(
            string addonId,
            string version,
            CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator
            .CreateDevelopmentWorkspaceFromRegistryReleaseAsync(
                addonId,
                version,
                cancellationToken);
    }

    public Task<AddonCommandResult> DiscardAddonDevelopmentWorkspaceAsync(
        string addonId,
        CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator
            .DiscardDevelopmentWorkspaceAsync(
                addonId,
                cancellationToken);
    }

    public IReadOnlyList<AddonDevelopmentDocument> GetAddonDevelopmentDocuments(
        string workspaceId)
    {
        return this.addonRuntimeCoordinator.GetDevelopmentDocuments(workspaceId);
    }

    public AddonDevelopmentDocumentContent ReadAddonDevelopmentDocument(
        string workspaceId,
        string relativePath)
    {
        return this.addonRuntimeCoordinator.ReadDevelopmentDocument(
            workspaceId,
            relativePath);
    }

    public AddonDevelopmentSaveResult SaveAddonDevelopmentDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        return this.addonRuntimeCoordinator.SaveDevelopmentDocument(
            workspaceId,
            relativePath,
            text);
    }

    public AddonDevelopmentValidationResult ValidateAddonDevelopmentDocument(
        string workspaceId,
        string relativePath,
        string text)
    {
        return this.addonRuntimeCoordinator.ValidateDevelopmentDocument(
            workspaceId,
            relativePath,
            text);
    }

    public AddonDevelopmentValidationResult ValidateAddonDevelopmentWorkspace(
        string workspaceId)
    {
        return this.addonRuntimeCoordinator.ValidateDevelopmentWorkspace(
            workspaceId);
    }

    public async Task<AddonPublicationResult> PublishAddonDevelopmentWorkspaceAsync(
        int ownerProcessId,
        string workspaceId,
        string? summary,
        CancellationToken cancellationToken = default)
    {
        string livePilotName;

        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                throw new InvalidOperationException(
                    "The selected game client is no longer available.");
            }

            livePilotName = client.LiveCharacterIdentity.Name?.Trim() ?? "";

            if (client.LifecycleState != ClientLifecycleState.InGame ||
                livePilotName.Length == 0)
            {
                throw new InvalidOperationException(
                    "Log a pilot into the game before publishing an addon.");
            }
        }

        var sourcePackage = this.addonRuntimeCoordinator
            .BuildDevelopmentPublicationSourcePackage(workspaceId);
        var response = await this.forgeContributionCoordinator
            .PublishAddonAsync(
                livePilotName,
                sourcePackage,
                summary,
                cancellationToken)
            .ConfigureAwait(false);

        await this.addonRuntimeCoordinator
            .RefreshCatalogAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AddonPublicationResult
        {
            Release = response.Release,
            AlreadyPublished = response.AlreadyPublished,
        };
    }

    public string WriteAddonDevelopmentApiStub(string workspaceId)
    {
        return this.addonRuntimeCoordinator.WriteDevelopmentApiStub(workspaceId);
    }

    public string WriteAddonDevelopmentApiReference(string workspaceId)
    {
        return this.addonRuntimeCoordinator.WriteDevelopmentApiReference(workspaceId);
    }

    public string AddonEditorFontFamily =>
        string.IsNullOrWhiteSpace(
            this.settings.AddonCenter.EditorFontFamily)
                ? "Consolas"
                : this.settings.AddonCenter.EditorFontFamily.Trim();

    public int AddonEditorFontSize => Math.Clamp(
        this.settings.AddonCenter.EditorFontSize,
        8,
        28);

    public bool CheckAddonUpdatesAutomatically =>
        this.settings.AddonCenter.CheckForUpdatesAutomatically;

    public void SetAddonEditorFont(
        string fontFamily,
        int fontSize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        this.settings.AddonCenter.EditorFontFamily = fontFamily.Trim();
        this.settings.AddonCenter.EditorFontSize = Math.Clamp(
            fontSize,
            8,
            28);
        this.SaveSettings();
    }

    public void SetCheckAddonUpdatesAutomatically(bool enabled)
    {
        this.settings.AddonCenter.CheckForUpdatesAutomatically = enabled;
        this.SaveSettings();
    }

    public bool AddonsSuspendedForSession
    {
        get
        {
            lock (this.lockObject)
            {
                return this.addonsSuspendedForSession;
            }
        }
    }

    public string GetAddonOwnerDisplayName(int ownerProcessId)
    {
        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                return "Client unavailable";
            }

            var slot = this.GetAssignedSlot(client);
            var pilot = client.LiveCharacterIdentity.Name;
            var pilotText = string.IsNullOrWhiteSpace(pilot)
                ? "pilot not observed yet"
                : pilot;

            return slot == null
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Unassigned client · {pilotText}")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Client slot: {slot.Name} · Pilot: {pilotText}");
        }
    }

    public string GetAddonPublicationPilotName(int ownerProcessId)
    {
        lock (this.lockObject)
        {
            return this.clients.TryGetValue(
                    ownerProcessId,
                    out var client) &&
                   client.LifecycleState == ClientLifecycleState.InGame
                ? client.LiveCharacterIdentity.Name?.Trim() ?? ""
                : "";
        }
    }

    public IReadOnlyList<AddonRuntimeStatus> GetAddonStatuses(
        int ownerProcessId)
    {
        ClientInstance? client;
        ClientSlot? slot;
        bool suspended;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(
                ownerProcessId,
                out client);

            slot = client == null
                ? null
                : this.GetAssignedSlot(client);
            suspended = this.addonsSuspendedForSession;
        }

        IReadOnlyList<string> enabledAddonIds = client == null
            ? Array.Empty<string>()
            : this.GetEnabledAddonIds(client);

        var missionWikiEnabled = enabledAddonIds.Contains(
            MissionWikiFeature.AddonId,
            StringComparer.Ordinal);

        var missionWikiStatus =
            new AddonRuntimeStatus
            {
                AddonId = MissionWikiFeature.AddonId,
                Name = MissionWikiFeature.Name,
                Version = MissionWikiFeature.Version,
                Description = MissionWikiFeature.Description,
                Author = "Net7 Client Manager",
                PublisherName = "Net7 Client Manager",
                IsOfficial = true,
                DirectoryPath = this.AddonDirectory,
                Source = "Host",
                IsValid = true,
                IsEnabled = missionWikiEnabled,
                State = !missionWikiEnabled
                    ? AddonRuntimeState.Disabled
                    : suspended
                        ? AddonRuntimeState.Suspended
                        : client?.HostForm?.MissionWikiRuntimeState ??
                          AddonRuntimeState.WaitingForContext,
                Detail = !missionWikiEnabled
                    ? "Disabled for this client."
                    : suspended
                        ? "Temporarily suspended for this Client Manager session."
                        : client?.HostForm?.MissionWikiStatusText ??
                          "Waiting for the hosted client.",
                CurrentContext =
                    client?.LifecycleState.ToString() ??
                    "unknown",
                LastActivityAt = client?.LastObservedAt,
            };

        var packageStatuses = this.addonRuntimeCoordinator
            .GetOwnerStatuses(ownerProcessId)
            .Where(status =>
                !string.Equals(
                    status.AddonId,
                    MissionWikiFeature.AddonId,
                    StringComparison.Ordinal))
            .Select(status =>
            {
                var enabledForClient = enabledAddonIds.Contains(
                    status.AddonId,
                    StringComparer.Ordinal);

                return status with
                {
                    IsEnabled = enabledForClient,
                    State = !enabledForClient
                        ? AddonRuntimeState.Disabled
                        : suspended && status.IsValid
                            ? AddonRuntimeState.Suspended
                            : status.State,
                    Detail = suspended &&
                             enabledForClient &&
                             status.IsValid
                        ? "Temporarily suspended for this Client Manager session."
                        : status.Detail,
                };
            });

        return [missionWikiStatus, .. packageStatuses];
    }

    public void SetAddonsSuspendedForSession(bool suspended)
    {
        ClientInstance[] currentClients;

        lock (this.lockObject)
        {
            if (this.addonsSuspendedForSession == suspended)
            {
                return;
            }

            this.addonsSuspendedForSession = suspended;
            currentClients = [.. this.clients.Values];
        }

        var snapshots = this.clientObservationCoordinator
            .GetSnapshots()
            .ToDictionary(
                snapshot => snapshot.ProcessId);

        foreach (var client in currentClients)
        {
            var missionWikiEnabled = !suspended &&
                this.GetEnabledAddonIds(client).Contains(
                    MissionWikiFeature.AddonId,
                    StringComparer.Ordinal);

            client.HostForm?.SetAddonsSuspendedForSession(suspended);
            client.HostForm?.SetMissionWikiEnabled(missionWikiEnabled);

            if (!snapshots.TryGetValue(
                    client.ProcessId,
                    out var snapshot))
            {
                continue;
            }

            var navigationRoute = this.NavigationRoutes.GetSnapshot(
                client.ProcessId);

            this.addonRuntimeCoordinator.UpdateOwnerSnapshot(
                snapshot,
                this.BuildAddonOwnerRegistration(client),
                BuildAddonNavigationRouteSnapshot(
                    navigationRoute,
                    snapshot));
        }
    }

    public IReadOnlyList<AddonLogEntry> GetAddonRecentLogs(
        int ownerProcessId)
    {
        return this.addonRuntimeCoordinator.GetRecentLogs(
            ownerProcessId);
    }

    public bool CanPersistAddonSelection(int ownerProcessId)
    {
        lock (this.lockObject)
        {
            return this.clients.ContainsKey(ownerProcessId);
        }
    }

    public Task RefreshAddonCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator.RefreshCatalogAsync(
            cancellationToken);
    }

    public Task<AddonCommandResult> UpdateAddonAsync(
        string addonId,
        CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator.UpdateAddonAsync(
            addonId,
            cancellationToken);
    }

    public async Task<AddonCommandResult> InstallAddonVersionAsync(
        int ownerProcessId,
        string addonId,
        string version,
        CancellationToken cancellationToken = default)
    {
        var alreadyInstalled = this.GetAddonInstallations().Any(
            installation => string.Equals(
                installation.AddonId,
                addonId,
                StringComparison.Ordinal));

        var installResult = await this.addonRuntimeCoordinator
            .InstallAddonVersionAsync(
                addonId,
                version,
                cancellationToken)
            .ConfigureAwait(false);

        if (!installResult.Succeeded || alreadyInstalled)
        {
            return installResult;
        }

        var enableResult = await this.EnableAddonAsync(
                ownerProcessId,
                addonId,
                cancellationToken)
            .ConfigureAwait(false);

        if (enableResult.Succeeded)
        {
            return AddonCommandResult.Success();
        }

        return AddonCommandResult.Failure(
            string.Concat(
                "The addon was installed, but could not be enabled for ",
                "this client slot. ",
                enableResult.Error));
    }

    public async Task<AddonCommandResult> UninstallAddonAsync(
        string addonId,
        bool removeStoredData,
        CancellationToken cancellationToken = default)
    {
        var result = await this.addonRuntimeCoordinator
            .UninstallAddonAsync(
                addonId,
                removeStoredData,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result;
        }

        ClientInstance[] currentClients;

        lock (this.lockObject)
        {
            foreach (var slot in this.settings.Profiles
                         .SelectMany(profile => profile.Slots))
            {
                slot.EnabledAddonIds.RemoveAll(candidate =>
                    string.Equals(
                        candidate,
                        addonId,
                        StringComparison.Ordinal));

                if (removeStoredData)
                {
                    slot.AddonWindowPlacements.RemoveAll(placement =>
                        string.Equals(
                            placement.AddonId,
                            addonId,
                            StringComparison.Ordinal));
                }
            }

            this.settings.AddonCenter.UnassignedEnabledAddonIds.RemoveAll(
                candidate => string.Equals(
                    candidate,
                    addonId,
                    StringComparison.Ordinal));

            if (removeStoredData)
            {
                this.settings.AddonCenter.UnassignedAddonWindowPlacements
                    .RemoveAll(placement => string.Equals(
                        placement.AddonId,
                        addonId,
                        StringComparison.Ordinal));
            }

            currentClients = [.. this.clients.Values];
            this.SaveSettings();
        }

        var snapshots = this.clientObservationCoordinator
            .GetSnapshots()
            .ToDictionary(snapshot => snapshot.ProcessId);

        foreach (var client in currentClients)
        {
            if (!snapshots.TryGetValue(
                    client.ProcessId,
                    out var snapshot))
            {
                continue;
            }

            var navigationRoute = this.NavigationRoutes.GetSnapshot(
                client.ProcessId);

            this.addonRuntimeCoordinator.UpdateOwnerSnapshot(
                snapshot,
                this.BuildAddonOwnerRegistration(client),
                BuildAddonNavigationRouteSnapshot(
                    navigationRoute,
                    snapshot));
        }

        return AddonCommandResult.Success();
    }

    public Task<AddonCommandResult> SetAddonPinnedAsync(
        string addonId,
        bool pinned,
        CancellationToken cancellationToken = default)
    {
        return this.addonRuntimeCoordinator.SetAddonPinnedAsync(
            addonId,
            pinned,
            cancellationToken);
    }

    public async Task<AddonCommandResult> EnableAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ClientHostForm? hostForm;
        bool suspended;
        List<string> enabledAddonIds;

        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                return AddonCommandResult.Failure(
                    "The addon owner is not attached.");
            }

            hostForm = client.HostForm;
            enabledAddonIds = this.GetMutableEnabledAddonIds(client);
            suspended = this.addonsSuspendedForSession;
        }

        if (!enabledAddonIds.Contains(
                addonId,
                StringComparer.Ordinal))
        {
            enabledAddonIds.Add(addonId);
            this.SaveSettings();
        }

        if (suspended)
        {
            return AddonCommandResult.Success();
        }

        if (string.Equals(
                addonId,
                MissionWikiFeature.AddonId,
                StringComparison.Ordinal))
        {
            hostForm?.SetMissionWikiEnabled(enabled: true);
            return AddonCommandResult.Success();
        }

        var result = await this.addonRuntimeCoordinator
            .EnableAddonAsync(
                ownerProcessId,
                addonId,
                cancellationToken);

        if (!result.Succeeded)
        {
            enabledAddonIds.RemoveAll(
                id => string.Equals(
                    id,
                    addonId,
                    StringComparison.Ordinal));
            this.SaveSettings();
        }

        return result;
    }

    public async Task<AddonCommandResult> DisableAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        ClientHostForm? hostForm;
        bool suspended;
        List<string>? enabledAddonIds;

        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                hostForm = null;
                enabledAddonIds = null;
            }
            else
            {
                hostForm = client.HostForm;
                enabledAddonIds = this.GetMutableEnabledAddonIds(client);
            }

            suspended = this.addonsSuspendedForSession;
        }

        if (enabledAddonIds?.RemoveAll(
                id => string.Equals(
                    id,
                    addonId,
                    StringComparison.Ordinal)) > 0)
        {
            this.SaveSettings();
        }

        if (string.Equals(
                addonId,
                MissionWikiFeature.AddonId,
                StringComparison.Ordinal))
        {
            hostForm?.SetMissionWikiEnabled(enabled: false);
            return AddonCommandResult.Success();
        }

        if (suspended)
        {
            return AddonCommandResult.Success();
        }

        return await this.addonRuntimeCoordinator
            .DisableAddonAsync(
                ownerProcessId,
                addonId,
                cancellationToken);
    }

    public Task<AddonCommandResult> ReloadAddonAsync(
        int ownerProcessId,
        string addonId,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                addonId,
                MissionWikiFeature.AddonId,
                StringComparison.Ordinal))
        {
            ClientHostForm? hostForm;

            lock (this.lockObject)
            {
                hostForm = this.clients.TryGetValue(
                        ownerProcessId,
                        out var client)
                    ? client.HostForm
                    : null;
            }

            hostForm?.ReloadMissionWiki();

            return Task.FromResult(
                AddonCommandResult.Success());
        }

        return this.addonRuntimeCoordinator.ReloadAddonAsync(
            ownerProcessId,
            addonId,
            cancellationToken);
    }

    public IReadOnlyList<FleetCommandDefinition> GetFleetCommandDefinitions(
        FleetCommandInvocationContext invocationContext)
    {
        var staticCommands = this.fleetCommandService.GetOverlayCommands()
            .Select(command => this.ApplyFleetCommandPresentation(
                command,
                invocationContext));

        return [.. staticCommands
            .Concat(this.BuildDynamicFleetCommandDefinitions(invocationContext))];
    }

    private FleetCommandDefinition ApplyFleetCommandPresentation(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        if (!string.Equals(
                command.Id,
                BuiltInFleetCommandProvider.InteractCommandId,
                StringComparison.OrdinalIgnoreCase))
        {
            return command;
        }

        var label = "Interact";
        var isEnabled = true;

        if (this.clientObservationCoordinator.TryGetSnapshot(
                invocationContext.ActiveClient.ProcessId,
                out var snapshot))
        {
            label = this.BuildInteractCommandLabel(snapshot);
            isEnabled = !HasProvenNoTarget(snapshot);
        }

        if (string.Equals(label, command.Label, StringComparison.Ordinal) &&
            isEnabled == command.IsEnabled)
        {
            return command;
        }

        return new FleetCommandDefinition
        {
            Id = command.Id,
            Label = label,
            ShowInOverlay = command.ShowInOverlay,
            IsEnabled = isEnabled,
            Category = command.Category,
            Arguments = new Dictionary<string, string>(command.Arguments, StringComparer.OrdinalIgnoreCase),
            OverlayOrder = command.OverlayOrder,
            OverlayRow = command.OverlayRow,
            OverlayColumn = command.OverlayColumn,
            OverlayColumnSpan = command.OverlayColumnSpan,
            Blocks = command.Blocks,
        };
    }

    private static bool HasProvenNoTarget(
        ClientObservationSnapshot snapshot)
    {
        return snapshot.HasGameState &&
               snapshot.TargetObjectId == 0;
    }

    private string BuildInteractCommandLabel(
        ClientObservationSnapshot activeSnapshot)
    {
        var verbText = ResolvePrimaryInteractVerbText(activeSnapshot);
        var hasFleetCompanions = this.HasControlledGroupCompanions(activeSnapshot);

        if (!hasFleetCompanions)
        {
            return verbText;
        }

        return string.Equals(verbText, "Interact", StringComparison.Ordinal)
            ? "Fleet Interact"
            : string.Concat("Fleet ", verbText);
    }

    private static string ResolvePrimaryInteractVerbText(
        ClientObservationSnapshot activeSnapshot)
    {
        // Keep presentation and execution on the same readiness predicate.
        // The corpse payload often becomes available before the target verb
        // observer has rebuilt the visible Tractor/Loot action.
        if (IsLootContextAvailable(activeSnapshot))
        {
            return "Loot";
        }

        // Do not infer a verb from target kind. A planet is not necessarily
        // landable, and the native target-action observation is authoritative.
        var verb = activeSnapshot.TargetInteraction.Actions
            .Select(action => action.Verb)
            .FirstOrDefault(verb => verb is
                ClientTargetVerb.Dock or
                ClientTargetVerb.Gate or
                ClientTargetVerb.Land or
                ClientTargetVerb.Tractor or
                ClientTargetVerb.Trade or
                ClientTargetVerb.Register or
                ClientTargetVerb.Jumpstart or
                ClientTargetVerb.Follow);

        var verbText = verb switch
        {
            ClientTargetVerb.Dock => "Dock",
            ClientTargetVerb.Gate => "Gate",
            ClientTargetVerb.Land => "Land",
            ClientTargetVerb.Tractor => "Loot",
            ClientTargetVerb.Trade => "Trade",
            ClientTargetVerb.Register => "Register",
            ClientTargetVerb.Jumpstart => "Jumpstart",
            ClientTargetVerb.Follow => "Follow",
            _ => "Interact",
        };

        return verbText;
    }

    public void Start()
    {
        this.addonRuntimeCoordinator.Start(
            checkForUpdatesAutomatically: false);
        this.RetireLegacyNavigationHudAddon();

        if (this.settings.AddonCenter.CheckForUpdatesAutomatically)
        {
            this.addonRuntimeCoordinator.RefreshCatalogInBackground();
        }

        this.clientObservationCoordinator.Start();
        this.clientProcessWatcher.Start();
        this.clientWindowTimer.Start();
        this.hostedClientActivationTimer.Start();
        this.socialCoordinator.Start();

        this.ScheduleInitialAutomaticNavigationDataUpdateCheck();
    }

    private void RetireLegacyNavigationHudAddon()
    {
        var retiredAddonIds = this.addonRuntimeCoordinator
            .RetireInstalledAddonsByName(
                NavigationPresentationIds.LegacyAddonName,
                removeStoredData: true);
        var retiredSet = retiredAddonIds.ToHashSet(
            StringComparer.Ordinal);
        var settingsChanged = false;

        foreach (var slot in this.settings.Profiles
                     .SelectMany(profile => profile.Slots))
        {
            settingsChanged |= MigrateLegacyNavigationCompanionPlacement(
                slot.AddonWindowPlacements);

            var wasEnabled = slot.EnabledAddonIds.Any(
                retiredSet.Contains);

            if (wasEnabled)
            {
                settingsChanged |= MigrateLegacyNavigationHudPlacement(
                    slot.AddonWindowPlacements,
                    retiredSet);
                slot.NavigationPresentationMode =
                    NavigationPresentationMode.InGame;
                settingsChanged = true;
            }

            if (retiredSet.Count != 0)
            {
                var removedEnabled = slot.EnabledAddonIds.RemoveAll(
                    retiredSet.Contains);
                var removedPlacements = slot.AddonWindowPlacements.RemoveAll(
                    placement => retiredSet.Contains(placement.AddonId));
                settingsChanged |= removedEnabled != 0 ||
                                   removedPlacements != 0;
            }
        }

        settingsChanged |= MigrateLegacyNavigationCompanionPlacement(
            this.settings.AddonCenter.UnassignedAddonWindowPlacements);

        if (retiredSet.Count != 0)
        {
            var removedEnabled = this.settings.AddonCenter
                .UnassignedEnabledAddonIds.RemoveAll(retiredSet.Contains);
            var removedPlacements = this.settings.AddonCenter
                .UnassignedAddonWindowPlacements.RemoveAll(
                    placement => retiredSet.Contains(placement.AddonId));
            settingsChanged |= removedEnabled != 0 ||
                               removedPlacements != 0;
        }

        if (settingsChanged)
        {
            this.SaveSettings();
        }
    }

    private static bool MigrateLegacyNavigationHudPlacement(
        List<AddonWindowPlacement> placements,
        IReadOnlySet<string> retiredAddonIds)
    {
        if (placements.Any(placement =>
                string.Equals(
                    placement.AddonId,
                    NavigationPresentationIds.BuiltInAddonId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    placement.WidgetId,
                    NavigationPresentationIds.InGameWindowId,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        var legacy = placements.FirstOrDefault(placement =>
            retiredAddonIds.Contains(placement.AddonId) &&
            !string.Equals(
                placement.WidgetId,
                NavigationPresentationIds.CompanionWindowId,
                StringComparison.Ordinal));

        if (legacy == null)
        {
            return false;
        }

        placements.Add(new AddonWindowPlacement
        {
            AddonId = NavigationPresentationIds.BuiltInAddonId,
            WidgetId = NavigationPresentationIds.InGameWindowId,
            OffsetX = legacy.OffsetX,
            OffsetY = legacy.OffsetY,
            Width = legacy.Width,
            Height = legacy.Height,
            IsClosed = legacy.IsClosed,
            IsVisible = legacy.IsVisible,
            IsMinimized = legacy.IsMinimized,
            IsMaximized = legacy.IsMaximized,
            HorizontalEdge = legacy.HorizontalEdge,
            MinimizedOffsetX = legacy.MinimizedOffsetX,
            MinimizedOffsetY = legacy.MinimizedOffsetY,
        });
        return true;
    }

    private static bool MigrateLegacyNavigationCompanionPlacement(
        List<AddonWindowPlacement> placements)
    {
        var legacy = placements.FirstOrDefault(placement =>
            string.Equals(
                placement.AddonId,
                NavigationPresentationIds.LegacyCompanionPlacementAddonId,
                StringComparison.Ordinal) &&
            string.Equals(
                placement.WidgetId,
                NavigationPresentationIds.CompanionWindowId,
                StringComparison.Ordinal));

        if (legacy == null)
        {
            return false;
        }

        var hasCurrentPlacement = placements.Any(placement =>
            string.Equals(
                placement.AddonId,
                NavigationPresentationIds.BuiltInAddonId,
                StringComparison.Ordinal) &&
            string.Equals(
                placement.WidgetId,
                NavigationPresentationIds.CompanionWindowId,
                StringComparison.Ordinal));

        if (!hasCurrentPlacement)
        {
            placements.Add(new AddonWindowPlacement
            {
                AddonId = NavigationPresentationIds.BuiltInAddonId,
                WidgetId = NavigationPresentationIds.CompanionWindowId,
                OffsetX = legacy.OffsetX,
                OffsetY = legacy.OffsetY,
                Width = legacy.Width,
                Height = legacy.Height,
                IsClosed = legacy.IsClosed,
                IsVisible = legacy.IsVisible,
                IsMinimized = legacy.IsMinimized,
                IsMaximized = legacy.IsMaximized,
                HorizontalEdge = legacy.HorizontalEdge,
                MinimizedOffsetX = legacy.MinimizedOffsetX,
                MinimizedOffsetY = legacy.MinimizedOffsetY,
            });
        }

        placements.RemoveAll(placement =>
            string.Equals(
                placement.AddonId,
                NavigationPresentationIds.LegacyCompanionPlacementAddonId,
                StringComparison.Ordinal) &&
            string.Equals(
                placement.WidgetId,
                NavigationPresentationIds.CompanionWindowId,
                StringComparison.Ordinal));
        return true;
    }

    private void ScheduleInitialAutomaticNavigationDataUpdateCheck()
    {
        var now = DateTimeOffset.UtcNow;
        var status = this.navigationDataStore.GetUpdateStatus(
            this.NavigationData.Revision,
            isChecking: false);
        var lastCheck = status.LastSuccessfulCheck;

        if (status.LastFailedCheck is { } failedAt &&
            (lastCheck == null || failedAt > lastCheck.Value))
        {
            lastCheck = failedAt;
        }

        var nextCheck = now + navigationDataStartupCheckDelay;

        if (lastCheck is { } checkedAt)
        {
            var earliestAllowed = checkedAt +
                navigationDataAutomaticCheckInterval;

            if (earliestAllowed > now)
            {
                nextCheck = earliestAllowed;
            }
        }

        lock (this.navigationUpdateTaskLock)
        {
            this.nextAutomaticNavigationUpdateCheckAt = nextCheck;
        }
    }

    private Task QueueNavigationDataUpdateCheck()
    {
        lock (this.navigationUpdateTaskLock)
        {
            this.navigationUpdateCheckRequested = true;
            this.nextAutomaticNavigationUpdateCheckAt =
                DateTimeOffset.UtcNow + navigationDataAutomaticCheckInterval;

            if (this.navigationUpdateTask is not
                { IsCompleted: false })
            {
                this.navigationUpdateTask =
                    this.RunNavigationUpdateChecksAsync(
                        this.navigationUpdateCancellation.Token);
            }

            return this.navigationUpdateTask!;
        }
    }

    private async Task RunNavigationUpdateChecksAsync(
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (this.navigationUpdateTaskLock)
            {
                if (!this.navigationUpdateCheckRequested)
                {
                    return;
                }

                this.navigationUpdateCheckRequested = false;
            }

            await this.CheckForNavigationDataUpdateAsync(cancellationToken)
                .ConfigureAwait(false);
            await this.CheckForProductionRecipeCatalogUpdateAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            await this.CheckForMissionCatalogUpdateAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void TickAutomaticNavigationDataUpdates()
    {
        if (this.navigationUpdateCancellation.IsCancellationRequested)
        {
            return;
        }

        this.TryActivatePendingNavigationDataAutomatically();

        var now = DateTimeOffset.UtcNow;
        var shouldCheck = false;

        lock (this.navigationUpdateTaskLock)
        {
            if (now >= this.nextAutomaticNavigationUpdateCheckAt &&
                this.navigationUpdateTask is not { IsCompleted: false })
            {
                shouldCheck = true;
                this.nextAutomaticNavigationUpdateCheckAt =
                    now + navigationDataAutomaticCheckInterval;
            }
        }

        if (shouldCheck)
        {
            _ = this.QueueNavigationDataUpdateCheck();
        }
    }

    private void TryActivatePendingNavigationDataAutomatically()
    {
        var status = this.GetNavigationDataUpdateStatus();

        if (!status.HasPendingUpdate ||
            status.IsChecking ||
            this.HasActiveNavigationAutoPilot())
        {
            return;
        }

        if (!this.forgeContributionCoordinator.CanActivateDataSet(out _))
        {
            return;
        }

        if (this.TryActivatePendingNavigationData(out var message))
        {
            Debug.WriteLine(message);
        }
    }

    private bool HasActiveNavigationAutoPilot()
    {
        return this.Clients.Any(client =>
            this.navigationAutoPilotCoordinator
                .GetSnapshot(client.ProcessId)
                .IsActive);
    }

    private async Task CheckForProductionRecipeCatalogUpdateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await this.forgeNavigationDataClient
                .GetProductionRecipeCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            var next =
                ForgeProductionRecipeCatalogStore.CreateSnapshot(
                    response);
            var current = Volatile.Read(
                ref this.forgeProductionRecipeCatalog);

            if (current.IsAvailable)
            {
                if (next.Revision < current.Revision)
                {
                    Debug.WriteLine(
                        "Net7 Forge returned an older production-recipe catalogue; " +
                        "the cached catalogue remains active.");
                    return;
                }

                if (next.Revision == current.Revision)
                {
                    if (!string.Equals(
                            current.Sha256,
                            next.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine(
                            "Net7 Forge returned different production-recipe " +
                            "content for the active catalogue revision; the " +
                            "cached catalogue remains active.");
                    }

                    return;
                }
            }

            this.forgeProductionRecipeCatalogStore.Save(response);
            Volatile.Write(
                ref this.forgeProductionRecipeCatalog,
                next);
            this.galaxyKnowledgeCoordinator.UpdateRecipeCatalog(next);
            this.addonRuntimeCoordinator.PublishGlobalEvent(
                "forge.recipe_catalog_updated",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "installation",
                    ["previous_revision"] = current.IsAvailable
                        ? current.Revision
                        : null,
                    ["revision"] = next.Revision,
                    ["recipe_count"] = next.Recipes.Count,
                    ["generated_at"] = next.GeneratedAtUtc
                        .ToUnixTimeMilliseconds(),
                },
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancels the best-effort recipe refresh.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Forge production-recipe update failed: {exception}");
        }
    }

    private async Task CheckForMissionCatalogUpdateAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await this.forgeNavigationDataClient
                .GetMissionCatalogAsync(cancellationToken)
                .ConfigureAwait(false);
            var next =
                ForgeMissionCatalogStore.CreateSnapshot(response);
            var current = Volatile.Read(
                ref this.forgeMissionCatalog);

            if (current.IsAvailable)
            {
                if (next.Revision < current.Revision)
                {
                    Debug.WriteLine(
                        "Net7 Forge returned an older mission catalogue; " +
                        "the cached catalogue remains active.");
                    return;
                }

                if (next.Revision == current.Revision)
                {
                    if (!string.Equals(
                            current.Sha256,
                            next.Sha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine(
                            "Net7 Forge returned different mission content " +
                            "for the active catalogue revision; the cached " +
                            "catalogue remains active.");
                    }

                    return;
                }
            }

            this.forgeMissionCatalogStore.Save(response);
            Volatile.Write(ref this.forgeMissionCatalog, next);
            this.galaxyKnowledgeCoordinator.UpdateMissionCatalog(next);
            this.addonRuntimeCoordinator.PublishGlobalEvent(
                "forge.mission_catalog_updated",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "installation",
                    ["previous_revision"] = current.IsAvailable
                        ? current.Revision
                        : null,
                    ["revision"] = next.Revision,
                    ["mission_count"] = next.Missions.Count,
                    ["generated_at"] = next.GeneratedAtUtc
                        .ToUnixTimeMilliseconds(),
                },
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancels the best-effort mission refresh.
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"Forge mission-catalogue update failed: {exception}");
        }
    }

    private async Task CheckForNavigationDataUpdateAsync(
        CancellationToken cancellationToken)
    {
        string? downloadedPackagePath = null;

        try
        {
            var update = await this.forgeNavigationDataClient
                .GetUpdateAsync(
                    this.NavigationData.DatasetEpoch,
                    this.NavigationData.Revision,
                    this.NavigationData.ContractVersion,
                    this.NavigationData.SnapshotSha256,
                    cancellationToken)
                .ConfigureAwait(false);

            if (update == null)
            {
                this.navigationDataStore.RecordSuccessfulUpdateCheck();
                return;
            }

            downloadedPackagePath = await this.forgeNavigationDataClient
                .DownloadPackageAsync(update, cancellationToken)
                .ConfigureAwait(false);
            var package = this.forgeNavigationUpdatePackageReader.Read(
                downloadedPackagePath,
                update);
            this.navigationDataStore.StageUpdate(
                package,
                this.NavigationData,
                update.DatasetEpoch);
            this.addonRuntimeCoordinator.PublishGlobalEvent(
                "forge.dataset_update_downloaded",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "installation",
                    ["dataset_epoch"] = update.DatasetEpoch,
                    ["from_revision"] = update.FromRevision,
                    ["to_revision"] = update.ToRevision,
                    ["mode"] = update.Mode,
                    ["reason"] = update.Reason,
                    ["package_size"] = update.PackageSize,
                },
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application shutdown cancels the best-effort update check.
        }
        catch (Exception exception)
        {
            try
            {
                this.navigationDataStore.RecordFailedUpdateCheck(exception);
            }
            catch (IOException diagnosticsException)
            {
                Debug.WriteLine(
                    $"Navigation data diagnostics could not be saved: {diagnosticsException}");
            }
            catch (UnauthorizedAccessException diagnosticsException)
            {
                Debug.WriteLine(
                    $"Navigation data diagnostics could not be saved: {diagnosticsException}");
            }

            Debug.WriteLine(
                $"Navigation data update failed: {exception}");
        }
        finally
        {
            if (downloadedPackagePath != null &&
                File.Exists(downloadedPackagePath))
            {
                try
                {
                    File.Delete(downloadedPackagePath);
                }
                catch (IOException cleanupException)
                {
                    Debug.WriteLine(
                        $"Navigation data download cleanup failed: {cleanupException}");
                }
                catch (UnauthorizedAccessException cleanupException)
                {
                    Debug.WriteLine(
                        $"Navigation data download cleanup failed: {cleanupException}");
                }
            }
        }
    }

    private SavedWindowPlacement? LoadWindowPlacement(
        string windowId,
        string key)
    {
        if (this.settings.WindowPlacements.TryGetValue(
                key,
                out var placement))
        {
            return placement;
        }

        if (string.Equals(
                windowId,
                WindowPlacementIds.WorldFind,
                StringComparison.Ordinal) &&
            this.settings.WorldFind.Bounds is { } legacyBounds)
        {
            return new SavedWindowPlacement
            {
                Bounds = new WindowBounds
                {
                    Left = legacyBounds.Left,
                    Top = legacyBounds.Top,
                    Width = legacyBounds.Width,
                    Height = legacyBounds.Height,
                },
                Maximized = this.settings.WorldFind.Maximized,
            };
        }

        return null;
    }

    private void SaveWindowPlacement(
        string key,
        SavedWindowPlacement placement)
    {
        this.settings.WindowPlacements[key] = placement;
        this.SaveSettings();
    }

    private WindowPlacementContext?
        ResolveClientWindowPlacementContext(
            string windowId,
            int? processId)
    {
        if (!processId.HasValue ||
            !this.TryGetClient(processId.Value, out var client))
        {
            return null;
        }

        string? scopeKey = null;

        if (client.AssignedSlotId.HasValue)
        {
            scopeKey = string.Concat(
                "slot:",
                client.AssignedSlotId.Value.ToString("N"));
        }
        else if (this.ResolveClientLaunchRequest(client)?.CharacterId is
                 { } configuredCharacterId)
        {
            scopeKey = string.Concat(
                "configured-character:",
                configuredCharacterId.ToString("N"));
        }
        else if (client.LiveCharacterIdentity.CharacterObjectId is
                 { } characterObjectId)
        {
            scopeKey = string.Create(
                CultureInfo.InvariantCulture,
                $"character:{characterObjectId}");
        }

        if (scopeKey == null)
        {
            return null;
        }

        Screen? preferredScreen = null;

        if (client.HostForm is { IsDisposed: false } hostForm)
        {
            preferredScreen = Screen.FromControl(hostForm);
        }
        else if (client.GameWindowHandle != IntPtr.Zero)
        {
            preferredScreen = Screen.FromHandle(
                client.GameWindowHandle);
        }

        return new WindowPlacementContext(
            string.Concat(scopeKey, ":", windowId),
            preferredScreen);
    }

    private static Screen? ResolvePreferredScreen(
        IWin32Window? owner)
    {
        return owner?.Handle is { } handle && handle != IntPtr.Zero
            ? Screen.FromHandle(handle)
            : null;
    }

    internal void SetHistoryRecordingOptions(
        bool recordMissionHistory,
        bool recordActivityHistory,
        bool recordCombatHistory)
    {
        this.settings.History.RecordMissionHistory = recordMissionHistory;
        this.settings.History.RecordActivityHistory = recordActivityHistory;
        this.settings.History.RecordCombatHistory = recordCombatHistory;
    }

    internal void ApplyHistoryRecordingOptions()
    {
        this.missionJournalCoordinator.SetRetainFinishedHistory(
            this.settings.History.RecordMissionHistory);
        this.activityJournalCoordinator.SetEnabled(
            this.settings.History.RecordActivityHistory);
        this.combatJournalCoordinator.SetRecordingOptions(
            this.settings.History.RecordCombatHistory,
            this.settings.History.RecordActivityHistory);
        this.pilotArchiveForm?.ApplyHistorySettings();
    }

    internal void PreviewGameItemToolTipOptions(
        bool enabled,
        int horizontalOffset,
        int verticalOffset)
    {
        this.ApplyGameItemToolTipOptionsToHosts(
            enabled,
            horizontalOffset,
            verticalOffset);
    }

    internal void SetGameItemToolTipOptions(
        bool enabled,
        int horizontalOffset,
        int verticalOffset)
    {
        var settings = this.settings.GameItemToolTips;
        settings.Enabled = enabled;
        settings.HorizontalOffset = Math.Clamp(
            horizontalOffset,
            GameItemToolTipSettings.MinimumOffset,
            GameItemToolTipSettings.MaximumOffset);
        settings.VerticalOffset = Math.Clamp(
            verticalOffset,
            GameItemToolTipSettings.MinimumOffset,
            GameItemToolTipSettings.MaximumOffset);

        this.ApplyGameItemToolTipOptionsToHosts(
            settings.Enabled,
            settings.HorizontalOffset,
            settings.VerticalOffset);
    }

    private void ApplyGameItemToolTipOptionsToHosts(
        bool enabled,
        int horizontalOffset,
        int verticalOffset)
    {
        ClientHostForm[] hostForms;

        lock (this.lockObject)
        {
            hostForms =
            [
                .. this.clients.Values
                    .Select(client => client.HostForm)
                    .OfType<ClientHostForm>(),
            ];
        }

        foreach (var hostForm in hostForms)
        {
            hostForm.SetGameItemToolTipOptions(
                enabled,
                horizontalOffset,
                verticalOffset);
        }
    }

    internal void SetVendorShoppingCompanionEnabled(bool enabled)
    {
        ClientHostForm[] hostForms;

        lock (this.lockObject)
        {
            hostForms =
            [
                .. this.clients.Values
                    .Select(client => client.HostForm)
                    .OfType<ClientHostForm>(),
            ];
        }

        foreach (var hostForm in hostForms)
        {
            hostForm.SetVendorShoppingCompanionEnabled(enabled);
        }
    }

    public void SaveSettings()
    {
        lock (this.settingsSaveLock)
        {
            this.settingsStore.Save(this.settings);
        }
    }

    private AddonWindowPlacement? GetAddonWindowPlacement(
        int ownerProcessId,
        string addonId,
        string widgetId)
    {
        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                return null;
            }

            var placements = this.GetAddonWindowPlacements(client);
            var placement = placements.LastOrDefault(candidate =>
                    string.Equals(
                        candidate.AddonId,
                        addonId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.WidgetId,
                        widgetId,
                        StringComparison.Ordinal));

            return placement == null
                ? null
                : new AddonWindowPlacement
                {
                    AddonId = placement.AddonId,
                    WidgetId = placement.WidgetId,
                    OffsetX = placement.OffsetX,
                    OffsetY = placement.OffsetY,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsClosed = placement.IsClosed,
                    IsVisible = placement.IsVisible,
                    IsMinimized = placement.IsMinimized,
                    IsMaximized = placement.IsMaximized,
                    HorizontalEdge = placement.HorizontalEdge,
                    MinimizedOffsetX = placement.MinimizedOffsetX,
                    MinimizedOffsetY = placement.MinimizedOffsetY,
                };
        }
    }

    private void SaveAddonWindowPlacement(
        int ownerProcessId,
        string addonId,
        string widgetId,
        AddonWindowPlacement placement)
    {
        var changed = false;

        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    ownerProcessId,
                    out var client))
            {
                return;
            }

            var placements = this.GetAddonWindowPlacements(client);
            var existing = placements.LastOrDefault(candidate =>
                string.Equals(
                    candidate.AddonId,
                    addonId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidate.WidgetId,
                    widgetId,
                    StringComparison.Ordinal));

            if (existing == null)
            {
                placements.Add(new AddonWindowPlacement
                {
                    AddonId = addonId,
                    WidgetId = widgetId,
                    OffsetX = placement.OffsetX,
                    OffsetY = placement.OffsetY,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsClosed = placement.IsClosed,
                    IsVisible = placement.IsVisible,
                    IsMinimized = placement.IsMinimized,
                    IsMaximized = placement.IsMaximized,
                    HorizontalEdge = placement.HorizontalEdge,
                    MinimizedOffsetX = placement.MinimizedOffsetX,
                    MinimizedOffsetY = placement.MinimizedOffsetY,
                });

                changed = true;
            }
            else if (existing.OffsetX != placement.OffsetX ||
                     existing.OffsetY != placement.OffsetY ||
                     existing.Width != placement.Width ||
                     existing.Height != placement.Height ||
                     existing.IsClosed != placement.IsClosed ||
                     existing.IsVisible != placement.IsVisible ||
                     existing.IsMinimized != placement.IsMinimized ||
                     existing.IsMaximized != placement.IsMaximized ||
                     existing.HorizontalEdge != placement.HorizontalEdge ||
                     existing.MinimizedOffsetX != placement.MinimizedOffsetX ||
                     existing.MinimizedOffsetY != placement.MinimizedOffsetY)
            {
                existing.OffsetX = placement.OffsetX;
                existing.OffsetY = placement.OffsetY;
                existing.Width = placement.Width;
                existing.Height = placement.Height;
                existing.IsClosed = placement.IsClosed;
                existing.IsVisible = placement.IsVisible;
                existing.IsMinimized = placement.IsMinimized;
                existing.IsMaximized = placement.IsMaximized;
                existing.HorizontalEdge = placement.HorizontalEdge;
                existing.MinimizedOffsetX = placement.MinimizedOffsetX;
                existing.MinimizedOffsetY = placement.MinimizedOffsetY;
                changed = true;
            }
        }

        if (changed)
        {
            this.SaveSettings();
        }
    }

    private IReadOnlyList<string> GetEnabledAddonIds(ClientInstance client)
    {
        return this.GetAssignedSlot(client)?.EnabledAddonIds ??
               this.settings.AddonCenter.UnassignedEnabledAddonIds;
    }

    private List<string> GetMutableEnabledAddonIds(ClientInstance client)
    {
        return this.GetAssignedSlot(client)?.EnabledAddonIds ??
               this.settings.AddonCenter.UnassignedEnabledAddonIds;
    }

    private List<AddonWindowPlacement> GetAddonWindowPlacements(
        ClientInstance client)
    {
        return this.GetAssignedSlot(client)?.AddonWindowPlacements ??
               this.settings.AddonCenter.UnassignedAddonWindowPlacements;
    }

    public ClientSlot? GetAssignedSlot(ClientInstance client)
    {
        if (client.AssignedSlotId == null)
        {
            return null;
        }

        return this.ActiveProfile?.Slots.FirstOrDefault(
            slot => slot.Id == client.AssignedSlotId.Value);
    }

    private bool IsForgePilotLive(string pilotName)
    {
        if (string.IsNullOrWhiteSpace(pilotName))
        {
            return false;
        }

        lock (this.lockObject)
        {
            return this.clients.Values.Any(client =>
                client.LifecycleState == ClientLifecycleState.InGame &&
                string.Equals(
                    client.LiveCharacterIdentity.Name?.Trim(),
                    pilotName.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    private string? ResolveAnyLiveForgePilotName()
    {
        lock (this.lockObject)
        {
            return this.clients.Values
                .Where(client =>
                    client.LifecycleState == ClientLifecycleState.InGame &&
                    !string.IsNullOrWhiteSpace(client.LiveCharacterIdentity.Name))
                .OrderBy(client => client.ProcessId)
                .Select(client => client.LiveCharacterIdentity.Name!.Trim())
                .FirstOrDefault();
        }
    }

    private static string? GetObservedCharacterName(
        ClientInstance client)
    {
        return client.LiveCharacterIdentity.Name;
    }

    private static string BuildNeutralClientLabel(int processId)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"PID {processId}");
    }

    public IReadOnlyList<LayoutProfile> Profiles => this.settings.Profiles;

    public void CreateMissingClients(IWin32Window owner)
    {
        if (this.ActiveProfile == null)
        {
            return;
        }

        this.automationOwner = owner;
        this.ClearManagedClientLaunchFailures();
        this.createMissingClientsRequested = true;
        this.TickClientCreationAutomation();
    }

    public void SetCurrentProfileKeepAlive(
        bool enabled,
        IWin32Window owner)
    {
        var profile = this.ActiveProfile;

        if (profile == null)
        {
            return;
        }

        profile.KeepClientsAlive = enabled;
        this.automationOwner = owner;
        this.SaveSettings();

        this.createMissingClientsRequested = enabled;

        if (enabled)
        {
            this.ClearManagedClientLaunchFailures();
            this.TickClientCreationAutomation();
        }
    }

    public void ReconcileClientsToCurrentProfile()
    {
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void CreateProfile(string name)
    {
        this.settings.CreateProfile(name);
        this.createMissingClientsRequested = false;
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void SwitchProfile(Guid? profileId)
    {
        if (this.settings.CurrentProfileId == profileId)
        {
            return;
        }

        if (profileId.HasValue &&
            this.settings.Profiles.TrueForAll(
                profile => profile.Id != profileId.Value))
        {
            return;
        }

        this.settings.CurrentProfileId = profileId;
        this.ClearManagedClientLaunchFailures();
        this.createMissingClientsRequested = false;
        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void RenameProfile(Guid profileId, string name)
    {
        var profile = this.settings.Profiles.FirstOrDefault(
            profile => profile.Id == profileId);

        if (profile == null)
        {
            return;
        }

        profile.Name = string.IsNullOrWhiteSpace(name)
            ? "Unnamed Profile"
            : name.Trim();

        this.SaveSettings();
    }

    public void DuplicateProfile(Guid profileId)
    {
        var source = this.settings.Profiles.FirstOrDefault(
            profile => profile.Id == profileId);

        if (source == null)
        {
            return;
        }

        var profile = new LayoutProfile
        {
            Name = string.Concat(source.Name, " Copy"),
            KeepClientsAlive = source.KeepClientsAlive,
            Slots = [.. source.Slots
                .Select(slot => new ClientSlot
                {
                    Name = slot.Name,
                    AccountId = slot.AccountId,
                    CharacterId = slot.CharacterId,
                    AutoEnterGame = slot.AutoEnterGame,
                    Bounds = new WindowBounds
                    {
                        Left = slot.Bounds.Left,
                        Top = slot.Bounds.Top,
                        Width = slot.Bounds.Width,
                        Height = slot.Bounds.Height,
                    },
                    AutoLogin = slot.AutoLogin,
                    ResolutionPresetName = slot.ResolutionPresetName,
                    MatchGameResolutionToHost =
                        slot.MatchGameResolutionToHost,
                    ShowTitleBar = slot.ShowTitleBar,
                    TitleBarMode = slot.TitleBarMode,
                    TitleBarHoverDelaySeconds =
                        slot.TitleBarHoverDelaySeconds,
                    NavigationPresentationMode =
                        slot.NavigationPresentationMode,
                    MissionWikiPresentationMode =
                        slot.MissionWikiPresentationMode,
                    MissionWikiLeftPaneRatio =
                        slot.MissionWikiLeftPaneRatio,
                    MissionWikiListPaneRatio =
                        slot.MissionWikiListPaneRatio,
                    MissionWikiDetailsPaneRatio =
                        slot.MissionWikiDetailsPaneRatio,
                    GameResolutionWidth = slot.GameResolutionWidth,
                    GameResolutionHeight = slot.GameResolutionHeight,
                    IncludeInAssistMe = slot.IncludeInAssistMe,
                    EnabledAddonIds = [.. slot.EnabledAddonIds],
                    AddonWindowPlacements =
                    [
                        .. slot.AddonWindowPlacements.Select(
                            placement => new AddonWindowPlacement
                            {
                                AddonId = placement.AddonId,
                                WidgetId = placement.WidgetId,
                                OffsetX = placement.OffsetX,
                                OffsetY = placement.OffsetY,
                                Width = placement.Width,
                                Height = placement.Height,
                                IsClosed = placement.IsClosed,
                                IsVisible = placement.IsVisible,
                                IsMinimized = placement.IsMinimized,
                                IsMaximized = placement.IsMaximized,
                                HorizontalEdge = placement.HorizontalEdge,
                                MinimizedOffsetX = placement.MinimizedOffsetX,
                                MinimizedOffsetY = placement.MinimizedOffsetY,
                            }),
                    ],
                })],
        };

        this.settings.Profiles.Add(profile);
        this.settings.CurrentProfileId = profile.Id;
        this.createMissingClientsRequested = false;

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void DeleteProfile(Guid profileId)
    {
        var profile = this.settings.Profiles.FirstOrDefault(
            profile => profile.Id == profileId);

        if (profile == null)
        {
            return;
        }

        this.settings.Profiles.Remove(profile);

        if (this.settings.CurrentProfileId == profileId)
        {
            this.settings.CurrentProfileId = null;
            this.createMissingClientsRequested = false;
        }

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    public void SaveConfiguredAccounts(
        IReadOnlyCollection<GameAccount> gameAccounts)
    {
        this.accounts = [.. gameAccounts
            .OrderBy(account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(account => account.LoginName, StringComparer.OrdinalIgnoreCase)];

        this.gameAccountStore.Save(this.accounts);
    }

    public void Dispose()
    {
        _ = this.TryRestoreGameRenderResolution(out _);
        this.navigationUpdateCancellation.Cancel();

        try
        {
            this.navigationUpdateTask?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when shutdown interrupts the update check.
        }

        this.forgeNavigationDataClient.Dispose();
        this.navigationUpdateCancellation.Dispose();
        this.hostedClientActivationTimer.Stop();
        this.hostedClientActivationTimer.Tick -=
            this.HostedClientActivationTimer_OnTick;
        this.hostedClientActivationTimer.Dispose();

        this.clientWindowTimer.Stop();
        this.clientWindowTimer.Tick -= this.ClientWindowTimer_OnTick;
        this.clientWindowTimer.Dispose();

        this.clientObservationCoordinator.ChatMessageObserved -=
            this.ClientObservationCoordinator_OnChatMessageObserved;

        this.clientObservationCoordinator.SnapshotChanged -=
            this.ClientObservationCoordinator_OnSnapshotChanged;

        this.clientObservationCoordinator.MissionPresentationChanged -=
            this.ClientObservationCoordinator_OnMissionPresentationChanged;

        this.clientObservationCoordinator.FactionPresentationChanged -=
            this.ClientObservationCoordinator_OnFactionPresentationChanged;

        this.clientObservationCoordinator.SkillPresentationChanged -=
            this.ClientObservationCoordinator_OnSkillPresentationChanged;

        this.clientObservationCoordinator.InventoryPresentationChanged -=
            this.ClientObservationCoordinator_OnInventoryPresentationChanged;

        this.clientObservationCoordinator.TooltipHoverChanged -=
            this.ClientObservationCoordinator_OnTooltipHoverChanged;

        this.addonRuntimeCoordinator.UiCommandEmitted -=
            this.AddonRuntimeCoordinator_OnUiCommandEmitted;

        this.NavigationRoutes.RouteChanged -=
            this.NavigationRouteCoordinator_OnRouteChanged;

        this.navigationAutoPilotCoordinator.StateChanged -=
            this.NavigationAutoPilotCoordinator_OnStateChanged;

        this.forgeContributionCoordinator.RevisionPublished -=
            this.ForgeContributionCoordinator_OnRevisionPublished;
        this.forgeContributionCoordinator.StatisticsChanged -=
            this.ForgeContributionCoordinator_OnStatisticsChanged;
        this.socialCoordinator.SnapshotRefreshed -=
            this.SocialCoordinator_OnSnapshotRefreshed;

        this.galaxyKnowledgeCoordinator.SnapshotChanged -=
            this.GalaxyKnowledgeCoordinator_OnSnapshotChanged;

        this.activityJournalCoordinator.EntryRecorded -=
            this.ActivityJournalCoordinator_OnEntryRecorded;
        this.missionJournalCoordinator.LifecycleEvent -=
            this.MissionJournalCoordinator_OnLifecycleEvent;
        this.combatJournalCoordinator.EncounterEnded -=
            this.CombatJournalCoordinator_OnEncounterEnded;

        if (this.forgeContributionsForm is
            { IsDisposed: false, Disposing: false })
        {
            this.forgeContributionsForm.Close();
        }

        this.forgeContributionsForm = null;

        this.pilotArchiveForm?.Close();
        this.pilotArchiveForm = null;

        this.pilotArchiveBuildBoardForm?.Close();
        this.pilotArchiveBuildBoardForm = null;

        this.socialForm?.Close();
        this.socialForm = null;

        foreach (var form in this.addonCenterForms.Values.ToArray())
        {
            form.Close();
        }

        this.addonCenterForms.Clear();

        foreach (var form in this.groupSkillsForms.Values.ToArray())
        {
            form.Close();
        }

        this.groupSkillsForms.Clear();

        foreach (var form in this.fleetLootWindowForms.Values.ToArray())
        {
            form.Close();
        }

        this.fleetLootWindowForms.Clear();

        this.socialCoordinator.Dispose();
        this.galaxyKnowledgeCoordinator.Dispose();
        this.forgeContributionCoordinator.Dispose();
        this.pilotArchiveCoordinator.Dispose();
        this.navigationAutoPilotCoordinator.Dispose();
        this.fleetFormationGate.Dispose();
        this.gameIconService.Dispose();
        this.clientObservationCoordinator.Dispose();
        this.clientProcessWatcher.Dispose();
        this.foregroundInputCoordinator.Dispose();

        foreach (var client in this.Clients)
        {
            this.CloseClient(client, CloseReason.ApplicationExit);
        }

        this.addonRuntimeCoordinator
            .DisposeAsync()
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    public IReadOnlyList<SlotResolutionPreset> SlotResolutionPresets => this.settings.SlotResolutionPresets;

    public SlotResolutionPreset DefaultSlotResolutionPreset =>
        this.settings.SlotResolutionPresets.FirstOrDefault(
            preset => string.Equals(preset.Name, this.settings.DefaultSlotResolutionPresetName, StringComparison.Ordinal))
        ?? this.settings.SlotResolutionPresets[0];

    public (bool CanStart, string ButtonText, string Status)
        GetProfileSlotLaunchStatus(ClientSlot slot)
    {
        var profile = this.ActiveProfile;

        if (profile == null ||
            profile.Slots.TrueForAll(candidate => candidate.Id != slot.Id))
        {
            return (false, "Start", "The slot is not part of the active profile.");
        }

        var assignedClient = this.Clients.FirstOrDefault(client =>
            client.AssignedSlotId == slot.Id &&
            client.State is not ClientState.Closing and not ClientState.Stopped);

        if (assignedClient != null)
        {
            return (false, "Running", "This slot already has a running client.");
        }

        // Evaluating this first also expires a stale launcher handoff before
        // the pending request is used to render the slot as Starting.
        var launchInProgress = this.IsManagedClientLaunchInProgress;

        if (this.pendingManagedClientLaunch is
            {
                Kind: ManagedClientLaunchKind.ProfileSlot,
                TargetSlotId: { } pendingSlotId,
            } && pendingSlotId == slot.Id)
        {
            return (
                false,
                "Starting...",
                this.launcherSession?.Status ??
                "Waiting for the game client to appear.");
        }

        if (launchInProgress)
        {
            return (
                false,
                "Start",
                this.GetManagedClientLaunchBlockingStatus());
        }

        if (this.TryGetManagedClientLaunchFailure(
                slot.Id,
                out var launchFailure))
        {
            return (true, "Retry", launchFailure);
        }

        return (true, "Start", "Start this exact profile slot.");
    }

    public bool StartProfileSlot(
        Guid slotId,
        IWin32Window owner,
        out string status)
    {
        var profile = this.ActiveProfile;
        var slot = profile?.Slots.FirstOrDefault(
            candidate => candidate.Id == slotId);

        if (profile == null || slot == null)
        {
            status = "The slot is not part of the active profile.";
            return false;
        }

        var launchState = this.GetProfileSlotLaunchStatus(slot);

        if (!launchState.CanStart)
        {
            status = launchState.Status;
            return false;
        }

        this.ClearManagedClientLaunchFailure(slot.Id);

        var gameResolution = ResolveSlotGameResolution(slot);
        var request = new ManagedClientLaunchRequest
        {
            Kind = ManagedClientLaunchKind.ProfileSlot,
            PlacementPolicy = ManagedClientPlacementPolicy.ProfileSlot,
            ProfileId = profile.Id,
            TargetSlotId = slot.Id,
            AccountId = slot.AccountId,
            CharacterId = slot.CharacterId,
            SkipIntro = true,
            AutoLogin = slot.AutoLogin,
            AutoEnterGame = slot.AutoEnterGame,
            GameResolutionWidth = gameResolution.Width,
            GameResolutionHeight = gameResolution.Height,
            DisplayName = slot.Name,
        };

        if (!this.StartClientFromLauncher(owner, request))
        {
            status = "The Net7 launcher was not started.";
            return false;
        }

        status = string.Concat("Starting ", slot.Name, ".");
        return true;
    }

    public bool StartUnassignedClient(
        IWin32Window owner,
        out string status)
    {
        if (this.IsManagedClientLaunchInProgress)
        {
            status = this.GetManagedClientLaunchBlockingStatus();
            return false;
        }

        var (hostResolution, gameResolution) =
            this.ResolveQuickLaunchResolutions();

        var request = new ManagedClientLaunchRequest
        {
            Kind = ManagedClientLaunchKind.Generic,
            PlacementPolicy = ManagedClientPlacementPolicy.NativeWindow,
            SkipIntro = true,
            AutoLogin = false,
            AutoEnterGame = false,
            HostWidth = hostResolution.Width,
            HostHeight = hostResolution.Height,
            GameResolutionWidth = gameResolution.Width,
            GameResolutionHeight = gameResolution.Height,
            DisplayName = "Unassigned client",
        };

        if (!this.StartClientFromLauncher(owner, request))
        {
            status = "The Net7 launcher was not started.";
            return false;
        }

        status = string.Create(
            CultureInfo.InvariantCulture,
            $"Starting an unassigned client at host {hostResolution.Width}×{hostResolution.Height} and game {gameResolution.Width}×{gameResolution.Height}.");
        return true;
    }

    private bool StartClientFromLauncher(
        IWin32Window owner,
        ManagedClientLaunchRequest request)
    {
        if (this.IsManagedClientLaunchInProgress)
        {
            return false;
        }

        this.automationOwner = owner;

        var launcherPath = this.ResolveLauncherPath(owner);

        if (launcherPath == null)
        {
            return false;
        }

        var folder = Path.GetDirectoryName(launcherPath);

        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var normalizedLauncherPath = Path.GetFullPath(launcherPath);

        if (request.HasGameResolution)
        {
            if (!this.gameRenderResolutionOverrideCoordinator.TryApply(
                    request.GameResolutionWidth!.Value,
                    request.GameResolutionHeight!.Value,
                    out var resolutionStatus))
            {
                ThemedMessageDialog.ShowWarning(
                    owner,
                    "Game resolution could not be prepared",
                    resolutionStatus);
                return false;
            }

            Debug.WriteLine(string.Concat(
                "[RenderResolution] ",
                resolutionStatus));
        }

        this.gameRenderResolutionOverrideProcessId = null;
        this.gameRenderResolutionRestoreDeadline = null;

        try
        {
            var launcherProcess = FindRunningLauncherProcess(
                normalizedLauncherPath,
                preferredProcessId: 0);

            if (launcherProcess == null)
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo(normalizedLauncherPath)
                    {
                        WorkingDirectory = folder,
                        UseShellExecute = true,
                        LoadUserProfile = true,
                    },
                };

                try
                {
                    if (!process.Start())
                    {
                        process.Dispose();
                        _ = this.TryRestoreGameRenderResolution(out _);
                        return false;
                    }
                }
                catch (Exception)
                {
                    process.Dispose();
                    throw;
                }

                launcherProcess = process;
            }

            using var activeLauncherProcess = launcherProcess;
            var now = DateTimeOffset.UtcNow;

            this.pendingManagedClientLaunch = request;
            this.launcherSession = new LauncherAutomationSession
            {
                ProcessId = activeLauncherProcess.Id,
                LauncherPath = normalizedLauncherPath,
                StartedAt = now,
                StateEnteredAt = now,
                Status = "Starting Net7 Launcher.",
            };

            return true;
        }
        catch (Exception)
        {
            _ = this.TryRestoreGameRenderResolution(out _);
            return false;
        }
    }

    private (Size Host, Size Game) ResolveQuickLaunchResolutions()
    {
        var quickLaunch = this.settings.QuickLaunch;
        var hostPreset = this.settings.SlotResolutionPresets.FirstOrDefault(
            preset => string.Equals(
                preset.Name,
                quickLaunch.HostResolutionPresetName,
                StringComparison.Ordinal))
            ?? this.DefaultSlotResolutionPreset;
        var hostResolution = new Size(
            hostPreset.Width,
            hostPreset.Height);
        var gameResolution = quickLaunch.MatchGameResolutionToHost
            ? hostResolution
            : new Size(
                quickLaunch.GameResolutionWidth > 0
                    ? quickLaunch.GameResolutionWidth
                    : hostResolution.Width,
                quickLaunch.GameResolutionHeight > 0
                    ? quickLaunch.GameResolutionHeight
                    : hostResolution.Height);

        return (hostResolution, gameResolution);
    }

    private static Size ResolveSlotGameResolution(ClientSlot slot)
    {
        if (slot.MatchGameResolutionToHost ||
            slot.GameResolutionWidth <= 0 ||
            slot.GameResolutionHeight <= 0)
        {
            return new Size(
                slot.Bounds.Width,
                slot.Bounds.Height);
        }

        return new Size(
            slot.GameResolutionWidth,
            slot.GameResolutionHeight);
    }

    private string GetManagedClientLaunchBlockingStatus()
    {
        if (this.gameRenderResolutionOverrideCoordinator.HasActiveOverride &&
            this.launcherSession == null &&
            this.pendingManagedClientLaunch == null)
        {
            if (this.gameRenderResolutionOverrideProcessId != null)
            {
                var restoreStatus =
                    this.gameRenderResolutionOverrideCoordinator.LastStatus;

                if (!string.IsNullOrWhiteSpace(restoreStatus) &&
                    restoreStatus.Contains(
                        "could not be restored",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return restoreStatus;
                }

                return
                    "Waiting for the launched game's window before restoring the previous Earth & Beyond resolution.";
            }

            return this.gameRenderResolutionOverrideCoordinator.LastStatus ??
                   "Restoring the previous Earth & Beyond resolution before another client can start.";
        }

        return "Another managed client is currently starting.";
    }

    private void TickGameRenderResolutionRestoration()
    {
        if (!this.gameRenderResolutionOverrideCoordinator.HasActiveOverride)
        {
            return;
        }

        if (this.gameRenderResolutionOverrideProcessId is { } processId)
        {
            var client = this.Clients.FirstOrDefault(candidate =>
                candidate.ProcessId == processId);

            if (client == null ||
                client.GameWindowHandle == IntPtr.Zero)
            {
                if (this.gameRenderResolutionRestoreDeadline == null ||
                    DateTimeOffset.UtcNow <
                    this.gameRenderResolutionRestoreDeadline.Value)
                {
                    return;
                }
            }
        }
        else if (this.launcherSession != null ||
                 this.pendingManagedClientLaunch != null)
        {
            return;
        }

        _ = this.TryRestoreGameRenderResolution(out _);
    }

    private bool TryRestoreGameRenderResolution(out string status)
    {
        var restored =
            this.gameRenderResolutionOverrideCoordinator.TryRestore(
                out status);

        if (restored)
        {
            this.gameRenderResolutionOverrideProcessId = null;
            this.gameRenderResolutionRestoreDeadline = null;

            if (!string.IsNullOrWhiteSpace(status))
            {
                Debug.WriteLine(string.Concat(
                    "[RenderResolution] ",
                    status));
            }
        }
        else
        {
            Debug.WriteLine(string.Concat(
                "[RenderResolution] Restore failed: ",
                status));
        }

        return restored;
    }

    public ClientInstance? FindForegroundHostedClient()
    {
        var foregroundWindowHandle = NativeMethods.GetForegroundWindowHandle();

        if (foregroundWindowHandle == IntPtr.Zero)
        {
            return null;
        }

        lock (this.lockObject)
        {
            return this.clients.Values.FirstOrDefault(client =>
                                                          client.HostForm != null &&
                                                          (
                                                              client.HostForm.Handle == foregroundWindowHandle ||
                                                              NativeMethods.IsChildOrSameWindow(client.HostForm.Handle, foregroundWindowHandle) ||
                                                              client.GameWindowHandle == foregroundWindowHandle ||
                                                              NativeMethods.IsChildOrSameWindow(client.GameWindowHandle, foregroundWindowHandle)
                                                          ));
        }
    }

    public async Task<bool> ExecuteFleetCommandAsync(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        if (!command.IsEnabled ||
            (string.Equals(
                 command.Id,
                 BuiltInFleetCommandProvider.InteractCommandId,
                 StringComparison.OrdinalIgnoreCase) &&
             this.clientObservationCoordinator.TryGetSnapshot(
                 invocationContext.ActiveClient.ProcessId,
                 out var interactSnapshot) &&
             HasProvenNoTarget(interactSnapshot)))
        {
            return false;
        }

        if (this.TryExecuteFleetUiCommand(command, invocationContext))
        {
            return false;
        }

        if (this.TryExecuteFleetLootInteractCommand(command, invocationContext))
        {
            return false;
        }

        if (IsFleetFormationCommand(command))
        {
            await this.ExecuteFleetFormationCommandAsync(
                    command,
                    invocationContext)
                .ConfigureAwait(true);

            return true;
        }

        if (GameShortcutPaletteService.IsShortcutCommand(command))
        {
            await this.ExecuteShortcutFleetCommandAsync(
                    command,
                    invocationContext)
                .ConfigureAwait(true);

            return true;
        }

        await this.fleetCommandService.ExecuteAsync(
                command,
                invocationContext,
                this.Clients,
                this.CurrentProfile,
                this.settings.FleetCommands,
                this.GetFleetCommandPilotName(invocationContext.ActiveClient))
            .ConfigureAwait(true);

        return true;
    }

    private static bool IsFleetFormationCommand(
        FleetCommandDefinition command)
    {
        return string.Equals(
                   command.Id,
                   BuiltInFleetCommandProvider.FormationModeCommandId,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   command.Id,
                   BuiltInFleetCommandProvider.FormationToggleCommandId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task ExecuteFleetFormationCommandAsync(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        if (!await this.fleetFormationGate
                .WaitAsync(millisecondsTimeout: 0)
                .ConfigureAwait(true))
        {
            invocationContext.ActiveClient.AutomationStatus =
                "Another formation command is already running.";
            return;
        }

        try
        {
            if (string.Equals(
                    command.Id,
                    BuiltInFleetCommandProvider.FormationModeCommandId,
                    StringComparison.OrdinalIgnoreCase))
            {
                await this.CycleFleetFormationModeAsync(invocationContext)
                    .ConfigureAwait(true);
                return;
            }

            await this.ToggleFleetFormationAsync(invocationContext)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException)
        {
            invocationContext.ActiveClient.AutomationStatus =
                string.Concat(
                    "Formation command failed: ",
                    exception.Message);
        }
        finally
        {
            this.fleetFormationGate.Release();
        }
    }

    private async Task CycleFleetFormationModeAsync(
        FleetCommandInvocationContext invocationContext)
    {
        var mode = GetNextFleetFormationMode(
            this.settings.FleetCommands.FormationMode);

        this.settings.FleetCommands.FormationMode = mode;
        this.SaveSettings();

        var activeClient = invocationContext.ActiveClient;
        activeClient.AutomationStatus = string.Concat(
            "Formation mode set to ",
            GetFleetFormationModeDisplayName(mode),
            ".");

        if (!this.TryResolveFleetFormationContext(
                activeClient.ProcessId,
                out var context,
                out _) ||
            !context.HasAnyFormedFollower)
        {
            return;
        }

        await this.StopFleetAutoPilotForManualFormationAsync(context)
            .ConfigureAwait(true);

        activeClient.AutomationStatus = await this.ApplyFleetFormationAsync(
                context,
                mode,
                forceFollowersToJoin: true,
                CancellationToken.None)
            .ConfigureAwait(true);
    }

    private async Task ToggleFleetFormationAsync(
        FleetCommandInvocationContext invocationContext)
    {
        var activeClient = invocationContext.ActiveClient;

        if (!this.TryResolveFleetFormationContext(
                activeClient.ProcessId,
                out var context,
                out var error))
        {
            activeClient.AutomationStatus = error;
            return;
        }

        if (context.EligibleFollowers.Count == 0)
        {
            activeClient.AutomationStatus =
                "No managed group followers are ready in the leader's current sector.";
            return;
        }

        await this.StopFleetAutoPilotForManualFormationAsync(context)
            .ConfigureAwait(true);

        activeClient.AutomationStatus = context.IsFullyFormed
            ? await this.BreakFleetFormationAsync(
                    context,
                    CancellationToken.None)
                .ConfigureAwait(true)
            : await this.ApplyFleetFormationAsync(
                    context,
                    this.settings.FleetCommands.FormationMode,
                    forceFollowersToJoin: false,
                    CancellationToken.None)
                .ConfigureAwait(true);
    }

    private async Task<string> ApplyFleetFormationAsync(
        FleetFormationContext context,
        FleetFormationMode mode,
        bool forceFollowersToJoin,
        CancellationToken cancellationToken)
    {
        var modeName = GetFleetFormationModeDisplayName(mode);
        context.Leader.AutomationStatus =
            string.Concat("Setting ", modeName, " formation");

        var menuResult = await this.ExecuteNamedInputActionForClientAsync(
                context.Leader,
                BuiltInInputActionProvider.ToggleFormationDialogName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!menuResult.Succeeded)
        {
            return string.Concat(
                "Could not open the leader's formation menu: ",
                menuResult.Message);
        }

        await Task.Delay(
                fleetFormationMenuSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        var selectionResult = await this.ExecuteNamedInputActionForClientAsync(
                context.Leader,
                GetFleetFormationModeInputActionName(mode),
                cancellationToken)
            .ConfigureAwait(true);

        if (!selectionResult.Succeeded)
        {
            return string.Concat(
                "Could not select ",
                modeName,
                " formation on the group leader: ",
                selectionResult.Message);
        }

        await Task.Delay(
                fleetFormationSelectionSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        if (!await this.WaitForFleetFormationModeAsync(
                context.Leader.ProcessId,
                mode,
                cancellationToken)
            .ConfigureAwait(true))
        {
            return string.Concat(
                "The group leader did not expose ",
                modeName,
                " formation after it was selected.");
        }

        if (!this.TryResolveFleetFormationContext(
                context.Leader.ProcessId,
                out var refreshed,
                out var refreshError))
        {
            return refreshError;
        }

        // Selecting a different formation mode resets the formation from the
        // game's point of view, even when the old parent-relative projection
        // is still visible for a short time. In that path every eligible
        // follower must explicitly Join/Leave Formation again. A normal
        // Form Up remains conservative and only toggles missing followers.
        var followersToJoin = forceFollowersToJoin
            ? refreshed.EligibleFollowers.ToList()
            : refreshed.EligibleFollowers
                .Where(follower => !follower.IsFullyFormed)
                .ToList();

        foreach (var follower in followersToJoin)
        {
            cancellationToken.ThrowIfCancellationRequested();
            follower.Client.AutomationStatus =
                string.Concat("Joining ", modeName, " formation");

            if (!await this.JoinFleetFormationAsync(
                    follower.Client,
                    cancellationToken)
                .ConfigureAwait(true))
            {
                return string.Concat(
                    "Could not ask ",
                    GetObservedCharacterName(follower.Client) ??
                    BuildNeutralClientLabel(follower.Client.ProcessId),
                    " to join formation.");
            }

            await Task.Delay(
                    fleetFormationJoinStaggerDelay,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        var expectedFollowerProcessIds = refreshed.EligibleFollowers
            .Select(follower => follower.Client.ProcessId)
            .ToHashSet();

        if (!await this.WaitForFleetFollowersFormedAsync(
                refreshed.Leader.ProcessId,
                expectedFollowerProcessIds,
                cancellationToken)
            .ConfigureAwait(true))
        {
            return string.Concat(
                modeName,
                " formation was selected, but not all managed followers settled into it.");
        }

        return string.Concat(
            "Fleet formed up in ",
            modeName,
            " formation.");
    }

    private async Task<string> BreakFleetFormationAsync(
        FleetFormationContext context,
        CancellationToken cancellationToken)
    {
        context.Leader.AutomationStatus = "Breaking formation";

        var menuResult = await this.ExecuteNamedInputActionForClientAsync(
                context.Leader,
                BuiltInInputActionProvider.ToggleFormationDialogName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!menuResult.Succeeded)
        {
            return string.Concat(
                "Could not open the leader's formation menu: ",
                menuResult.Message);
        }

        await Task.Delay(
                fleetFormationMenuSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        var breakResult = await this.ExecuteNamedInputActionForClientAsync(
                context.Leader,
                BuiltInInputActionProvider.BreakFormationName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!breakResult.Succeeded)
        {
            return string.Concat(
                "Could not break the leader's formation: ",
                breakResult.Message);
        }

        var expectedFollowerProcessIds = context.EligibleFollowers
            .Select(follower => follower.Client.ProcessId)
            .ToHashSet();

        if (!await this.WaitForFleetFollowersUnformedAsync(
                context.Leader.ProcessId,
                expectedFollowerProcessIds,
                cancellationToken)
            .ConfigureAwait(true))
        {
            return "Formation break was sent, but some managed followers still appear formed.";
        }

        return "Fleet formation broken.";
    }

    private async Task<bool> JoinFleetFormationAsync(
        ClientInstance follower,
        CancellationToken cancellationToken)
    {
        var command = await this.gameCommandCoordinator.ExecuteAsync(
                follower,
                GameCommand.Formation,
                cancellationToken)
            .ConfigureAwait(true);

        if (command.Succeeded)
        {
            return true;
        }

        var menuResult = await this.ExecuteNamedInputActionForClientAsync(
                follower,
                BuiltInInputActionProvider.ToggleFormationDialogName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!menuResult.Succeeded)
        {
            return false;
        }

        await Task.Delay(
                fleetFormationMenuSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        var formResult = await this.ExecuteNamedInputActionForClientAsync(
                follower,
                BuiltInInputActionProvider.FormUpName,
                cancellationToken)
            .ConfigureAwait(true);

        return formResult.Succeeded;
    }

    private async Task StopFleetAutoPilotForManualFormationAsync(
        FleetFormationContext context)
    {
        var activeRun = context.Participants
            .Select(participant => this.navigationAutoPilotCoordinator
                .GetSnapshot(participant.ProcessId))
            .FirstOrDefault(snapshot => snapshot.IsActive);

        if (activeRun == null)
        {
            return;
        }

        _ = this.navigationAutoPilotCoordinator.Stop(activeRun.ProcessId);

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);

        while (DateTimeOffset.UtcNow < deadline &&
               this.navigationAutoPilotCoordinator
                   .GetSnapshot(activeRun.ProcessId)
                   .IsActive)
        {
            await Task.Delay(fleetFormationPollInterval)
                .ConfigureAwait(true);
        }
    }

    private async Task<bool> WaitForFleetFormationModeAsync(
        int leaderProcessId,
        FleetFormationMode mode,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow +
                       fleetFormationObservationTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.clientObservationCoordinator.TryGetSnapshot(
                    leaderProcessId,
                    out var snapshot) &&
                snapshot.Group.IsAvailable &&
                snapshot.Group.IsValid &&
                snapshot.Group.IsInGroup &&
                snapshot.Group.IsLeader &&
                FleetFormationNameMatches(
                    snapshot.Group.FormationName,
                    mode))
            {
                return true;
            }

            await Task.Delay(
                    fleetFormationPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return false;
    }

    private async Task<bool> WaitForFleetFollowersFormedAsync(
        int leaderProcessId,
        IReadOnlySet<int> expectedFollowerProcessIds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + fleetFormationJoinTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.TryResolveFleetFormationContext(
                    leaderProcessId,
                    out var context,
                    out _) &&
                expectedFollowerProcessIds.All(processId =>
                    context.EligibleFollowers.Any(follower =>
                        follower.Client.ProcessId == processId &&
                        follower.IsFullyFormed)))
            {
                return true;
            }

            await Task.Delay(
                    fleetFormationPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return false;
    }

    private async Task<bool> WaitForFleetFollowersUnformedAsync(
        int leaderProcessId,
        IReadOnlySet<int> expectedFollowerProcessIds,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + fleetFormationJoinTimeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.TryResolveFleetFormationContext(
                    leaderProcessId,
                    out var context,
                    out _) &&
                expectedFollowerProcessIds.All(processId =>
                    context.EligibleFollowers.All(follower =>
                        follower.Client.ProcessId != processId ||
                        !follower.IsFullyFormed)))
            {
                return true;
            }

            await Task.Delay(
                    fleetFormationPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return false;
    }

    private bool TryResolveFleetFormationContext(
        int sourceProcessId,
        out FleetFormationContext context,
        out string error)
    {
        context = null!;
        error = "";

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                sourceProcessId,
                out var sourceSnapshot) ||
            !sourceSnapshot.Group.IsAvailable ||
            !sourceSnapshot.Group.IsValid ||
            !sourceSnapshot.Group.IsInGroup)
        {
            error = "The invoking pilot is not in an observable group.";
            return false;
        }

        var participants = this.GetControlledGroupClients(sourceSnapshot)
            .ToList();

        ClientInstance? leader = null;
        ClientObservationSnapshot? leaderSnapshot = null;

        foreach (var participant in participants)
        {
            if (this.clientObservationCoordinator.TryGetSnapshot(
                    participant.ProcessId,
                    out var snapshot) &&
                snapshot.Group.IsAvailable &&
                snapshot.Group.IsValid &&
                snapshot.Group.IsInGroup &&
                snapshot.Group.IsLeader)
            {
                leader = participant;
                leaderSnapshot = snapshot;
                break;
            }
        }

        if (leader == null || leaderSnapshot == null)
        {
            error = "The actual group leader is not a managed, observable client.";
            return false;
        }

        var followers = new List<FleetFormationFollower>();

        foreach (var participant in participants.Where(client =>
                     client.ProcessId != leader.ProcessId))
        {
            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    participant.ProcessId,
                    out var followerSnapshot) ||
                !IsFleetFormationFollowerEligible(
                    leaderSnapshot,
                    followerSnapshot))
            {
                continue;
            }

            var isFullyFormed = TryFindFleetFormationMember(
                    leaderSnapshot.Group,
                    participant,
                    followerSnapshot,
                    out var member) &&
                IsFleetFormationMemberFullyFormed(member);

            // A same-sector managed follower whose group-member projection is
            // temporarily incomplete is still a follower we must form. Treat
            // missing proof as not formed rather than silently dropping them.
            followers.Add(new FleetFormationFollower(
                participant,
                isFullyFormed));
        }

        context = new FleetFormationContext(
            leader,
            participants,
            followers);

        return true;
    }

    private static bool TryFindFleetFormationMember(
        ClientGroupObservation group,
        ClientInstance follower,
        ClientObservationSnapshot followerSnapshot,
        out ClientGroupMemberObservation member)
    {
        var identity = ClientLiveCharacterIdentityResolver.Resolve(
            followerSnapshot);
        var followerName = GetObservedCharacterName(follower);

        member = group.Members.FirstOrDefault(candidate =>
            candidate.IsPresent &&
            ((identity.CharacterObjectId is { } objectId &&
              objectId is not 0 and not uint.MaxValue &&
              candidate.ObjectId == objectId) ||
             (!string.IsNullOrWhiteSpace(followerName) &&
              string.Equals(
                  candidate.Name,
                  followerName,
                  StringComparison.OrdinalIgnoreCase))))!;

        return member != null;
    }

    private static bool IsFleetFormationFollowerEligible(
        ClientObservationSnapshot leader,
        ClientObservationSnapshot follower)
    {
        // NavigationState is an opt-in control observer and is normally inactive
        // until Auto Pilot acquires a lease. Formation quick actions must be
        // available during ordinary grouped play, so use the always-on world
        // observation plus the snapshot transition flag instead.
        return leader.LifecycleState == ClientLifecycleState.InGame &&
               leader.LoadingOrTransitionFlag == 0 &&
               leader.World.IsAvailable &&
               leader.World.Environment == ClientWorldEnvironment.Space &&
               leader.World.ActiveSectorNumber != 0 &&
               follower.LifecycleState == ClientLifecycleState.InGame &&
               follower.LoadingOrTransitionFlag == 0 &&
               follower.World.IsAvailable &&
               follower.World.Environment == ClientWorldEnvironment.Space &&
               follower.World.ActiveSectorNumber ==
                   leader.World.ActiveSectorNumber;
    }

    private static bool IsFleetFormationMemberFullyFormed(
        ClientGroupMemberObservation member)
    {
        return member.IsObjectResolved &&
               member.FormationPosition >= 0 &&
               member.Distance.IsAvailable &&
               member.Distance.Target.IsAvailable &&
               member.Distance.Target.StateKind ==
                   ClientSpatialStateKind.ParentRelative;
    }

    private static FleetFormationMode GetNextFleetFormationMode(
        FleetFormationMode mode)
    {
        return mode switch
        {
            FleetFormationMode.Block => FleetFormationMode.SlotBack,
            FleetFormationMode.SlotBack => FleetFormationMode.Pipe,
            FleetFormationMode.Pipe => FleetFormationMode.Block,
            _ => FleetFormationMode.Block,
        };
    }

    private static string GetFleetFormationModeDisplayName(
        FleetFormationMode mode)
    {
        return mode switch
        {
            FleetFormationMode.Block => "Block",
            FleetFormationMode.SlotBack => "Slot-Back",
            FleetFormationMode.Pipe => "Pipe",
            _ => "Block",
        };
    }

    private static string GetFleetFormationModeInputActionName(
        FleetFormationMode mode)
    {
        return mode switch
        {
            FleetFormationMode.Block =>
                BuiltInInputActionProvider.BeginBlockFormationName,
            FleetFormationMode.SlotBack =>
                BuiltInInputActionProvider.BeginSlotBackFormationName,
            FleetFormationMode.Pipe =>
                BuiltInInputActionProvider.BeginPipeFormationName,
            _ => BuiltInInputActionProvider.BeginBlockFormationName,
        };
    }

    private static bool FleetFormationNameMatches(
        string formationName,
        FleetFormationMode mode)
    {
        if (string.IsNullOrWhiteSpace(formationName))
        {
            return false;
        }

        return mode switch
        {
            FleetFormationMode.Block => formationName.Contains(
                "Block",
                StringComparison.OrdinalIgnoreCase),
            FleetFormationMode.SlotBack => formationName.Contains(
                "Slot",
                StringComparison.OrdinalIgnoreCase),
            FleetFormationMode.Pipe => formationName.Contains(
                "Pipe",
                StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private async Task ExecuteShortcutFleetCommandAsync(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        var client = invocationContext.ActiveClient;

        if (!GameShortcutPaletteService.TryCreateInvocation(
                command,
                out var shortcut,
                out var error))
        {
            client.AutomationStatus = error;
            return;
        }

        var result = await this.gameShortcutInvocationService
            .ExecuteAsync(
                client,
                shortcut,
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(true);

        client.AutomationStatus = result.Succeeded
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Invoked {shortcut.Name} via slot {shortcut.VisibleKey} (bar {shortcut.Bar}, group {shortcut.Group}, {result.Binding?.Preferred?.DisplayText ?? "unresolved key"})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Shortcut invoke failed for {shortcut.Name} (bar {shortcut.Bar}, group {shortcut.Group}, slot {shortcut.VisibleKey}): {result.Error}");
    }

    private bool TryExecuteFleetLootInteractCommand(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        if (!string.Equals(
                command.Id,
                BuiltInFleetCommandProvider.InteractCommandId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                invocationContext.ActiveClient.ProcessId,
                out var snapshot) ||
            !IsLootContextAvailable(snapshot))
        {
            return false;
        }

        this.ShowFleetLootWindow(invocationContext.ActiveClient);
        return true;
    }

    private bool TryExecuteFleetUiCommand(
        FleetCommandDefinition command,
        FleetCommandInvocationContext invocationContext)
    {
        if (!command.Arguments.TryGetValue(UiCommandArgument, out var uiCommand))
        {
            return false;
        }

        switch (uiCommand)
        {
            case GroupSkillsUiCommand:
                this.ShowGroupSkillsWindow(invocationContext.ActiveClient);
                return true;

            case FleetLootUiCommand:
                this.ShowFleetLootWindow(invocationContext.ActiveClient);
                return true;

            default:
                invocationContext.ActiveClient.AutomationStatus =
                    string.Concat("Unknown command UI action: ", uiCommand);
                return true;
        }
    }

    private void ShowGroupSkillsWindow(
        ClientInstance leader)
    {
        if (leader.LifecycleState != ClientLifecycleState.InGame)
        {
            leader.AutomationStatus = "Action HUD is available while the pilot is in game.";
            return;
        }

        if (this.groupSkillsForms.TryGetValue(
                leader.ProcessId,
                out var existing) &&
            !existing.IsDisposed)
        {
            _ = NativeMethods.TryBringWindowToTopWithoutActivation(existing.Handle);
            return;
        }

        var form = new GroupSkillsForm(this, leader.ProcessId);
        form.FormClosed += (_, _) =>
        {
            this.groupSkillsForms.Remove(leader.ProcessId);
        };

        this.groupSkillsForms[leader.ProcessId] = form;
        var owner = leader.HostForm as IWin32Window ?? this.automationOwner;

        if (owner == null)
        {
            form.Show();
        }
        else
        {
            form.Show(owner);
        }
    }

    private void ShowFleetLootWindow(
        ClientInstance leader,
        IWin32Window? preferredOwner = null)
    {
        if (this.fleetLootWindowForms.TryGetValue(
                leader.ProcessId,
                out var existing) &&
            !existing.IsDisposed)
        {
            if (preferredOwner is Form preferredOwnerForm &&
                !ReferenceEquals(existing.Owner, preferredOwnerForm))
            {
                // Keep an Action HUD-opened loot window above that HUD even
                // while loot automation briefly returns focus to the game.
                existing.Owner = preferredOwnerForm;
            }

            existing.Activate();
            return;
        }

        if (!this.sessionLootOwnerByLeaderProcessId.ContainsKey(leader.ProcessId))
        {
            this.sessionLootOwnerByLeaderProcessId[leader.ProcessId] = leader.ProcessId;
        }

        var form = new FleetLootWindowForm(this, leader.ProcessId);
        form.FormClosed += (_, _) =>
        {
            this.fleetLootWindowForms.Remove(leader.ProcessId);
        };

        this.fleetLootWindowForms[leader.ProcessId] = form;

        // An owned window is always stacked above its owner. When launched
        // from the Action HUD, that relationship is stronger and cleaner than
        // making either window globally topmost.
        var owner = preferredOwner ??
                    (leader.HostForm as IWin32Window) ??
                    this.automationOwner;

        if (owner == null)
        {
            form.Show();
        }
        else
        {
            form.Show(owner);
        }
    }

    public bool IsActionHudAvailable(int ownerProcessId)
    {
        return this.TryGetClient(ownerProcessId, out var owner) &&
               owner.LifecycleState == ClientLifecycleState.InGame;
    }

    public bool CanOpenFleetLootFromActionHud(int ownerProcessId)
    {
        return this.TryGetClient(ownerProcessId, out var owner) &&
               owner.LifecycleState == ClientLifecycleState.InGame &&
               this.clientObservationCoordinator.TryGetSnapshot(
                   ownerProcessId,
                   out var snapshot) &&
               IsActionHudLootShortcutAvailable(snapshot);
    }

    public void OpenFleetLootFromActionHud(
        int ownerProcessId,
        IWin32Window actionHudOwner)
    {
        if (!this.TryGetClient(ownerProcessId, out var owner) ||
            owner.LifecycleState != ClientLifecycleState.InGame ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                ownerProcessId,
                out var snapshot) ||
            !IsActionHudLootShortcutAvailable(snapshot))
        {
            return;
        }

        this.ShowFleetLootWindow(owner, actionHudOwner);
    }

    public void RequestGroupSkillsObservation(
        int leaderProcessId)
    {
        var processIds = this.clients.Values
            .Where(client => client.LifecycleState == ClientLifecycleState.InGame)
            .Select(client => client.ProcessId)
            .ToArray();

        this.clientObservationCoordinator.RequestGroupSkillsTargetPoll(processIds);
    }

    public void RestoreGroupSkillsOpenerFocus(
        int leaderProcessId)
    {
        if (!this.TryGetClient(leaderProcessId, out var leader) ||
            leader.GameWindowHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.FocusWindow(leader.GameWindowHandle);
    }

    public async Task ExecuteShortcutForClientAsync(
        int processId,
        GameShortcutInvocation shortcut,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(processId, out var client))
        {
            return;
        }

        var result = await this.gameShortcutInvocationService
            .ExecuteAsync(
                client,
                shortcut,
                cancellationToken: cancellationToken)
            .ConfigureAwait(true);

        client.AutomationStatus = result.Succeeded
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Invoked {shortcut.Name} via slot {shortcut.VisibleKey} (bar {shortcut.Bar}, group {shortcut.Group}, {result.Binding?.Preferred?.DisplayText ?? "unresolved key"})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Shortcut invoke failed for {shortcut.Name} (bar {shortcut.Bar}, group {shortcut.Group}, slot {shortcut.VisibleKey}): {result.Error}");
    }

    public async Task ExecuteFireAllForClientAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(processId, out var client))
        {
            return;
        }

        var result = await this.gameCommandCoordinator
            .ExecuteAsync(
                client,
                GameCommand.FireAll,
                cancellationToken)
            .ConfigureAwait(true);

        client.AutomationStatus = result.Succeeded
            ? "Fire All sent"
            : result.Error;
    }

    public async Task<GroupSkillsActionResult> ExecuteGroupSkillsFireAllAsync(
        int leaderProcessId,
        int casterProcessId,
        GroupSkillsTargetRow selectedTarget,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(casterProcessId, out var caster))
        {
            return GroupSkillsActionResult.Failure("The selected pilot is no longer available.");
        }

        var targetResult = await this.TargetSelectedGroupSkillsTargetAsync(
                leaderProcessId,
                caster,
                selectedTarget,
                cancellationToken)
            .ConfigureAwait(true);

        if (!targetResult.Succeeded)
        {
            caster.AutomationStatus = string.Concat(
                "Action HUD targeting failed: ",
                targetResult.Message);
            return targetResult;
        }

        await Task.Delay(
                groupSkillsInputSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        var result = await this.gameCommandCoordinator
            .ExecuteAsync(
                caster,
                GameCommand.FireAll,
                cancellationToken)
            .ConfigureAwait(true);

        caster.AutomationStatus = result.Succeeded
            ? string.Concat(
                "Action HUD: Fire All on ",
                FormatGroupSkillsTargetName(selectedTarget))
            : result.Error;

        return result.Succeeded
            ? GroupSkillsActionResult.Success(
                string.Concat(
                    "Fire All sent to ",
                    FormatGroupSkillsTargetName(selectedTarget),
                    "."))
            : GroupSkillsActionResult.Failure(result.Error);
    }

    public async Task<GroupSkillsActionResult> ExecuteGroupSkillsShortcutAsync(
        int leaderProcessId,
        int casterProcessId,
        GameShortcutInvocation shortcut,
        GroupSkillsTargetRow selectedTarget,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(casterProcessId, out var caster))
        {
            return GroupSkillsActionResult.Failure("The selected pilot is no longer available.");
        }

        var targetResult = await this.TargetSelectedGroupSkillsTargetAsync(
                leaderProcessId,
                caster,
                selectedTarget,
                cancellationToken)
            .ConfigureAwait(true);

        if (!targetResult.Succeeded)
        {
            caster.AutomationStatus = string.Concat(
                "Action HUD targeting failed: ",
                targetResult.Message);
            return targetResult;
        }

        await Task.Delay(
                groupSkillsInputSettleDelay,
                cancellationToken)
            .ConfigureAwait(true);

        return await this.InvokeGroupSkillsShortcutDirectAsync(
                caster,
                shortcut,
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<GroupSkillsActionResult> TargetSelectedGroupSkillsTargetAsync(
        int leaderProcessId,
        ClientInstance caster,
        GroupSkillsTargetRow selectedTarget,
        CancellationToken cancellationToken)
    {
        if (selectedTarget.IsLeaderTarget && !selectedTarget.HasTarget)
        {
            return GroupSkillsActionResult.Success(
                "Group Target is empty; preserving the acting pilot's current target.");
        }

        return selectedTarget.IsLeaderTarget
            ? await this.TargetGroupSkillsGroupTargetAsync(
                    leaderProcessId,
                    caster,
                    cancellationToken)
                .ConfigureAwait(true)
            : await this.TargetGroupSkillsMemberByNameAsync(
                    caster,
                    selectedTarget.Name,
                    cancellationToken)
                .ConfigureAwait(true);
    }

    private static string FormatGroupSkillsTargetName(
        GroupSkillsTargetRow target)
    {
        if (target.IsLeaderTarget)
        {
            if (!target.HasTarget)
            {
                return "current target";
            }

            return string.IsNullOrWhiteSpace(target.Name)
                ? "Group Target"
                : string.Concat("Group Target ", target.Name);
        }

        return target.Name;
    }

    private async Task<GroupSkillsActionResult> InvokeGroupSkillsShortcutDirectAsync(
        ClientInstance caster,
        GameShortcutInvocation shortcut,
        CancellationToken cancellationToken)
    {
        var result = await this.gameShortcutInvocationService
            .ExecuteAsync(
                caster,
                shortcut,
                cancellationToken)
            .ConfigureAwait(true);

        if (result.Succeeded)
        {
            caster.AutomationStatus = string.Create(
                CultureInfo.InvariantCulture,
                $"Action HUD: invoked {shortcut.Name} via key slot {shortcut.VisibleKey} (bar {shortcut.Bar}, group {shortcut.Group}, {result.Binding?.Preferred?.DisplayText ?? "resolved key"})");

            return GroupSkillsActionResult.Success(string.Concat("Invoked ", shortcut.Name, "."));
        }

        var fallbackResult = await this.InvokeGroupSkillsShortcutClickFallbackAsync(
                caster,
                shortcut,
                result.Error,
                cancellationToken)
            .ConfigureAwait(true);

        caster.AutomationStatus = fallbackResult.Succeeded
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Action HUD: invoked {shortcut.Name} via click fallback slot {shortcut.VisibleKey} (bar {shortcut.Bar}, group {shortcut.Group})")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Action HUD shortcut failed for {shortcut.Name}: {fallbackResult.Message}");

        return fallbackResult;
    }

    private async Task<GroupSkillsActionResult> InvokeGroupSkillsShortcutClickFallbackAsync(
        ClientInstance caster,
        GameShortcutInvocation shortcut,
        string keyboardError,
        CancellationToken cancellationToken)
    {
        var slotActionName = GetShortcutBarSlotInputActionName(shortcut.VisibleKey);
        var slotAction = this.FindInputAction(slotActionName);

        if (slotAction == null)
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Keyboard shortcut invocation failed (",
                    keyboardError,
                    ") and click fallback action is missing: ",
                    slotActionName));
        }

        if (!this.TryReadShortcutBarGroup(
                caster.ProcessId,
                shortcut.Bar,
                out var currentGroup,
                out var groupError))
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Keyboard shortcut invocation failed (",
                    keyboardError,
                    ") and click fallback could not verify the visible shortcut bank: ",
                    groupError));
        }


        if (currentGroup == shortcut.Group)
        {
            var visibleClickResult = await this.ExecuteInputActionForClientAsync(
                    caster,
                    slotAction,
                    cancellationToken)
                .ConfigureAwait(true);

            return visibleClickResult;
        }

        if (shortcut.Group == 0)
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Keyboard shortcut invocation failed (",
                    keyboardError,
                    ") and group-0 click fallback is unsafe because shortcut bar ",
                    shortcut.Bar.ToString(CultureInfo.InvariantCulture),
                    " is currently showing group ",
                    currentGroup.ToString(CultureInfo.InvariantCulture),
                    "."));
        }

        var swapBinding = await this.gameKeyBindingResolver
            .ResolveAsync(
                caster,
                GameCommand.SwapShortcutBanks,
                cancellationToken)
            .ConfigureAwait(true);

        if (!swapBinding.Succeeded || swapBinding.Preferred == null)
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Keyboard shortcut invocation failed (",
                    keyboardError,
                    ") and group-1 click fallback needs the Shift Shortcuts modifier: ",
                    swapBinding.Error));
        }

        var alternateClickResult = await this.ExecuteMouseClickInputActionWithHeldChordAsync(
                caster,
                slotAction,
                swapBinding.Preferred,
                token => this.WaitForShortcutBarGroupAsync(
                    caster.ProcessId,
                    shortcut.Bar,
                    expectedGroup: 1,
                    token),
                cancellationToken)
            .ConfigureAwait(true);

        return alternateClickResult.Succeeded
            ? GroupSkillsActionResult.Success(string.Concat("Clicked ", slotAction.Name, " with Shift Shortcuts held."))
            : GroupSkillsActionResult.Failure(
                string.Concat(
                    "Keyboard shortcut invocation failed (",
                    keyboardError,
                    ") and click fallback failed: ",
                    alternateClickResult.Error));
    }

    private async Task<GroupSkillsActionResult> TargetGroupSkillsGroupTargetAsync(
        int leaderProcessId,
        ClientInstance caster,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(leaderProcessId, out var leader) ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                leaderProcessId,
                out var leaderSnapshot))
        {
            return GroupSkillsActionResult.Failure("The Action HUD owner is no longer available.");
        }

        if (!leaderSnapshot.Target.HasTarget)
        {
            return GroupSkillsActionResult.Failure("Group Target is empty.");
        }

        if (caster.ProcessId == leaderProcessId)
        {
            return GroupSkillsActionResult.Success("Acting pilot already owns Group Target.");
        }

        if (this.clientObservationCoordinator.TryGetSnapshot(
                caster.ProcessId,
                out var casterSnapshot) &&
            TargetsMatch(casterSnapshot.Target, leaderSnapshot.Target))
        {
            return GroupSkillsActionResult.Success("Acting pilot already targets Group Target.");
        }

        var leaderName = GetObservedCharacterName(leader);

        if (string.IsNullOrWhiteSpace(leaderName))
        {
            return GroupSkillsActionResult.Failure("Could not resolve the Action HUD owner's character name.");
        }

        if (!this.TryResolveActingClientGroupMemberIndex(
                caster.ProcessId,
                leaderName,
                out var memberIndex,
                out var indexError))
        {
            return GroupSkillsActionResult.Failure(indexError);
        }

        var actionResult = await this.ExecuteNamedInputActionForClientAsync(
                caster,
                GetTargetOfGroupMemberInputActionName(memberIndex),
                cancellationToken)
            .ConfigureAwait(true);

        if (!actionResult.Succeeded)
        {
            return actionResult;
        }

        return await this.WaitForGroupTargetMatchAsync(
                caster.ProcessId,
                leaderProcessId,
                cancellationToken)
            .ConfigureAwait(true)
            ? GroupSkillsActionResult.Success("Selected Group Target on acting pilot.")
            : GroupSkillsActionResult.Failure(
                string.Concat(
                    "Clicked Target Of Group Member ",
                    memberIndex.ToString(CultureInfo.InvariantCulture),
                    ", but acting pilot target did not match Group Target."));
    }

    private async Task<GroupSkillsActionResult> TargetGroupSkillsMemberByNameAsync(
        ClientInstance caster,
        string targetName,
        CancellationToken cancellationToken)
    {
        targetName = targetName.Trim();

        if (targetName.Length == 0)
        {
            return GroupSkillsActionResult.Failure("Selected group member has no name.");
        }

        var casterName = GetObservedCharacterName(caster);

        if (this.clientObservationCoordinator.TryGetSnapshot(
                caster.ProcessId,
                out var casterSnapshot) &&
            IsTargetName(casterSnapshot.Target, targetName))
        {
            return GroupSkillsActionResult.Success(
                string.Concat("Acting pilot already targets ", targetName, "."));
        }

        if (!string.IsNullOrWhiteSpace(casterName) &&
            string.Equals(
                casterName,
                targetName,
                StringComparison.OrdinalIgnoreCase))
        {
            return await this.TargetGroupSkillsSelfAsync(
                    caster,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        if (!this.TryResolveActingClientGroupMemberIndex(
                caster.ProcessId,
                targetName,
                out var memberIndex,
                out var indexError))
        {
            return GroupSkillsActionResult.Failure(indexError);
        }

        var command = GetTargetGroupMemberCommand(memberIndex);
        var keyResult = await this.gameCommandCoordinator
            .ExecuteAsync(
                caster,
                command,
                cancellationToken)
            .ConfigureAwait(true);

        if (keyResult.Succeeded &&
            await this.WaitForTargetNameAsync(
                    caster.ProcessId,
                    targetName,
                    cancellationToken)
                .ConfigureAwait(true))
        {
            return GroupSkillsActionResult.Success(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Selected {targetName} via {keyResult.Binding?.Preferred?.DisplayText ?? command.ToString()}."));
        }

        var actionName = GetSelectGroupMemberInputActionName(memberIndex);
        var clickResult = await this.ExecuteNamedInputActionForClientAsync(
                caster,
                actionName,
                cancellationToken)
            .ConfigureAwait(true);

        if (!clickResult.Succeeded)
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Could not target ",
                    targetName,
                    " via keybinding (",
                    keyResult.Error,
                    ") or click fallback (",
                    clickResult.Message,
                    ")."));
        }

        return await this.WaitForTargetNameAsync(
                caster.ProcessId,
                targetName,
                cancellationToken)
            .ConfigureAwait(true)
            ? GroupSkillsActionResult.Success(
                string.Concat("Selected ", targetName, " via click fallback."))
            : GroupSkillsActionResult.Failure(
                string.Concat(
                    "Clicked ",
                    actionName,
                    ", but acting pilot target did not become ",
                    targetName,
                    "."));
    }

    private async Task<GroupSkillsActionResult> TargetGroupSkillsSelfAsync(
        ClientInstance caster,
        CancellationToken cancellationToken)
    {
        if (this.clientObservationCoordinator.TryGetSnapshot(
                caster.ProcessId,
                out var snapshot) &&
            snapshot.Target.IsSelf)
        {
            return GroupSkillsActionResult.Success("Acting pilot already targets self.");
        }

        var result = await this.gameCommandCoordinator
            .ExecuteAsync(
                caster,
                GameCommand.TargetSelf,
                cancellationToken)
            .ConfigureAwait(true);

        if (!result.Succeeded)
        {
            return GroupSkillsActionResult.Failure(
                string.Concat(
                    "Target Self keybinding is required to target the acting pilot, but it is unavailable: ",
                    result.Error));
        }

        return await this.WaitForTargetSelfAsync(
                caster.ProcessId,
                cancellationToken)
            .ConfigureAwait(true)
            ? GroupSkillsActionResult.Success("Selected acting pilot through Target Self.")
            : GroupSkillsActionResult.Failure("Target Self was sent, but acting pilot target did not become self.");
    }

    private bool TryResolveActingClientGroupMemberIndex(
        int actingProcessId,
        string characterName,
        out int memberIndex,
        out string error)
    {
        memberIndex = 0;
        error = "";

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                actingProcessId,
                out var snapshot))
        {
            error = "Acting pilot group state is unavailable.";
            return false;
        }

        var member = snapshot.Group.Members.FirstOrDefault(candidate =>
            candidate.IsPresent &&
            string.Equals(
                candidate.Name,
                characterName,
                StringComparison.OrdinalIgnoreCase));

        if (member == null)
        {
            error = string.Concat(
                "Could not find '",
                characterName,
                "' in acting pilot's group list.");
            return false;
        }

        if (member.Slot is < 1 or > 5)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Group member '{characterName}' resolved to invalid acting-client slot {member.Slot}.");
            return false;
        }

        memberIndex = member.Slot;
        return true;
    }

    private async Task<bool> WaitForTargetNameAsync(
        int processId,
        string targetName,
        CancellationToken cancellationToken)
    {
        return await this.WaitForTargetPredicateAsync(
                processId,
                target => IsTargetName(target, targetName),
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<bool> WaitForTargetSelfAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        return await this.WaitForTargetPredicateAsync(
                processId,
                target => target.HasTarget && target.IsSelf,
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<bool> WaitForGroupTargetMatchAsync(
        int casterProcessId,
        int leaderProcessId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + groupSkillsTargetVerifyTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.clientObservationCoordinator.RequestGroupSkillsTargetPoll(
                [casterProcessId, leaderProcessId]);

            if (this.clientObservationCoordinator.TryGetSnapshot(
                    casterProcessId,
                    out var casterSnapshot) &&
                this.clientObservationCoordinator.TryGetSnapshot(
                    leaderProcessId,
                    out var leaderSnapshot) &&
                TargetsMatch(casterSnapshot.Target, leaderSnapshot.Target))
            {
                return true;
            }

            await Task.Delay(
                    groupSkillsTargetVerifyPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private async Task<bool> WaitForTargetPredicateAsync(
        int processId,
        Func<ClientTargetObservation, bool> predicate,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + groupSkillsTargetVerifyTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.clientObservationCoordinator.RequestGroupSkillsTargetPoll([processId]);

            if (this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot) &&
                predicate(snapshot.Target))
            {
                return true;
            }

            await Task.Delay(
                    groupSkillsTargetVerifyPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private static bool TargetsMatch(
        ClientTargetObservation actual,
        ClientTargetObservation desired)
    {
        if (!actual.HasTarget ||
            !desired.HasTarget)
        {
            return false;
        }

        if (actual.ObjectId is not 0 and not uint.MaxValue &&
            desired.ObjectId is not 0 and not uint.MaxValue)
        {
            return actual.ObjectId == desired.ObjectId;
        }

        return !string.IsNullOrWhiteSpace(actual.Name) &&
               string.Equals(
                   actual.Name,
                   desired.Name,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTargetName(
        ClientTargetObservation target,
        string targetName)
    {
        return target.HasTarget &&
               !string.IsNullOrWhiteSpace(target.Name) &&
               string.Equals(
                   target.Name,
                   targetName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<InputActionExecutionResult> ExecuteMouseClickInputActionWithHeldChordAsync(
        ClientInstance client,
        InputActionDefinition action,
        GameKeyChord heldChord,
        Func<CancellationToken, Task<bool>>? waitAfterHeldChordAsync,
        CancellationToken cancellationToken)
    {
        if (action.Kind != InputActionKind.MouseClick)
        {
            return InputActionExecutionResult.Failure(
                $"Action '{action.Name}' is not a mouse click.");
        }

        if (client.GameWindowHandle == IntPtr.Zero)
        {
            return InputActionExecutionResult.Failure("The game window is unavailable.");
        }

        using var foregroundLease = await this.foregroundInputCoordinator
            .AcquireAsync(cancellationToken)
            .ConfigureAwait(true);

        if (!NativeMethods.TryGetClientSize(
                client.GameWindowHandle,
                out var clientSize))
        {
            return InputActionExecutionResult.Failure("Could not get the game viewport size.");
        }

        var x = (int)Math.Round(
            action.BaseX * clientSize.Width / action.BaseWidth);

        var y = (int)Math.Round(
            action.BaseY * clientSize.Height / action.BaseHeight);

        if (!NativeMethods.TryConvertClientPointToScreen(
                client.GameWindowHandle,
                new Point(x, y),
                out var screenPoint))
        {
            return InputActionExecutionResult.Failure(
                $"Could not translate click point for action '{action.Name}'.");
        }

        var sent = await Win32.NativeMethods
            .TryForegroundStableLeftClickWithHeldChordAsync(
                client.GameWindowHandle,
                screenPoint,
                heldChord,
                waitAfterHeldChordAsync,
                cancellationToken)
            .ConfigureAwait(true);

        return sent
            ? InputActionExecutionResult.Success()
            : InputActionExecutionResult.Failure(
                $"Could not focus or click the game window for action '{action.Name}' while holding {heldChord.DisplayText}.");
    }

    private async Task<bool> WaitForShortcutBarGroupAsync(
        int processId,
        int bar,
        int expectedGroup,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + groupSkillsShortcutBankSettleTimeout;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.TryReadShortcutBarGroup(
                    processId,
                    bar,
                    out var currentGroup,
                    out _) &&
                currentGroup == expectedGroup)
            {
                return true;
            }

            await Task.Delay(
                    groupSkillsShortcutBankSettlePollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return false;
    }

    private bool TryReadShortcutBarGroup(
        int processId,
        int bar,
        out int currentGroup,
        out string error)
    {
        currentGroup = -1;

        if (!this.clientObservationCoordinator.TryReadShortcutState(
                processId,
                out var shortcuts,
                out error))
        {
            return false;
        }

        var shortcutBar = shortcuts.GetBar(bar);

        if (shortcutBar?.CurrentGroup is not { } observedGroup)
        {
            error = string.Concat(
                "Shortcut bar ",
                bar.ToString(CultureInfo.InvariantCulture),
                " is unavailable: ",
                shortcutBar?.Status ?? shortcuts.Status);
            return false;
        }

        currentGroup = observedGroup;
        return true;
    }

    private static GameCommand GetTargetGroupMemberCommand(
        int memberIndex)
    {
        return memberIndex switch
        {
            1 => GameCommand.TargetGroupMember1,
            2 => GameCommand.TargetGroupMember2,
            3 => GameCommand.TargetGroupMember3,
            4 => GameCommand.TargetGroupMember4,
            5 => GameCommand.TargetGroupMember5,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };
    }

    private static string GetSelectGroupMemberInputActionName(
        int memberIndex)
    {
        return memberIndex switch
        {
            1 => BuiltInInputActionProvider.SelectGroupMember1Name,
            2 => BuiltInInputActionProvider.SelectGroupMember2Name,
            3 => BuiltInInputActionProvider.SelectGroupMember3Name,
            4 => BuiltInInputActionProvider.SelectGroupMember4Name,
            5 => BuiltInInputActionProvider.SelectGroupMember5Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };
    }

    private static string GetTargetOfGroupMemberInputActionName(
        int memberIndex)
    {
        return memberIndex switch
        {
            1 => BuiltInInputActionProvider.TargetOfGroupMember1Name,
            2 => BuiltInInputActionProvider.TargetOfGroupMember2Name,
            3 => BuiltInInputActionProvider.TargetOfGroupMember3Name,
            4 => BuiltInInputActionProvider.TargetOfGroupMember4Name,
            5 => BuiltInInputActionProvider.TargetOfGroupMember5Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(memberIndex),
                memberIndex,
                "Group member index must be 1..5."),
        };
    }

    private static string GetShortcutBarSlotInputActionName(
        int visibleKey)
    {
        return visibleKey switch
        {
            1 => BuiltInInputActionProvider.ShortcutBarSlot1Name,
            2 => BuiltInInputActionProvider.ShortcutBarSlot2Name,
            3 => BuiltInInputActionProvider.ShortcutBarSlot3Name,
            4 => BuiltInInputActionProvider.ShortcutBarSlot4Name,
            5 => BuiltInInputActionProvider.ShortcutBarSlot5Name,
            6 => BuiltInInputActionProvider.ShortcutBarSlot6Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(visibleKey),
                visibleKey,
                "Shortcut visible key must be 1..6."),
        };
    }

    private bool IsCurrentTargetControlledGroupMember(
        int leaderProcessId,
        int casterProcessId)
    {
        if (!this.clientObservationCoordinator.TryGetSnapshot(
                leaderProcessId,
                out var leaderSnapshot) ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                casterProcessId,
                out var casterSnapshot) ||
            !casterSnapshot.Target.HasTarget)
        {
            return false;
        }

        var controlledObjectIds = this.GetControlledGroupClients(leaderSnapshot)
            .Select(client => this.clientObservationCoordinator.TryGetSnapshot(
                client.ProcessId,
                out var snapshot)
                ? ClientLiveCharacterIdentityResolver.Resolve(snapshot).CharacterObjectId
                : null)
            .Where(objectId => objectId is not null and not 0 and not uint.MaxValue)
            .Select(objectId => objectId!.Value)
            .ToHashSet();

        return controlledObjectIds.Contains(casterSnapshot.Target.ObjectId) ||
               casterSnapshot.Target.IsGroupMember ||
               leaderSnapshot.Group.ContainsObjectId(casterSnapshot.Target.ObjectId);
    }

    private bool TryResolveGroupSkillsTarget(
        int leaderProcessId,
        int targetProcessId,
        out GroupSkillsTargetRow target,
        out string error)
    {
        target = null!;
        error = "";

        if (!this.TryGetClient(leaderProcessId, out var leader) ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                leaderProcessId,
                out var leaderSnapshot))
        {
            error = "The leader client is no longer available.";
            return false;
        }

        var targets = this.BuildLeaderPerspectiveTargets(
            leader,
            leaderSnapshot,
            this.GetControlledGroupClients(leaderSnapshot));

        target = targets.FirstOrDefault(candidate =>
            candidate.ProcessId == targetProcessId &&
            !candidate.IsLeaderTarget)!;

        if (target == null)
        {
            error = "The requested group target is no longer available.";
            return false;
        }

        return true;
    }

    private InputActionDefinition? FindInputAction(
        string actionName)
    {
        return new InputActionCatalog(new BuiltInInputActionProvider())
            .FindByName(actionName);
    }

    private async Task<GroupSkillsActionResult> ExecuteNamedInputActionForClientAsync(
        ClientInstance client,
        string actionName,
        CancellationToken cancellationToken)
    {
        var action = this.FindInputAction(actionName);

        if (action == null)
        {
            var message = string.Concat("Missing input action: ", actionName);
            client.AutomationStatus = message;
            return GroupSkillsActionResult.Failure(message);
        }

        return await this.ExecuteInputActionForClientAsync(
                client,
                action,
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<GroupSkillsActionResult> ExecuteInputActionForClientAsync(
        ClientInstance client,
        InputActionDefinition action,
        CancellationToken cancellationToken)
    {
        var executor = new InputActionExecutor(
            this.gameCommandCoordinator,
            this.foregroundInputCoordinator);

        var result = await executor.ExecuteAsync(
                client,
                action,
                cancellationToken)
            .ConfigureAwait(true);

        client.AutomationStatus = result.Succeeded
            ? string.Concat("Clicked ", action.Name)
            : result.Error;

        return result.Succeeded
            ? GroupSkillsActionResult.Success(string.Concat("Selected ", action.Name, "."))
            : GroupSkillsActionResult.Failure(result.Error);
    }

    internal Image? GetGroupSkillsActionIcon(
        int processId,
        GameShortcutPaletteEntry action,
        Size size)
    {
        return this.TryGetClient(processId, out var client)
            ? this.gameIconService.GetShortcutIcon(client, action, size)
            : null;
    }

    internal Image? GetGameItemIcon(
        int processId,
        int? itemTemplateId,
        Size size)
    {
        return this.TryGetClient(processId, out var client)
            ? this.gameIconService.GetItemIcon(client, itemTemplateId, size)
            : null;
    }

    internal Image? GetPilotArchiveItemIcon(
        string? outputDirectory,
        int? itemTemplateId,
        Size size)
    {
        return outputDirectory == null
            ? null
            : this.gameIconService.GetItemIcon(
                outputDirectory,
                itemTemplateId,
                size);
    }

    internal Image? GetPilotArchiveSkillIcon(
        string? outputDirectory,
        PilotArchiveSkill skill,
        Size size)
    {
        if (outputDirectory == null)
        {
            return null;
        }

        var catalog = this.skillIniCatalogService.GetCatalog(outputDirectory);

        // The archived skill index is the same canonical family index used
        // by the client's static skill vector and cskill_t.ini [All Skills].
        // Pilot Archive rows represent skill families, so resolve the family
        // artwork from cskill_ex_t.ini rather than an ability-specific
        // skilla_* presentation resource.
        if (!catalog.TryResolveSkillFamilyIcon(
                skill.Index,
                skill.Name,
                skill.CurrentRank,
                out var definition))
        {
            return null;
        }

        foreach (var iconResourceName in definition.IconResourceNames)
        {
            var icon = this.gameIconService.GetResourceIcon(
                outputDirectory,
                iconResourceName,
                size,
                definition.TintRed,
                definition.TintGreen,
                definition.TintBlue);

            if (icon != null)
            {
                return icon;
            }
        }

        return null;
    }

    internal float? ResolveGroupSkillsTargetDistance(
        int actingProcessId,
        GroupSkillsTargetRow selectedTarget)
    {
        if (selectedTarget.ProcessId == actingProcessId)
        {
            return 0.0f;
        }

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                actingProcessId,
                out var actingSnapshot))
        {
            return selectedTarget.SurfaceDistance;
        }

        if (selectedTarget.ObjectId is not 0 and not uint.MaxValue)
        {
            if (actingSnapshot.Target.HasTarget &&
                actingSnapshot.Target.ObjectId == selectedTarget.ObjectId &&
                actingSnapshot.Target.Distance.IsAvailable)
            {
                return actingSnapshot.Target.Distance.SurfaceDistance;
            }

            var groupMember = actingSnapshot.Group.Members.FirstOrDefault(member =>
                member.ObjectId == selectedTarget.ObjectId);

            if (groupMember?.Distance.IsAvailable == true)
            {
                return groupMember.Distance.SurfaceDistance;
            }
        }

        if (!selectedTarget.IsLeaderTarget)
        {
            var groupMember = actingSnapshot.Group.Members.FirstOrDefault(member =>
                string.Equals(
                    member.Name,
                    selectedTarget.Name,
                    StringComparison.OrdinalIgnoreCase));

            if (groupMember?.Distance.IsAvailable == true)
            {
                return groupMember.Distance.SurfaceDistance;
            }
        }

        return selectedTarget.SurfaceDistance;
    }

    internal async Task<string?> ResolveGameCommandKeyLabelAsync(
        int processId,
        GameCommand command,
        CancellationToken cancellationToken)
    {
        if (!this.TryGetClient(processId, out var client))
        {
            return null;
        }

        var resolution = await this.gameKeyBindingResolver
            .ResolveAsync(
                client,
                command,
                cancellationToken)
            .ConfigureAwait(false);

        return resolution.Succeeded
            ? resolution.Preferred?.DisplayText
            : null;
    }

    public IReadOnlyList<GroupSkillsPilotCard> BuildGroupSkillsCards(
        int leaderProcessId)
    {
        if (!this.TryGetClient(leaderProcessId, out var leader) ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                leaderProcessId,
                out var leaderSnapshot))
        {
            return [];
        }

        var cards = new List<GroupSkillsPilotCard>();
        var controlledGroupMembers = this.GetControlledGroupClients(leaderSnapshot)
            .ToList();
        var targets = this.BuildLeaderPerspectiveTargets(
            leader,
            leaderSnapshot,
            controlledGroupMembers);

        foreach (var client in controlledGroupMembers)
        {
            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    client.ProcessId,
                    out var snapshot))
            {
                continue;
            }

            var shortcuts = this.clientObservationCoordinator.TryReadShortcutState(
                client.ProcessId,
                out var directShortcuts,
                out _)
                ? directShortcuts
                : snapshot.Shortcuts;

            var actions = this.gameShortcutPaletteService
                .BuildEntries(client, snapshot, shortcuts)
                .Where(action => !action.IsIndividualWeapon)
                .Where(action =>
                    action.ItemDetails?.IsNativeAction != false)
                .ToList();

            var inventory = snapshot.LocalPlayer.Inventory;
            var fireAllWeapons = inventory.EquippedSlots
                .Where(slot =>
                    slot.IsOccupied &&
                    inventory.GetEquipmentSlotKind(slot.Slot) ==
                        ClientEquipmentSlotKind.Weapon)
                .OrderBy(slot => slot.Slot)
                .Select(slot => CreateGroupSkillsFireAllWeapon(
                    inventory,
                    slot))
                .ToList();
            var fireAllIconItemTemplateId = fireAllWeapons
                .Select(weapon => (int?)weapon.ItemTemplateId)
                .FirstOrDefault();

            var tooltipDelayMilliseconds =
                snapshot.TooltipDelay.IsAvailable
                    ? snapshot.TooltipDelay.DelayMilliseconds
                    : ClientTooltipDelayObservation
                        .DefaultDelayMilliseconds;

            cards.Add(
                new GroupSkillsPilotCard(
                    client.ProcessId,
                    GetObservedCharacterName(client) ?? BuildNeutralClientLabel(client.ProcessId),
                    targets,
                    actions,
                    fireAllIconItemTemplateId,
                    fireAllWeapons,
                    tooltipDelayMilliseconds));
        }

        return cards;
    }


    private static GroupSkillsFireAllWeapon CreateGroupSkillsFireAllWeapon(
        ClientInventoryObservation inventory,
        ClientInventoryItemObservation weapon)
    {
        var weaponTemplate = weapon.Template;
        var ammo = inventory.GetAmmoForEquipmentSlot(
            weapon.Slot);
        var occupiedAmmo = ammo?.IsOccupied == true
            ? ammo
            : null;
        var weaponType = weaponTemplate?.TypeDisplayName ?? "";
        var weaponTechLevel = weaponTemplate?.TechLevel;
        var ammoTechLevel = occupiedAmmo?.Template?.TechLevel;
        var requiresAmmo = occupiedAmmo != null ||
                           WeaponTypeRequiresAmmo(weaponType);
        var weaponName = FirstNonEmpty(
            weaponTemplate?.Name,
            ClientItemTemplateNameResolver.GetKnownName(
                weapon.ItemTemplateId),
            string.Create(
                CultureInfo.InvariantCulture,
                $"Weapon slot {weapon.Slot}"));
        var ammoName = occupiedAmmo == null
            ? null
            : FirstNonEmpty(
                occupiedAmmo.Template?.Name,
                ClientItemTemplateNameResolver.GetKnownName(
                    occupiedAmmo.ItemTemplateId),
                "Loaded ammunition");
        var operational = weapon.Operational;

        return new GroupSkillsFireAllWeapon(
            weapon.ItemTemplateId!.Value,
            weaponName,
            weaponTechLevel is > 0
                ? weaponTechLevel
                : null,
            requiresAmmo,
            ammoName,
            ammoTechLevel is > 0
                ? ammoTechLevel
                : null,
            occupiedAmmo?.StackCount,
            operational.IsBusy,
            operational.IsOperationallyReady,
            operational.IsInPostDeadlineBusyTail,
            operational.NominalRemainingMilliseconds,
            operational.TargetRange is > 0
                ? operational.TargetRange
                : null);
    }

    private static string FirstNonEmpty(
        string? first,
        string? second,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first.Trim();
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second.Trim();
        }

        return fallback.Trim();
    }

    private static bool WeaponTypeRequiresAmmo(
        string typeDisplayName)
    {
        return typeDisplayName.Contains(
                   "Projectile",
                   StringComparison.OrdinalIgnoreCase) ||
               typeDisplayName.Contains(
                   "Missile",
                   StringComparison.OrdinalIgnoreCase);
    }

    public FleetLootWindowSnapshot BuildFleetLootWindowSnapshot(
        int leaderProcessId)
    {
        if (!this.TryResolveFleetLootContext(
                leaderProcessId,
                out var leader,
                out var leaderSnapshot,
                out var controlledGroupMembers,
                out _))
        {
            return new FleetLootWindowSnapshot(
                "Loot",
                "",
                [],
                [],
                RoundRobinEnabled: false,
                TargetObjectId: 0,
                ActiveLooterProcessId: null,
                ActiveLooterName: null,
                IsBusy: false);
        }

        if (!this.sessionLootOwnerByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var selectedProcessId) ||
            !controlledGroupMembers.Any(client =>
                client.ProcessId == selectedProcessId))
        {
            selectedProcessId = controlledGroupMembers
                .FirstOrDefault(client => client.ProcessId == leaderProcessId)
                ?.ProcessId ??
                controlledGroupMembers.First().ProcessId;
            this.sessionLootOwnerByLeaderProcessId[leaderProcessId] = selectedProcessId;
        }

        var roundRobinEnabled =
            this.roundRobinLootEnabledByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var enabled) &&
            enabled;

        var assignment = this.ResolveFleetLootAssignment(
            leaderProcessId,
            leader,
            leaderSnapshot,
            controlledGroupMembers,
            createIfMissing: false);

        var looters = controlledGroupMembers
            .Select(client => this.CreateLooterOption(
                client,
                selectedProcessId))
            .ToList();

        ClientInstance? activeLooter = null;
        string? activeOwnerName = null;

        if (assignment != null &&
            this.TryGetClient(assignment.LooterProcessId, out var assignedClient))
        {
            activeLooter = assignedClient;
            activeOwnerName = GetObservedCharacterName(activeLooter) ??
                              BuildNeutralClientLabel(activeLooter.ProcessId);
        }

        var sourceSnapshot = this.ResolveFleetLootSourceSnapshot(
            assignment,
            leaderSnapshot);

        var items = sourceSnapshot.Target.Corpse.LootItems
            .OrderBy(slot => slot.Slot)
            .Select((slot, visualSlotIndex) =>
            {
                var itemName = slot.ItemTemplateId.HasValue
                    ? ClientItemTemplateNameResolver.GetKnownName(slot.ItemTemplateId.Value)
                    : null;

                if (string.IsNullOrWhiteSpace(itemName))
                {
                    itemName = slot.ItemTemplateId.HasValue
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"Item template {slot.ItemTemplateId.Value}")
                        : "Unknown loot";
                }

                return new FleetLootItemRow(
                    visualSlotIndex,
                    slot.Slot,
                    slot.ItemTemplateId,
                    itemName,
                    slot.StackCount,
                    slot.QualityPercent);
            })
            .ToList();

        var isTractoring = activeLooter != null &&
                           this.clientObservationCoordinator.TryGetSnapshot(
                               activeLooter.ProcessId,
                               out var activeSnapshot) &&
                           activeSnapshot.LootTractor.IsTractoring;

        return new FleetLootWindowSnapshot(
            assignment == null || string.IsNullOrWhiteSpace(activeOwnerName)
                ? "Loot"
                : string.Concat(activeOwnerName, "'s loot"),
            leaderSnapshot.Target.HasTarget
                ? leaderSnapshot.Target.Name ?? ""
                : "",
            looters,
            items,
            roundRobinEnabled,
            leaderSnapshot.Target.ObjectId,
            assignment?.LooterProcessId,
            activeOwnerName,
            isTractoring);
    }

    public void SetSessionLootOwner(
        int leaderProcessId,
        int looterProcessId)
    {
        this.sessionLootOwnerByLeaderProcessId[leaderProcessId] = looterProcessId;
        this.roundRobinLootEnabledByLeaderProcessId[leaderProcessId] = false;
    }

    public void SetSessionLootRoundRobinEnabled(
        int leaderProcessId,
        bool enabled)
    {
        this.roundRobinLootEnabledByLeaderProcessId[leaderProcessId] = enabled;
    }

    private bool TryResolveFleetLootContext(
        int leaderProcessId,
        out ClientInstance leader,
        out ClientObservationSnapshot leaderSnapshot,
        out List<ClientInstance> controlledGroupMembers,
        out string error)
    {
        leader = null!;
        leaderSnapshot = null!;
        controlledGroupMembers = [];
        error = "";

        if (!this.TryGetClient(leaderProcessId, out leader) ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                leaderProcessId,
                out leaderSnapshot))
        {
            error = "The loot source client is no longer available.";
            return false;
        }

        controlledGroupMembers = this.GetControlledGroupClients(leaderSnapshot)
            .ToList();

        if (controlledGroupMembers.Count == 0)
        {
            controlledGroupMembers.Add(leader);
        }

        return true;
    }

    private FleetLootCorpseAssignment? ResolveFleetLootAssignment(
        int leaderProcessId,
        ClientInstance leader,
        ClientObservationSnapshot leaderSnapshot,
        IReadOnlyList<ClientInstance> controlledGroupMembers,
        bool createIfMissing)
    {
        var corpseObjectId = leaderSnapshot.Target.ObjectId;

        if (corpseObjectId == 0 ||
            !IsLootContextAvailable(leaderSnapshot))
        {
            this.activeLootAssignmentByLeaderProcessId.Remove(leaderProcessId);
            return null;
        }

        if (this.activeLootAssignmentByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var existing))
        {
            if (existing.CorpseObjectId == corpseObjectId &&
                controlledGroupMembers.Any(client =>
                    client.ProcessId == existing.LooterProcessId))
            {
                return existing;
            }

            this.activeLootAssignmentByLeaderProcessId.Remove(leaderProcessId);
        }

        if (!createIfMissing)
        {
            return null;
        }

        var looterProcessId = this.ResolveFleetLootNextLooterProcessId(
            leaderProcessId,
            leader,
            controlledGroupMembers);

        var assignment = new FleetLootCorpseAssignment(
            corpseObjectId,
            looterProcessId);

        this.activeLootAssignmentByLeaderProcessId[leaderProcessId] = assignment;
        return assignment;
    }

    private int ResolveFleetLootNextLooterProcessId(
        int leaderProcessId,
        ClientInstance leader,
        IReadOnlyList<ClientInstance> controlledGroupMembers)
    {
        var candidates = controlledGroupMembers.Count == 0
            ? new List<ClientInstance> { leader }
            : controlledGroupMembers.ToList();

        if (this.sessionLootOwnerByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var selectedProcessId) &&
            candidates.Any(client => client.ProcessId == selectedProcessId))
        {
            return selectedProcessId;
        }

        var fallbackProcessId = candidates
            .FirstOrDefault(client => client.ProcessId == leader.ProcessId)
            ?.ProcessId ??
            candidates[0].ProcessId;

        this.sessionLootOwnerByLeaderProcessId[leaderProcessId] = fallbackProcessId;
        return fallbackProcessId;
    }

    private void AdvanceFleetLootRoundRobinSelection(
        int leaderProcessId,
        IReadOnlyList<ClientInstance> controlledGroupMembers,
        int assignedLooterProcessId)
    {
        if (!this.roundRobinLootEnabledByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var enabled) ||
            !enabled ||
            controlledGroupMembers.Count == 0)
        {
            return;
        }

        var currentIndex = controlledGroupMembers
            .Select((client, index) => new { client.ProcessId, Index = index })
            .FirstOrDefault(item => item.ProcessId == assignedLooterProcessId)
            ?.Index ?? -1;

        var nextIndex = (currentIndex + 1) % controlledGroupMembers.Count;
        this.sessionLootOwnerByLeaderProcessId[leaderProcessId] =
            controlledGroupMembers[nextIndex].ProcessId;
    }

    private ClientObservationSnapshot ResolveFleetLootSourceSnapshot(
        FleetLootCorpseAssignment? assignment,
        ClientObservationSnapshot leaderSnapshot)
    {
        if (assignment == null ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                assignment.LooterProcessId,
                out var looterSnapshot) ||
            looterSnapshot.Target.ObjectId != assignment.CorpseObjectId)
        {
            return leaderSnapshot;
        }

        return looterSnapshot.Target.Corpse.LootItems.Count > 0 ||
               looterSnapshot.Target.Corpse.IsHydrated
            ? looterSnapshot
            : leaderSnapshot;
    }

    public async Task<FleetLootActionResult> EnsureFleetLootGameWindowOpenAsync(
        int leaderProcessId,
        Point? restoreCursorScreenPoint,
        CancellationToken cancellationToken)
    {
        FleetLootCorpseAssignment? assignment = null;
        var assignmentCreated = false;

        try
        {
            if (!this.TryResolveFleetLootContext(
                    leaderProcessId,
                    out var leader,
                    out var leaderSnapshot,
                    out var controlledGroupMembers,
                    out var error))
            {
                return FleetLootActionResult.Failure(error);
            }

            assignment = this.ResolveFleetLootAssignment(
                leaderProcessId,
                leader,
                leaderSnapshot,
                controlledGroupMembers,
                createIfMissing: false);

            if (assignment == null)
            {
                assignment = this.ResolveFleetLootAssignment(
                    leaderProcessId,
                    leader,
                    leaderSnapshot,
                    controlledGroupMembers,
                    createIfMissing: true);
                assignmentCreated = assignment != null;
            }

            if (assignment == null)
            {
                return FleetLootActionResult.Failure(
                    "No lootable corpse is targeted.");
            }

            if (!this.TryGetClient(assignment.LooterProcessId, out var looter))
            {
                this.ReleaseFleetLootAssignment(
                    leaderProcessId,
                    assignment,
                    assignmentCreated);

                return FleetLootActionResult.Failure(
                    "The assigned looter is no longer available.");
            }

            var targetWasAlreadyReady =
                this.clientObservationCoordinator.TryGetSnapshot(
                    looter.ProcessId,
                    out var targetSnapshot) &&
                targetSnapshot.Target.ObjectId == assignment.CorpseObjectId;

            var targetReady = await this.EnsureFleetLootTargetAsync(
                    leader,
                    leaderSnapshot,
                    looter,
                    assignment.CorpseObjectId,
                    cancellationToken)
                .ConfigureAwait(true);

            RestoreCursorPosition(restoreCursorScreenPoint);

            if (!targetReady)
            {
                this.ReleaseFleetLootAssignment(
                    leaderProcessId,
                    assignment,
                    assignmentCreated);

                return FleetLootActionResult.Failure(
                    "Could not target the corpse on the assigned looter.");
            }

            if (!targetWasAlreadyReady)
            {
                await Task.Delay(
                        fleetLootTargetVerbSettleDelay,
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            // Once N7CM has opened this corpse on a looter, our own assignment
            // is authoritative. The loot-panel observer is useful as positive
            // evidence for a panel the player opened manually, but its negative
            // states are transitional and must never toggle a known-open panel.
            if (assignment.IsGameLootWindowOpen)
            {
                return FleetLootActionResult.Success(
                    string.Concat(
                        "Loot window is open on ",
                        GetObservedCharacterName(looter) ?? BuildNeutralClientLabel(looter.ProcessId),
                        "."));
            }

            var openResult = await this.ExecuteNamedInputActionForClientAsync(
                    looter,
                    BuiltInInputActionProvider.InteractWithTargetName,
                    cancellationToken)
                .ConfigureAwait(true);

            RestoreCursorPosition(restoreCursorScreenPoint);

            if (!openResult.Succeeded)
            {
                this.ReleaseFleetLootAssignment(
                    leaderProcessId,
                    assignment,
                    assignmentCreated);

                return FleetLootActionResult.Failure(openResult.Message);
            }

            // The click above is the operation that opens the game panel. Do not
            // wait for the loot-panel observer to agree before continuing: that
            // observer can lag or briefly report a closed transitional state even
            // while the panel is visibly open. The caller pays the UI settle delay
            // before resolving and clicking the live item slot.
            assignment.IsGameLootWindowOpen = true;

            if (assignmentCreated)
            {
                this.AdvanceFleetLootRoundRobinSelection(
                    leaderProcessId,
                    controlledGroupMembers,
                    assignment.LooterProcessId);
            }

            return FleetLootActionResult.Success(
                string.Concat(
                    "Loot window opened on ",
                    GetObservedCharacterName(looter) ?? BuildNeutralClientLabel(looter.ProcessId),
                    "."));
        }
        catch
        {
            this.ReleaseFleetLootAssignment(
                leaderProcessId,
                assignment,
                assignmentCreated);
            RestoreCursorPosition(restoreCursorScreenPoint);
            throw;
        }
    }

    private void ReleaseFleetLootAssignment(
        int leaderProcessId,
        FleetLootCorpseAssignment? assignment,
        bool release)
    {
        if (!release ||
            assignment == null ||
            !this.activeLootAssignmentByLeaderProcessId.TryGetValue(
                leaderProcessId,
                out var current) ||
            current != assignment)
        {
            return;
        }

        this.activeLootAssignmentByLeaderProcessId.Remove(leaderProcessId);
    }

    public async Task<FleetLootActionResult> LootFleetLootItemAsync(
        int leaderProcessId,
        FleetLootItemRow requestedItem,
        Point? restoreCursorScreenPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var gameLootWindowWasAlreadyOpen =
                this.IsAssignedFleetLootGameWindowOpen(leaderProcessId);

            var openResult = await this.EnsureFleetLootGameWindowOpenAsync(
                    leaderProcessId,
                    restoreCursorScreenPoint,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!openResult.Succeeded)
            {
                return openResult;
            }

            if (!this.activeLootAssignmentByLeaderProcessId.TryGetValue(
                    leaderProcessId,
                    out var assignment) ||
                !this.TryGetClient(assignment.LooterProcessId, out var looter))
            {
                return FleetLootActionResult.Failure(
                    "The assigned looter is no longer available.");
            }

            if (!gameLootWindowWasAlreadyOpen)
            {
                await Task.Delay(
                        fleetLootWindowInputSettleDelay,
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            var itemReady = await this.WaitForFleetLootItemReadyAsync(
                    looter.ProcessId,
                    assignment.CorpseObjectId,
                    requestedItem,
                    cancellationToken)
                .ConfigureAwait(true);

            if (!itemReady ||
                !this.clientObservationCoordinator.TryGetSnapshot(
                    looter.ProcessId,
                    out var looterSnapshot) ||
                looterSnapshot.Target.ObjectId != assignment.CorpseObjectId)
            {
                return FleetLootActionResult.Failure(
                    "The assigned looter is not ready to loot this corpse.");
            }

            var preClickLootCount = looterSnapshot.Target.Corpse.LootItems.Count;
            var liveVisualSlot = this.FindLiveFleetLootVisualSlot(
                looterSnapshot,
                requestedItem);

            if (liveVisualSlot == null)
            {
                return FleetLootActionResult.Failure(
                    string.Concat("Could not find ", requestedItem.Name, " in the live loot window."));
            }

            var actionName = GetLootWindowSlotInputActionName(liveVisualSlot.Value + 1);
            var clickResult = await this.ExecuteNamedInputActionForClientAsync(
                    looter,
                    actionName,
                    cancellationToken)
                .ConfigureAwait(true);

            RestoreCursorPosition(restoreCursorScreenPoint);

            if (!clickResult.Succeeded)
            {
                return FleetLootActionResult.Failure(clickResult.Message);
            }

            var itemRemoved = await this.WaitForFleetLootItemRemovalAndTractorIdleAsync(
                    looter.ProcessId,
                    assignment.CorpseObjectId,
                    requestedItem,
                    preClickLootCount,
                    cancellationToken)
                .ConfigureAwait(true);

            // A newly opened ENB loot panel can become visible before its item
            // rows accept input. The previous user-visible behavior proved that
            // a later click on the same live slot succeeds. Retry only the item
            // slot once; never press Interact again, because that would toggle
            // the game loot panel closed.
            if (!itemRemoved &&
                !gameLootWindowWasAlreadyOpen &&
                this.clientObservationCoordinator.TryGetSnapshot(
                    looter.ProcessId,
                    out var retrySnapshot) &&
                retrySnapshot.Target.ObjectId == assignment.CorpseObjectId)
            {
                var retryVisualSlot = this.FindLiveFleetLootVisualSlot(
                    retrySnapshot,
                    requestedItem);

                if (retryVisualSlot != null)
                {
                    var retryActionName = GetLootWindowSlotInputActionName(
                        retryVisualSlot.Value + 1);
                    var retryClickResult = await this.ExecuteNamedInputActionForClientAsync(
                            looter,
                            retryActionName,
                            cancellationToken)
                        .ConfigureAwait(true);

                    RestoreCursorPosition(restoreCursorScreenPoint);

                    if (!retryClickResult.Succeeded)
                    {
                        return FleetLootActionResult.Failure(retryClickResult.Message);
                    }

                    itemRemoved = await this.WaitForFleetLootItemRemovalAndTractorIdleAsync(
                            looter.ProcessId,
                            assignment.CorpseObjectId,
                            requestedItem,
                            preClickLootCount,
                            cancellationToken)
                        .ConfigureAwait(true);
                }
            }

            if (!itemRemoved)
            {
                return FleetLootActionResult.Failure(
                    string.Concat(
                        "Looting did not start for ",
                        requestedItem.Name,
                        "."));
            }

            // The snapshot immediately after tractor completion can still
            // contain the just-removed final item for one publication cycle.
            // If there was exactly one live item before the click and removal
            // was confirmed, this was deterministically the final item.
            var closeWindow = preClickLootCount == 1 ||
                              !this.clientObservationCoordinator.TryGetSnapshot(
                                  looter.ProcessId,
                                  out var afterSnapshot) ||
                              afterSnapshot.Target.ObjectId != assignment.CorpseObjectId ||
                              !afterSnapshot.Target.Corpse.LootItems.Any();

            if (closeWindow)
            {
                // ENB closes the game loot panel when the final item leaves the
                // corpse. Mark our owned state closed and remove the assignment;
                // do not click the close coordinate onto an already-gone panel.
                assignment.IsGameLootWindowOpen = false;
                this.activeLootAssignmentByLeaderProcessId.Remove(leaderProcessId);
            }

            return FleetLootActionResult.Success(
                string.Concat("Looted ", requestedItem.Name, "."),
                closeWindow);
        }
        catch
        {
            RestoreCursorPosition(restoreCursorScreenPoint);
            throw;
        }
    }

    public async Task CloseFleetLootGameWindowAsync(
        int leaderProcessId,
        Point? restoreCursorScreenPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!this.activeLootAssignmentByLeaderProcessId.TryGetValue(
                    leaderProcessId,
                    out var assignment) ||
                !assignment.IsGameLootWindowOpen ||
                !this.TryGetClient(assignment.LooterProcessId, out var looter))
            {
                return;
            }

            _ = await this.ExecuteNamedInputActionForClientAsync(
                    looter,
                    BuiltInInputActionProvider.CloseLootWindowName,
                    cancellationToken)
                .ConfigureAwait(true);

            assignment.IsGameLootWindowOpen = false;
            RestoreCursorPosition(restoreCursorScreenPoint);
        }
        catch
        {
            RestoreCursorPosition(restoreCursorScreenPoint);
            throw;
        }
    }

    private async Task<bool> EnsureFleetLootTargetAsync(
        ClientInstance leader,
        ClientObservationSnapshot leaderSnapshot,
        ClientInstance looter,
        uint corpseObjectId,
        CancellationToken cancellationToken)
    {
        if (this.clientObservationCoordinator.TryGetSnapshot(
                looter.ProcessId,
                out var looterSnapshot) &&
            looterSnapshot.Target.ObjectId == corpseObjectId)
        {
            return true;
        }

        if (looter.ProcessId == leader.ProcessId)
        {
            return false;
        }

        var leaderIdentity = ClientLiveCharacterIdentityResolver.Resolve(leaderSnapshot);
        var leaderName = GetObservedCharacterName(leader);

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                looter.ProcessId,
                out looterSnapshot))
        {
            return false;
        }

        var leaderMember = looterSnapshot.Group.Members.FirstOrDefault(member =>
            member.IsPresent &&
            (
                (leaderIdentity.CharacterObjectId is not null and not 0 and not uint.MaxValue &&
                 member.ObjectId == leaderIdentity.CharacterObjectId.Value) ||
                (!string.IsNullOrWhiteSpace(leaderName) &&
                 string.Equals(member.Name, leaderName, StringComparison.OrdinalIgnoreCase))
            ));

        if (leaderMember == null ||
            leaderMember.Slot is < 1 or > 5)
        {
            looter.AutomationStatus = "Could not find loot opener in this client's group list.";
            return false;
        }

        var targetResult = await this.ExecuteNamedInputActionForClientAsync(
                looter,
                GetTargetOfGroupMemberInputActionName(leaderMember.Slot),
                cancellationToken)
            .ConfigureAwait(true);

        if (!targetResult.Succeeded)
        {
            looter.AutomationStatus = targetResult.Message;
            return false;
        }

        return await this.WaitForClientTargetAsync(
                looter.ProcessId,
                corpseObjectId,
                fleetLootTargetVerifyTimeout,
                cancellationToken)
            .ConfigureAwait(true);
    }

    private async Task<bool> WaitForClientTargetAsync(
        int processId,
        uint objectId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot) &&
                snapshot.Target.ObjectId == objectId)
            {
                return true;
            }

            await Task.Delay(
                    fleetLootTargetVerifyPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return false;
    }

    private bool IsAssignedFleetLootGameWindowOpen(
        int leaderProcessId)
    {
        return this.activeLootAssignmentByLeaderProcessId.TryGetValue(
                   leaderProcessId,
                   out var assignment) &&
               assignment.IsGameLootWindowOpen;
    }

    private async Task<bool> WaitForFleetLootItemReadyAsync(
        int processId,
        uint corpseObjectId,
        FleetLootItemRow requestedItem,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + fleetLootWindowHydrationTimeout;

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot) &&
                snapshot.Target.ObjectId == corpseObjectId &&
                this.FindLiveFleetLootVisualSlot(
                    snapshot,
                    requestedItem) != null)
            {
                return true;
            }

            await Task.Delay(
                    fleetLootWindowHydrationPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return false;
    }

    private async Task WaitForFleetLootTractorAsync(
        int processId,
        uint corpseObjectId,
        FleetLootItemRow requestedItem,
        CancellationToken cancellationToken)
    {
        var startDeadline = DateTimeOffset.UtcNow + fleetLootTractorStartTimeout;
        var tractorStarted = false;

        while (DateTimeOffset.UtcNow <= startDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot) ||
                snapshot.Target.ObjectId != corpseObjectId ||
                this.FindLiveFleetLootVisualSlot(snapshot, requestedItem) == null)
            {
                break;
            }

            if (snapshot.LootTractor.IsTractoring)
            {
                tractorStarted = true;
                break;
            }

            await Task.Delay(
                    fleetLootTractorPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        if (!tractorStarted)
        {
            return;
        }

        var completeDeadline = DateTimeOffset.UtcNow + fleetLootTractorCompleteTimeout;

        while (DateTimeOffset.UtcNow <= completeDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot) ||
                !snapshot.LootTractor.IsTractoring)
            {
                return;
            }

            await Task.Delay(
                    fleetLootTractorPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private async Task<bool> WaitForFleetLootItemRemovalAndTractorIdleAsync(
        int processId,
        uint corpseObjectId,
        FleetLootItemRow requestedItem,
        int preClickLootCount,
        CancellationToken cancellationToken)
    {
        var tractorStartDeadline = DateTimeOffset.UtcNow + fleetLootTractorStartTimeout;
        var itemRemoved = false;
        var tractorStarted = false;

        while (DateTimeOffset.UtcNow <= tractorStartDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot))
            {
                return itemRemoved;
            }

            itemRemoved =
                itemRemoved ||
                snapshot.Target.ObjectId != corpseObjectId ||
                snapshot.Target.Corpse.LootItems.Count < preClickLootCount ||
                this.FindLiveFleetLootVisualSlot(snapshot, requestedItem) == null;

            if (snapshot.LootTractor.IsTractoring)
            {
                // ENB closes its loot panel as soon as the final tractor starts.
                // Mirror that behavior and let our window close immediately too.
                if (preClickLootCount == 1)
                {
                    return true;
                }

                tractorStarted = true;
                break;
            }

            await Task.Delay(
                    fleetLootTractorPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        if (!tractorStarted)
        {
            return itemRemoved;
        }

        var completeDeadline = DateTimeOffset.UtcNow + fleetLootTractorCompleteTimeout;

        while (DateTimeOffset.UtcNow <= completeDeadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    processId,
                    out var snapshot))
            {
                return itemRemoved;
            }

            itemRemoved =
                itemRemoved ||
                snapshot.Target.ObjectId != corpseObjectId ||
                snapshot.Target.Corpse.LootItems.Count < preClickLootCount ||
                this.FindLiveFleetLootVisualSlot(snapshot, requestedItem) == null;

            if (!snapshot.LootTractor.IsTractoring)
            {
                return itemRemoved;
            }

            await Task.Delay(
                    fleetLootTractorPollInterval,
                    cancellationToken)
                .ConfigureAwait(true);
        }

        return itemRemoved;
    }

    private int? FindLiveFleetLootVisualSlot(
        ClientObservationSnapshot snapshot,
        FleetLootItemRow requestedItem)
    {
        var liveItems = snapshot.Target.Corpse.LootItems
            .OrderBy(slot => slot.Slot)
            .ToList();

        for (var index = 0; index < liveItems.Count; index++)
        {
            if (FleetLootSlotMatches(liveItems[index], requestedItem))
            {
                return index;
            }
        }

        if (!requestedItem.ItemTemplateId.HasValue)
        {
            return null;
        }

        for (var index = 0; index < liveItems.Count; index++)
        {
            var slot = liveItems[index];

            if (slot.Slot == requestedItem.CargoSlot &&
                slot.ItemTemplateId == requestedItem.ItemTemplateId)
            {
                return index;
            }
        }

        return null;
    }

    private static bool FleetLootSlotMatches(
        ClientCorpseCargoSlotObservation slot,
        FleetLootItemRow requestedItem)
    {
        if (requestedItem.ItemTemplateId.HasValue &&
            slot.ItemTemplateId != requestedItem.ItemTemplateId)
        {
            return false;
        }

        if (requestedItem.StackCount.HasValue &&
            slot.StackCount != requestedItem.StackCount)
        {
            return false;
        }

        if (requestedItem.QualityPercent.HasValue)
        {
            var quality = slot.QualityPercent;

            if (!quality.HasValue ||
                Math.Abs(quality.Value - requestedItem.QualityPercent.Value) > 0.1f)
            {
                return false;
            }
        }

        return requestedItem.ItemTemplateId.HasValue ||
               slot.Slot == requestedItem.CargoSlot;
    }

    private static void RestoreCursorPosition(Point? screenPoint)
    {
        if (screenPoint.HasValue)
        {
            _ = NativeMethods.MoveCursorToScreenPoint(screenPoint.Value);
        }
    }

    private static string GetLootWindowSlotInputActionName(
        int slotNumber)
    {
        return slotNumber switch
        {
            1 => BuiltInInputActionProvider.LootWindowSlot1Name,
            2 => BuiltInInputActionProvider.LootWindowSlot2Name,
            3 => BuiltInInputActionProvider.LootWindowSlot3Name,
            4 => BuiltInInputActionProvider.LootWindowSlot4Name,
            5 => BuiltInInputActionProvider.LootWindowSlot5Name,
            6 => BuiltInInputActionProvider.LootWindowSlot6Name,
            7 => BuiltInInputActionProvider.LootWindowSlot7Name,
            8 => BuiltInInputActionProvider.LootWindowSlot8Name,
            _ => throw new ArgumentOutOfRangeException(
                nameof(slotNumber),
                slotNumber,
                "Loot window slot must be 1..8."),
        };
    }

    private FleetLootLooterOption CreateLooterOption(
        ClientInstance client,
        int selectedProcessId)
    {
        ClientObservationSnapshot? snapshot = null;
        _ = this.clientObservationCoordinator.TryGetSnapshot(
            client.ProcessId,
            out snapshot);

        var inventory = snapshot?.LocalPlayer.Inventory;

        return new FleetLootLooterOption(
            client.ProcessId,
            GetObservedCharacterName(client) ?? BuildNeutralClientLabel(client.ProcessId),
            inventory?.CargoUsedSlotCount ?? 0,
            inventory?.CargoCapacity,
            client.ProcessId == selectedProcessId);
    }

    private NavigationWormholeAvailability
        BuildNavigationWormholeAvailability(
            ClientObservationSnapshot sourceSnapshot)
    {
        List<NavigationWormholeCasterAvailability> casters = [];

        foreach (var client in this.GetControlledGroupClients(sourceSnapshot))
        {
            if (!this.clientObservationCoordinator.TryGetSnapshot(
                    client.ProcessId,
                    out var snapshot) ||
                snapshot.LifecycleState != ClientLifecycleState.InGame)
            {
                continue;
            }

            var pilotName = GetObservedCharacterName(client) ??
                ClientLiveCharacterIdentityResolver.Resolve(snapshot).Name;

            if (string.IsNullOrWhiteSpace(pilotName))
            {
                continue;
            }

            var canInspectShortcuts =
                snapshot.LoadingOrTransitionFlag == 0 &&
                snapshot.World is
                {
                    IsAvailable: true,
                    Environment: ClientWorldEnvironment.Space,
                };
            var shortcuts = canInspectShortcuts &&
                            this.clientObservationCoordinator.TryReadShortcutState(
                                client.ProcessId,
                                out var directShortcuts,
                                out _)
                ? directShortcuts
                : snapshot.Shortcuts;

            IReadOnlyList<GameShortcutPaletteEntry> shortcutEntries =
                canInspectShortcuts
                    ? this.gameShortcutPaletteService
                        .BuildEntries(client, snapshot, shortcuts)
                    : [];

            foreach (var familyName in new[]
                     {
                         NavigationWormholeCatalog.CreateWormholeFamilyName,
                         NavigationWormholeCatalog.ExtendedWormholeFamilyName,
                     })
            {
                var skill = snapshot.LocalPlayer.CharacterProgression
                    .Skills.Skills
                    .FirstOrDefault(candidate =>
                        NavigationWormholeCatalog.FamilyMatches(
                            familyName,
                            candidate.Name));

                if (skill?.CurrentRank is not > 0)
                {
                    continue;
                }

                var hasShortcut = shortcutEntries.Any(entry =>
                    entry.Kind == GameShortcutKind.Skill &&
                    (NavigationWormholeCatalog.FamilyMatches(
                         familyName,
                         entry.FamilyName) ||
                     NavigationWormholeCatalog.FamilyMatches(
                         familyName,
                         entry.SkillDetails?.SkillFamilyName)));

                casters.Add(new NavigationWormholeCasterAvailability
                {
                    ProcessId = client.ProcessId,
                    PilotName = pilotName,
                    SkillFamilyName = familyName,
                    SkillRank = skill.CurrentRank,
                    CanInspectShortcuts = canInspectShortcuts,
                    HasShortcut = hasShortcut,
                });
            }
        }

        return new NavigationWormholeAvailability(casters);
    }

    private IReadOnlyList<ClientInstance> GetControlledGroupClients(
        ClientObservationSnapshot leaderSnapshot)
    {
        var groupNames = leaderSnapshot.Group.Members
            .Where(member => member.IsPresent)
            .Select(member => member.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leaderName = this.clients.TryGetValue(
                leaderSnapshot.ProcessId,
                out var leader)
            ? GetObservedCharacterName(leader)
            : null;

        if (!string.IsNullOrWhiteSpace(leaderName))
        {
            groupNames.Add(leaderName);
        }

        if (!leaderSnapshot.Group.IsAvailable ||
            !leaderSnapshot.Group.IsInGroup ||
            groupNames.Count == 0)
        {
            return this.clients.Values
                .Where(client => client.ProcessId == leaderSnapshot.ProcessId &&
                                 client.LifecycleState == ClientLifecycleState.InGame)
                .ToList();
        }

        return this.clients.Values
            .Where(client => client.LifecycleState == ClientLifecycleState.InGame)
            .Where(client =>
            {
                var name = GetObservedCharacterName(client);
                return !string.IsNullOrWhiteSpace(name) &&
                       groupNames.Contains(name);
            })
            .OrderBy(client => GetGroupOrder(
                client,
                leaderSnapshot,
                leaderName))
            .ThenBy(client => GetObservedCharacterName(client), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int GetGroupOrder(
        ClientInstance client,
        ClientObservationSnapshot leaderSnapshot,
        string? leaderName)
    {
        var name = GetObservedCharacterName(client);

        if (!string.IsNullOrWhiteSpace(name) &&
            string.Equals(name, leaderName, StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        var member = leaderSnapshot.Group.Members.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        return member?.Slot ?? int.MaxValue;
    }

    private IReadOnlyList<GroupSkillsTargetRow> BuildLeaderPerspectiveTargets(
        ClientInstance leader,
        ClientObservationSnapshot leaderSnapshot,
        IReadOnlyList<ClientInstance> controlledGroupMembers)
    {
        var rows = new List<GroupSkillsTargetRow>();

        if (leaderSnapshot.Target.HasTarget)
        {
            rows.Add(
                new GroupSkillsTargetRow(
                    ProcessId: null,
                    GroupSlot: -1,
                    leaderSnapshot.Target.Name,
                    BuildTargetLevelDetail(leaderSnapshot.Target),
                    GetShieldPercent(leaderSnapshot.Target.Shield),
                    GetHullPercent(leaderSnapshot.Target.Hull),
                    GetShieldCurrent(leaderSnapshot.Target.Shield),
                    GetShieldMaximum(leaderSnapshot.Target.Shield),
                    GetHullCurrent(leaderSnapshot.Target.Hull),
                    GetHullMaximum(leaderSnapshot.Target.Hull),
                    ReactorCurrent: null,
                    ReactorMaximum: null,
                    ReactorPercent: null,
                    IsLeader: false,
                    IsLeaderTarget: true,
                    HasTarget: true,
                    Kind: leaderSnapshot.Target.Kind,
                    Relation: leaderSnapshot.Target.Relation,
                    ObjectId: leaderSnapshot.Target.ObjectId,
                    SurfaceDistance: leaderSnapshot.Target.Distance.IsAvailable
                        ? leaderSnapshot.Target.Distance.SurfaceDistance
                        : null,
                    OverallLevel: null,
                    CombatLevel: leaderSnapshot.Target.Operational.Identity.CombatLevel,
                    ExploreLevel: null,
                    TradeLevel: null));
        }
        else
        {
            rows.Add(
                new GroupSkillsTargetRow(
                    ProcessId: null,
                    GroupSlot: -1,
                    "",
                    "",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    ReactorCurrent: null,
                    ReactorMaximum: null,
                    ReactorPercent: null,
                    IsLeader: false,
                    IsLeaderTarget: true,
                    HasTarget: false,
                    Kind: ClientTargetKind.Unknown,
                    Relation: ClientTargetRelation.Unknown,
                    ObjectId: 0,
                    SurfaceDistance: null,
                    OverallLevel: null,
                    CombatLevel: null,
                    ExploreLevel: null,
                    TradeLevel: null));
        }

        foreach (var client in controlledGroupMembers)
        {
            var name = GetObservedCharacterName(client) ?? BuildNeutralClientLabel(client.ProcessId);
            var isLeader = client.ProcessId == leader.ProcessId;
            ClientObservationSnapshot? snapshot = null;
            _ = this.clientObservationCoordinator.TryGetSnapshot(
                client.ProcessId,
                out snapshot);
            var progression = snapshot?.LocalPlayer.CharacterProgression;
            var leaderObservedMember = leaderSnapshot.Group.Members.FirstOrDefault(member =>
                string.Equals(
                    member.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            var objectId = snapshot?.LocalPlayerObjectId ??
                           leaderObservedMember?.ObjectId ??
                           0;
            var surfaceDistance = isLeader
                ? 0.0f
                : leaderObservedMember?.Distance.IsAvailable == true
                    ? leaderObservedMember.Distance.SurfaceDistance
                    : default(float?);

            rows.Add(
                new GroupSkillsTargetRow(
                    client.ProcessId,
                    GetGroupOrder(client, leaderSnapshot, GetObservedCharacterName(leader)),
                    name,
                    BuildGroupMemberLevelDetail(snapshot),
                    snapshot != null
                        ? GetShieldPercent(snapshot.LocalPlayer.Shield)
                        : null,
                    snapshot != null
                        ? GetHullPercent(snapshot.LocalPlayer.Hull)
                        : null,
                    snapshot != null
                        ? GetShieldCurrent(snapshot.LocalPlayer.Shield)
                        : null,
                    snapshot != null
                        ? GetShieldMaximum(snapshot.LocalPlayer.Shield)
                        : null,
                    snapshot != null
                        ? GetHullCurrent(snapshot.LocalPlayer.Hull)
                        : null,
                    snapshot != null
                        ? GetHullMaximum(snapshot.LocalPlayer.Hull)
                        : null,
                    snapshot != null
                        ? GetReactorCurrent(snapshot.LocalPlayer.Energy)
                        : null,
                    snapshot != null
                        ? GetReactorMaximum(snapshot.LocalPlayer.Energy)
                        : null,
                    snapshot != null
                        ? GetReactorPercent(snapshot.LocalPlayer.Energy)
                        : null,
                    isLeader,
                    IsLeaderTarget: false,
                    HasTarget: true,
                    Kind: ClientTargetKind.Player,
                    Relation: ClientTargetRelation.GroupMember,
                    ObjectId: objectId,
                    SurfaceDistance: surfaceDistance,
                    OverallLevel: progression?.OverallLevel,
                    CombatLevel: progression?.CombatLevel,
                    ExploreLevel: progression?.ExploreLevel,
                    TradeLevel: progression?.TradeLevel));
        }

        return rows;
    }

    private static int? GetShieldPercent(
        ClientTargetShieldObservation shield)
    {
        return shield.HasShieldPercent
            ? Math.Clamp(shield.ShieldPercent, 0, 100)
            : null;
    }

    private static int? GetHullPercent(
        ClientTargetHullObservation hull)
    {
        return hull.HasCompleteHullData
            ? Math.Clamp((int)Math.Round(hull.HullPercent), 0, 100)
            : null;
    }

    private static int? GetShieldCurrent(
        ClientTargetShieldObservation shield)
    {
        return shield.HasCurrentShieldPower
            ? Math.Max(0, (int)Math.Round(shield.CurrentShieldPower))
            : null;
    }

    private static int? GetShieldMaximum(
        ClientTargetShieldObservation shield)
    {
        return shield.HasMaximumShieldPower
            ? Math.Max(0, shield.MaximumShieldPower)
            : null;
    }

    private static int? GetHullCurrent(
        ClientTargetHullObservation hull)
    {
        return hull.HasHullPoints
            ? Math.Max(0, hull.HullPoints)
            : null;
    }

    private static int? GetHullMaximum(
        ClientTargetHullObservation hull)
    {
        return hull.HasMaximumHullPoints
            ? Math.Max(0, hull.MaximumHullPoints)
            : null;
    }

    private static int? GetReactorCurrent(
        ClientTargetEnergyObservation energy)
    {
        if (!energy.IsAvailable ||
            !energy.HasCompleteEnergyData)
        {
            return null;
        }

        var maximum = Math.Max(0, energy.MaximumEnergyPower);
        return Math.Clamp(
            (int)MathF.Round(energy.DerivedCurrentEnergyPower),
            0,
            maximum);
    }

    private static int? GetReactorMaximum(
        ClientTargetEnergyObservation energy)
    {
        return energy.IsAvailable &&
               energy.HasMaximumEnergyPower
            ? Math.Max(0, energy.MaximumEnergyPower)
            : null;
    }

    private static int? GetReactorPercent(
        ClientTargetEnergyObservation energy)
    {
        return energy.IsAvailable &&
               energy.HasEnergyPercent
            ? Math.Clamp(energy.EnergyPercent, 0, 100)
            : null;
    }

    private static string BuildTargetLevelDetail(
        ClientTargetObservation target)
    {
        var combatLevel = target.Operational.Identity.CombatLevel;
        return combatLevel.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {combatLevel.Value}")
            : "";
    }

    private static string BuildGroupMemberLevelDetail(
        ClientObservationSnapshot? snapshot)
    {
        var progression = snapshot?.LocalPlayer.CharacterProgression;

        if (progression == null)
        {
            return "";
        }

        var overall = progression.OverallLevel;
        var combat = progression.CombatLevel;
        var explore = progression.ExploreLevel;
        var trade = progression.TradeLevel;

        if (overall.HasValue &&
            combat.HasValue &&
            explore.HasValue &&
            trade.HasValue)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Level {overall.Value} [{combat.Value}  {explore.Value}  {trade.Value}]");
        }

        return overall.HasValue
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Level {overall.Value}")
            : "";
    }

    private bool TryGetClient(
        int processId,
        out ClientInstance client)
    {
        lock (this.lockObject)
        {
            var result = this.clients.TryGetValue(
                processId,
                out var found);

            client = found!;
            return result;
        }
    }

    private string GetFleetCommandPilotName(
        ClientInstance client)
    {
        return GetObservedCharacterName(client) ?? "";
    }

    public void CancelFleetCommand()
    {
        this.fleetCommandService.Cancel();
    }

    private IReadOnlyList<FleetCommandDefinition> BuildShortcutFleetCommandDefinitions(
        FleetCommandInvocationContext invocationContext)
    {
        var client = invocationContext.ActiveClient;

        if (client.LifecycleState != ClientLifecycleState.InGame)
        {
            return [];
        }

        if (!this.clientObservationCoordinator.TryGetSnapshot(
                client.ProcessId,
                out var snapshot))
        {
            return [];
        }

        var shortcuts = this.clientObservationCoordinator.TryReadShortcutState(
            client.ProcessId,
            out var directShortcuts,
            out _)
            ? directShortcuts
            : snapshot.Shortcuts;

        return this.gameShortcutPaletteService.BuildCommands(
            client,
            snapshot,
            shortcuts);
    }

    private IReadOnlyList<FleetCommandDefinition> BuildDynamicFleetCommandDefinitions(
        FleetCommandInvocationContext invocationContext)
    {
        var commands = new List<FleetCommandDefinition>();

        var hasActiveSnapshot = this.clientObservationCoordinator.TryGetSnapshot(
            invocationContext.ActiveClient.ProcessId,
            out var activeSnapshot);

        if (hasActiveSnapshot)
        {
            if (activeSnapshot.Group.IsAvailable &&
                activeSnapshot.Group.IsValid &&
                activeSnapshot.Group.IsInGroup)
            {
                commands.Add(CreateFormationModeCommand(
                    this.settings.FleetCommands.FormationMode));

                if (this.TryResolveFleetFormationContext(
                        invocationContext.ActiveClient.ProcessId,
                        out var formationContext,
                        out _) &&
                    formationContext.EligibleFollowers.Count > 0)
                {
                    commands.Add(CreateFormationToggleCommand(
                        formationContext.IsFullyFormed));
                }
            }

            if (this.HasControlledGroupCompanions(activeSnapshot))
            {
                commands.Add(CreateAssistMeCommand());
                commands.Add(CreateComeToMeCommand());
            }

            if (activeSnapshot.LifecycleState == ClientLifecycleState.InGame)
            {
                commands.Add(new FleetCommandDefinition
                {
                    Id = "ui:group-skills",
                    Label = "Action HUD",
                    ShowInOverlay = true,
                    Category = FleetCommandCategory.Combat,
                    Arguments =
                    {
                        [UiCommandArgument] = GroupSkillsUiCommand,
                    },
                });
            }

        }

        var targets = this.Clients
            .Where(client =>
                client.ProcessId != invocationContext.ActiveClient.ProcessId &&
                client.GameWindowHandle != IntPtr.Zero &&
                client.LifecycleState == ClientLifecycleState.InGame)
            .Select(client => new
            {
                Client = client,
                ObservedName = GetObservedCharacterName(client),
            })
            .Where(target =>
                !string.IsNullOrWhiteSpace(target.ObservedName))
            .OrderBy(
                target => target.ObservedName,
                StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var targetClient = target.Client;
            var targetName = target.ObservedName!;
            var targetIsGrouped = hasActiveSnapshot &&
                                  activeSnapshot.Group.IsAvailable &&
                                  activeSnapshot.Group.IsInGroup &&
                                  activeSnapshot.Group.Members.Any(member =>
                                      member.IsPresent &&
                                      string.Equals(
                                          member.Name,
                                          targetName,
                                          StringComparison.OrdinalIgnoreCase));

            if (!targetIsGrouped)
            {
                commands.Add(new FleetCommandDefinition
                {
                Id = string.Create(
                    CultureInfo.InvariantCulture,
                    $"invite-client:{targetClient.ProcessId}"),
                Label = string.Concat("Invite ", targetName),
                ShowInOverlay = true,
                Category = FleetCommandCategory.Group,
                Arguments =
                {
                    ["targetProcessId"] = targetClient.ProcessId.ToString(
                        CultureInfo.InvariantCulture),
                    ["target"] = targetName,
                },
                Blocks =
                [
                    FleetCommandBlock.For(
                        FleetCommandScope.Pilot,
                        FleetCommandStep.ChatCommand("/invite {target}")),
                    FleetCommandBlock.For(
                        FleetCommandScope.Target,
                        FleetCommandStep.SetTitle("Accepting invite from {pilot}", durationMilliseconds: 5000),
                        FleetCommandStep.Delay(650),
                        FleetCommandStep.Action(
                            BuiltInInputActionProvider.AcceptInviteName),
                        FleetCommandStep.Delay(150),
                        FleetCommandStep.Action(
                            BuiltInInputActionProvider.ConfirmDialogName)),
                    FleetCommandBlock.For(
                        FleetCommandScope.System,
                        FleetCommandStep.RestorePilotFocus()),
                    ],
                });
            }

            if (targetIsGrouped)
            {
                commands.Add(new FleetCommandDefinition
                {
                Id = string.Create(
                    CultureInfo.InvariantCulture,
                    $"kick-client:{targetClient.ProcessId}"),
                Label = string.Concat("Kick ", targetName),
                ShowInOverlay = true,
                Category = FleetCommandCategory.Group,
                Arguments =
                {
                    ["targetProcessId"] = targetClient.ProcessId.ToString(CultureInfo.InvariantCulture),
                    ["target"] = targetName,
                },
                Blocks =
                [
                    FleetCommandBlock.For(
                        FleetCommandScope.Target,
                        FleetCommandStep.SetTitle("Leaving group", durationMilliseconds: 5000),
                        FleetCommandStep.Action(BuiltInInputActionProvider.LeaveGroupName),
                        FleetCommandStep.Delay(650),
                        FleetCommandStep.Action(BuiltInInputActionProvider.ConfirmDialogName)),
                    FleetCommandBlock.For(
                        FleetCommandScope.System,
                        FleetCommandStep.RestorePilotFocus()),
                    ],
                });
            }
        }

        return commands;
    }

    private static FleetCommandDefinition CreateFormationModeCommand(
        FleetFormationMode mode)
    {
        return new FleetCommandDefinition
        {
            Id = BuiltInFleetCommandProvider.FormationModeCommandId,
            Label = string.Concat(
                "Mode: ",
                GetFleetFormationModeDisplayName(mode)),
            ShowInOverlay = true,
            Category = FleetCommandCategory.Formation,
        };
    }

    private static FleetCommandDefinition CreateFormationToggleCommand(
        bool isFullyFormed)
    {
        return new FleetCommandDefinition
        {
            Id = BuiltInFleetCommandProvider.FormationToggleCommandId,
            Label = isFullyFormed
                ? "Break Formation"
                : "Form Up",
            ShowInOverlay = true,
            Category = FleetCommandCategory.Formation,
        };
    }

    private static FleetCommandDefinition CreateAssistMeCommand()
    {
        return new FleetCommandDefinition
        {
            Id = BuiltInFleetCommandProvider.AssistMeCommandId,
            Label = "Assist Me",
            ShowInOverlay = true,
            Category = FleetCommandCategory.Combat,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Followers,
                    FleetCommandStep.SetTitle("Assisting {pilot}", durationMilliseconds: 5000),
                    FleetCommandStep.TargetInvokingPilotTarget(),
                    FleetCommandStep.Delay(100),
                    FleetCommandStep.Action(BuiltInInputActionProvider.FireAllName),
                    FleetCommandStep.Delay(100)),
                FleetCommandBlock.For(
                    FleetCommandScope.System,
                    FleetCommandStep.RestorePilotFocus()),
            ],
        };
    }

    private static FleetCommandDefinition CreateComeToMeCommand()
    {
        return new FleetCommandDefinition
        {
            Id = BuiltInFleetCommandProvider.ComeToMeCommandId,
            Label = "Come To Me",
            ShowInOverlay = true,
            Category = FleetCommandCategory.Move,
            Blocks =
            [
                FleetCommandBlock.For(
                    FleetCommandScope.Followers,
                    FleetCommandStep.SetTitle("Coming to {pilot}", durationMilliseconds: 5000),
                    FleetCommandStep.TargetInvokingPilot(),
                    FleetCommandStep.Delay(100),
                    FleetCommandStep.Action(BuiltInInputActionProvider.WarpName),
                    FleetCommandStep.Delay(100)),
                FleetCommandBlock.For(
                    FleetCommandScope.System,
                    FleetCommandStep.RestorePilotFocus()),
            ],
        };
    }

    private bool HasControlledGroupCompanions(
        ClientObservationSnapshot activeSnapshot)
    {
        if (!activeSnapshot.Group.IsAvailable ||
            !activeSnapshot.Group.IsInGroup)
        {
            return false;
        }

        var controlledMembers = this.GetControlledGroupClients(activeSnapshot);

        return controlledMembers.Any(client =>
            client.ProcessId != activeSnapshot.ProcessId);
    }

    private static bool IsActionHudLootShortcutAvailable(
        ClientObservationSnapshot activeSnapshot)
    {
        // Keep the Action HUD shortcut on the same proven readiness
        // predicate as the command palette's Loot action.
        return IsLootContextAvailable(activeSnapshot);
    }

    private static bool IsLootContextAvailable(
        ClientObservationSnapshot activeSnapshot)
    {
        if (activeSnapshot.Target.Kind == ClientTargetKind.Corpse ||
            activeSnapshot.Target.Corpse.HasLoot)
        {
            return true;
        }

        return activeSnapshot.TargetInteraction.FindAction(
            ClientTargetVerb.Tractor) != null;
    }

    private void CloseGroupSkillWindowsForProcess(int processId)
    {
        if (!this.groupSkillsForms.Remove(processId, out var groupSkillsForm) ||
            groupSkillsForm.IsDisposed)
        {
            return;
        }

        if (groupSkillsForm.InvokeRequired)
        {
            _ = groupSkillsForm.BeginInvoke(new MethodInvoker(groupSkillsForm.Close));
            return;
        }

        groupSkillsForm.Close();
    }

    private void CloseLootWindowsForProcess(int processId)
    {
        if (this.fleetLootWindowForms.Remove(processId, out var lootWindowForm) &&
            !lootWindowForm.IsDisposed)
        {
            lootWindowForm.Close();
        }

        this.sessionLootOwnerByLeaderProcessId.Remove(processId);
        this.roundRobinLootEnabledByLeaderProcessId.Remove(processId);
        this.activeLootAssignmentByLeaderProcessId.Remove(processId);
    }

    private void ClientProcessStarted(int processId)
    {
        Process process;

        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            return;
        }

        var client = new ClientInstance(processId, process);

        if (this.IsManagedLaunchClientCandidate(process))
        {
            var launchRequest = this.pendingManagedClientLaunch;

            client.StartedByManager = true;
            client.StartedByManagerAt = DateTimeOffset.UtcNow;
            client.State = ClientState.WaitingForTos;
            client.AutomationStatus = "Waiting for TOS";
            client.ManagedLaunchRequest = launchRequest;

            if (launchRequest?.HasGameResolution == true &&
                this.gameRenderResolutionOverrideCoordinator.HasActiveOverride)
            {
                this.gameRenderResolutionOverrideProcessId = processId;
                this.gameRenderResolutionRestoreDeadline =
                    DateTimeOffset.UtcNow +
                    managerStartedClientDetectionWindow;
            }

            if (launchRequest?.TargetSlotId is { } launchedSlotId)
            {
                this.ClearManagedClientLaunchFailure(launchedSlotId);
            }

            if (launchRequest?.PlacementPolicy ==
                ManagedClientPlacementPolicy.ProfileSlot &&
                launchRequest.ProfileId == this.settings.CurrentProfileId &&
                launchRequest.TargetSlotId is { } targetSlotId &&
                this.ActiveProfile?.Slots.Exists(
                    slot => slot.Id == targetSlotId) == true)
            {
                client.AssignedSlotId = targetSlotId;
            }
            else if (launchRequest != null)
            {
                client.AllowProfileAutoAssignment = false;
            }

            this.pendingManagedClientLaunch = null;
            this.expectManagerStartedClientUntil = null;
            this.launcherSession = null;
        }

        lock (this.lockObject)
        {
            this.clients[processId] = client;
        }

        this.clientObservationCoordinator.Attach(processId);
    }

    private bool IsManagedLaunchClientCandidate(Process process)
    {
        if (this.IsExpectedManagerStartedClient())
        {
            return true;
        }

        var session = this.launcherSession;

        if (session == null || this.pendingManagedClientLaunch == null)
        {
            return false;
        }

        try
        {
            var processStartedAt =
                new DateTimeOffset(process.StartTime.ToUniversalTime());

            return processStartedAt >=
                   session.StartedAt - launcherSingletonRebindGrace;
        }
        catch (Exception)
        {
            // ClientProcessWatcher reports only newly observed client.exe
            // processes. While one managed launch is pending, claiming that
            // new process is safer than abandoning a successful manual Play
            // handoff merely because Windows withheld its start time.
            return true;
        }
    }

    private bool IsExpectedManagerStartedClient()
    {
        return this.expectManagerStartedClientUntil != null
               && DateTimeOffset.UtcNow <= this.expectManagerStartedClientUntil.Value;
    }

    private void ClientProcessStopped(int processId)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            if (!this.clients.Remove(processId, out client))
            {
                return;
            }
        }

        if (this.gameRenderResolutionOverrideProcessId == processId)
        {
            _ = this.TryRestoreGameRenderResolution(out _);
        }

        _ = this.gameInstallationObservedProcessIds.Remove(processId);
        this.navigationAutoPilotCoordinator.ForgetProcess(processId);
        this.pilotArchiveCoordinator.ForgetProcess(processId);
        this.CloseGroupSkillWindowsForProcess(processId);
        this.CloseLootWindowsForProcess(processId);

        client.HostForm?.CloseFromManager();
        this.gameKeyBindingResolver.ForgetProcess(processId);
        this.clientObservationCoordinator.Detach(processId);
        this.missionJournalCoordinator.ForgetProcess(processId);
        this.activityJournalCoordinator.ForgetProcess(processId);
        this.combatJournalCoordinator.ForgetProcess(processId);
        this.NavigationRoutes.DetachProcess(processId);
        this.addonRuntimeCoordinator.DetachOwner(processId);
    }

    private void HostedClientActivationTimer_OnTick(
        object? sender,
        EventArgs e)
    {
        var mouseIsDown =
            NativeMethods.IsKeyDown(Keys.LButton) ||
            NativeMethods.IsKeyDown(Keys.RButton) ||
            NativeMethods.IsKeyDown(Keys.MButton) ||
            NativeMethods.IsKeyDown(Keys.XButton1) ||
            NativeMethods.IsKeyDown(Keys.XButton2);
        var mousePressStarted =
            mouseIsDown &&
            !this.hostedClientMouseWasDown;

        this.hostedClientMouseWasDown = mouseIsDown;

        if (!mousePressStarted ||
            this.foregroundInputCoordinator.IsBusy ||
            !NativeMethods.TryGetCursorScreenPosition(
                out var cursorPosition))
        {
            return;
        }

        var clickedWindowHandle =
            NativeMethods.GetWindowAtScreenPoint(
                cursorPosition);

        if (clickedWindowHandle == IntPtr.Zero)
        {
            return;
        }

        ClientHostForm? clickedHost = null;

        lock (this.lockObject)
        {
            foreach (var client in this.clients.Values)
            {
                if (client.GameWindowHandle == IntPtr.Zero ||
                    client.HostForm is not
                    {
                        IsDisposed: false,
                        Disposing: false,
                        Visible: true,
                    } hostForm ||
                    !NativeMethods.IsChildOrSameWindow(
                        client.GameWindowHandle,
                        clickedWindowHandle))
                {
                    continue;
                }

                clickedHost = hostForm;
                break;
            }
        }

        if (clickedHost == null ||
            !clickedHost.IsHandleCreated ||
            clickedHost.WindowState ==
                FormWindowState.Minimized)
        {
            return;
        }

        // Do not activate or refocus anything here. The physical click has
        // already been delivered to ENB. We only repair the surrounding
        // host's desktop Z-order so the complete hosted client follows the
        // game window to the front without disturbing mouse or keyboard input.
        _ = NativeMethods.TryBringWindowToTopWithoutActivation(
            clickedHost.Handle);
    }

    private void ClientWindowTimer_OnTick(object? sender, EventArgs e)
    {
        this.TickGameRenderResolutionRestoration();
        this.TickLauncherAutomation();

        foreach (var client in this.Clients)
        {
            _ = this.TryObserveGameInstallation(client);

            switch (client.State)
            {
                case ClientState.WaitingForGameWindow:
                    this.TryDockWaitingClient(client);
                    break;

                case ClientState.WaitingForTos:
                case ClientState.AcceptingTos:
                    this.TickStartedClientAutomation(client);
                    break;

                case ClientState.Docked:
                    this.StartAutomationIfEnabled(client);
                    break;

                case ClientState.WaitingForIntro:
                case ClientState.WaitingForLogin:
                case ClientState.LoginSubmitted:
                case ClientState.WaitingForCharacterSelect:
                case ClientState.EnteringGame:
                case ClientState.Ready:
                    this.TickClientAutomation(client);
                    break;

                case ClientState.Closing:
                case ClientState.Stopped:
                default:
                    break;
            }
        }

        this.TickClientCreationAutomation();
        this.TickAutomaticNavigationDataUpdates();
    }

    private void TickClientCreationAutomation()
    {
        var profile = this.ActiveProfile;

        if (profile == null)
        {
            this.createMissingClientsRequested = false;
            return;
        }

        if (!this.createMissingClientsRequested &&
            !profile.KeepClientsAlive)
        {
            return;
        }

        if (this.automationOwner == null ||
            this.IsManagedClientLaunchInProgress)
        {
            return;
        }

        if (this.nextMissingClientStartAllowedAt != null &&
            DateTimeOffset.UtcNow < this.nextMissingClientStartAllowedAt.Value)
        {
            return;
        }

        if (this.HasClientStillStarting())
        {
            return;
        }

        ClientSlot? missingSlot;

        lock (this.lockObject)
        {
            missingSlot = profile.Slots.FirstOrDefault(
                slot =>
                    !this.IsSlotSatisfied(slot) &&
                    !this.managedClientLaunchFailures.ContainsKey(slot.Id));
        }

        if (missingSlot == null)
        {
            this.createMissingClientsRequested = false;
            return;
        }

        _ = this.StartProfileSlot(
            missingSlot.Id,
            this.automationOwner,
            out _);

        this.nextMissingClientStartAllowedAt =
            DateTimeOffset.UtcNow + missingClientStartCooldown;
    }

    private bool HasClientStillStarting()
    {
        lock (this.lockObject)
        {
            return this.clients.Values.Any(client =>
                client.State is ClientState.WaitingForGameWindow
                    or ClientState.WaitingForTos
                    or ClientState.AcceptingTos
                    or ClientState.WaitingForIntro
                    or ClientState.WaitingForLogin
                    or ClientState.WaitingForCharacterSelect
                    or ClientState.EnteringGame ||
                (
                    client.State == ClientState.LoginSubmitted &&
                    client.LifecycleState !=
                    ClientLifecycleState.CharacterSelection
                ));
        }
    }

    private void TryDockWaitingClient(ClientInstance client)
    {
        var gameWindowHandle = this.clientWindowFinder.FindGameWindow(client.ProcessId);

        if (gameWindowHandle is not { } resolvedGameWindowHandle)
        {
            return;
        }

        client.GameWindowHandle = resolvedGameWindowHandle;

        if (this.gameRenderResolutionOverrideProcessId == client.ProcessId)
        {
            _ = this.TryRestoreGameRenderResolution(out _);
        }

        this.DockClient(client);
    }

    private void DockClient(ClientInstance client)
    {
        if (client.HostForm != null)
        {
            return;
        }

        var hostForm = new ClientHostForm(
            this,
            client,
            this.clientDockingService,
            this.CloseClient,
            this.OpenGalaxyAtlas,
            this.OpenWorldFind,
            this.OpenForgeContributions,
            this.OpenPilotArchive,
            this.OpenSocial,
            this.OpenAddonCenter,
            this.RequestHelp,
            this.RequestInGameOptions,
            this.ResolveMissionWikiDestination,
            mission => this.ResolveMissionJobGuidance(
                client.ProcessId,
                mission),
            destination => this.SetNavigationDestination(
                client.ProcessId,
                destination),
            this.addonRuntimeCoordinator.PublishUiInteraction,
            (addonId, widgetId) => this.GetAddonWindowPlacement(
                client.ProcessId,
                addonId,
                widgetId),
            (addonId, widgetId, placement) =>
                this.SaveAddonWindowPlacement(
                    client.ProcessId,
                    addonId,
                    widgetId,
                    placement),
            this.skillBuildLocalWorkspace,
            this.forgeContributionCoordinator,
            (itemTemplateId, size) => this.GetGameItemIcon(
                client.ProcessId,
                itemTemplateId,
                size),
            this.clientObservationCoordinator
                .RequestTooltipItemPoll);

        client.HostForm = hostForm;
        hostForm.SetGameItemToolTipOptions(
            this.settings.GameItemToolTips.Enabled,
            this.settings.GameItemToolTips.HorizontalOffset,
            this.settings.GameItemToolTips.VerticalOffset);
        hostForm.SetVendorShoppingCompanionEnabled(
            this.settings.WorldFind.ShowVendorCompanion);
        hostForm.SetAddonsSuspendedForSession(
            this.AddonsSuspendedForSession);
        hostForm.SetAddonPresentationState(
            client.LifecycleState,
            client.LoadingOrTransitionFlag != 0);
        client.State = ClientState.Docked;
        client.DockedAt = DateTimeOffset.UtcNow;
        client.LastIntroSkipClickAt = null;
        client.LoginSubmittedAt = null;
        client.AutoLoginProvenance = null;
        client.EnterGameClickAt = null;
        client.EnterGameSubmittedAt = null;
        client.PendingEnterGameAction = null;

        this.AutoAssignSlotsIfEnabled();

        var slot = this.GetAssignedSlot(client);

        if (slot != null)
        {
            hostForm.ApplySlot(slot);
        }
        else if (client.ManagedLaunchRequest is
                 { HasHostSize: true } launchRequest)
        {
            hostForm.ApplyManagedHostSize(
                launchRequest.HostWidth!.Value,
                launchRequest.HostHeight!.Value);
        }

        hostForm.Show();

        if (this.clientObservationCoordinator.TryGetSnapshot(
                client.ProcessId,
                out var currentSnapshot))
        {
            this.missionJournalCoordinator.Observe(
                currentSnapshot);
            hostForm.UpdateMissionWikiPresentation(
                currentSnapshot);
            hostForm.UpdateJobTerminalRoutePresentation(
                this.ResolveJobTerminalRoutePresentation(
                    client.ProcessId,
                    currentSnapshot));
            hostForm.UpdateFactionDetailsPresentation(
                this.ResolveFactionDetailsPresentation(
                    currentSnapshot.PanelPresentation,
                    currentSnapshot.LocalPlayer));
            hostForm.UpdateSkillPlannerPresentation(
                currentSnapshot);
            hostForm.UpdateGameItemToolTipSnapshot(
                currentSnapshot);
            hostForm.UpdateVendorShoppingCompanionPresentation(
                this.ResolveVendorShoppingCompanionPresentation(
                    currentSnapshot));
            hostForm.UpdateGameItemToolTipHover(
                currentSnapshot.TooltipHover);
        }

        this.StartAutomationIfEnabled(client);
    }

    private ManagedClientLaunchRequest? ResolveClientLaunchRequest(
        ClientInstance client)
    {
        var slot = this.GetAssignedSlot(client);

        if (slot != null)
        {
            var gameResolution = ResolveSlotGameResolution(slot);

            return new ManagedClientLaunchRequest
            {
                Kind = ManagedClientLaunchKind.ProfileSlot,
                PlacementPolicy = ManagedClientPlacementPolicy.ProfileSlot,
                ProfileId = this.settings.CurrentProfileId,
                TargetSlotId = slot.Id,
                AccountId = slot.AccountId,
                CharacterId = slot.CharacterId,
                SkipIntro = client.StartedByManager || slot.AutoLogin,
                AutoLogin = slot.AutoLogin,
                AutoEnterGame = slot.AutoEnterGame,
                GameResolutionWidth = gameResolution.Width,
                GameResolutionHeight = gameResolution.Height,
                DisplayName = slot.Name,
            };
        }

        return client.ManagedLaunchRequest;
    }

    private void StartAutomationIfEnabled(ClientInstance client)
    {
        if (client.State != ClientState.Docked)
        {
            client.AutomationStatus = $"Not docked: {client.State}";
            return;
        }

        var launch = this.ResolveClientLaunchRequest(client);

        if (launch == null)
        {
            client.AutomationStatus = client.LifecycleState ==
                ClientLifecycleState.InGame
                    ? "In game; launch automation off"
                    : "Launch automation off";
            return;
        }

        client.DockedAt ??= DateTimeOffset.UtcNow;
        client.LastIntroSkipClickAt = null;
        client.LoginSubmittedAt = null;
        client.AutoLoginProvenance = null;
        client.EnterGameClickAt = null;
        client.EnterGameSubmittedAt = null;
        client.PendingEnterGameAction = null;

        switch (client.LifecycleState)
        {
            case ClientLifecycleState.InGame:
                client.State = ClientState.Ready;
                client.AutomationStatus = "In game";
                break;

            case ClientLifecycleState.CharacterSelection:
                if (!launch.AutoLogin)
                {
                    client.AutomationStatus =
                        "Character selection ready; auto login off";
                    break;
                }

                client.State = launch.AutoEnterGame
                    ? ClientState.WaitingForCharacterSelect
                    : ClientState.LoginSubmitted;

                client.AutomationStatus = launch.AutoEnterGame
                    ? "Character selection ready"
                    : "Character selection ready; auto enter off";
                break;

            case ClientLifecycleState.LoginScreen:
                if (launch.AutoLogin)
                {
                    client.State = ClientState.WaitingForLogin;
                    client.AutomationStatus = "Login screen ready";
                }
                else
                {
                    client.AutomationStatus =
                        "Login screen ready; auto login off";
                }

                break;

            case ClientLifecycleState.IntroScene:
                if (launch.SkipIntro)
                {
                    client.State = ClientState.WaitingForIntro;
                    client.AutomationStatus = "Skipping intro";
                }
                else
                {
                    client.AutomationStatus = "Intro ready; automation off";
                }

                break;

            case ClientLifecycleState.Unknown:
            case ClientLifecycleState.ApplicationStarted:
            default:
                if (launch.SkipIntro || launch.AutoLogin)
                {
                    client.State = ClientState.WaitingForIntro;
                    client.AutomationStatus = "Waiting for client lifecycle";
                }
                else
                {
                    client.AutomationStatus = "Launch automation off";
                }

                break;
        }
    }

    private bool IsWaitingForExpectedManagerStartedClient()
    {
        if (this.expectManagerStartedClientUntil == null)
        {
            return false;
        }

        if (DateTimeOffset.UtcNow <= this.expectManagerStartedClientUntil.Value)
        {
            return true;
        }

        var playAttemptCount =
            this.launcherSession?.PlayAttemptCount ?? 0;

        if (playAttemptCount == 0)
        {
            this.FailManagedClientLaunch(
                "The game client did not appear within 60 seconds after Net7 Launcher closed.");
            return false;
        }

        var attemptText = playAttemptCount == 1
            ? "one Play attempt"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{playAttemptCount} Play attempts");

        this.FailManagedClientLaunch(
            string.Concat(
                "The game client did not appear within 60 seconds after ",
                attemptText,
                "."));

        return false;
    }

    private void TickClientAutomation(ClientInstance client)
    {
        if (client.GameWindowHandle == IntPtr.Zero ||
            client.Process.HasExited)
        {
            return;
        }

        var launch = this.ResolveClientLaunchRequest(client);

        if (launch == null)
        {
            client.State = client.LifecycleState ==
                ClientLifecycleState.InGame
                    ? ClientState.Ready
                    : ClientState.Docked;
            return;
        }

        if (client.LifecycleState ==
            ClientLifecycleState.InGame)
        {
            client.State = ClientState.Ready;
            client.AutomationStatus = "In game";
            client.PendingEnterGameAction = null;
            client.EnterGameClickAt = null;
            client.EnterGameSubmittedAt = null;
            return;
        }

        switch (client.State)
        {
            case ClientState.WaitingForIntro:
                this.TickWaitingForIntro(client, launch);
                return;

            case ClientState.WaitingForLogin:
                if (!launch.AutoLogin ||
                    launch.AccountId == null)
                {
                    client.State = ClientState.Docked;
                    client.AutomationStatus =
                        "Login screen ready; auto login off";
                    return;
                }

                if (client.LifecycleState ==
                    ClientLifecycleState.LoginScreen)
                {
                    this.TryFillLoginName(client, launch);
                }
                else
                {
                    client.AutomationStatus =
                        $"Waiting for login screen; observed {client.LifecycleState}";
                }

                return;

            case ClientState.LoginSubmitted:
                client.AutomationStatus = client.LifecycleState ==
                    ClientLifecycleState.CharacterSelection
                        ? "Character selection ready; auto enter off"
                        : $"Login submitted; observed {client.LifecycleState}";

                return;

            case ClientState.WaitingForCharacterSelect:
                if (client.LifecycleState ==
                    ClientLifecycleState.CharacterSelection)
                {
                    this.TryEnterGame(client, launch);
                    return;
                }

                if (client is
                    {
                        LifecycleState: ClientLifecycleState.LoginScreen,
                        LoginSubmittedAt: not null,
                    } &&
                    DateTimeOffset.UtcNow -
                    client.LoginSubmittedAt.Value >=
                    TimeSpan.FromSeconds(10))
                {
                    client.State = ClientState.WaitingForLogin;
                    client.AutomationStatus =
                        "Login did not advance; retrying";
                    return;
                }

                client.AutomationStatus =
                    $"Waiting for character selection; observed {client.LifecycleState}";

                return;

            case ClientState.EnteringGame:
                if (client.LifecycleState ==
                    ClientLifecycleState.CharacterSelection)
                {
                    if (client.EnterGameSubmittedAt != null &&
                        DateTimeOffset.UtcNow -
                        client.EnterGameSubmittedAt.Value >=
                        enterGameAttemptTimeout)
                    {
                        client.State =
                            ClientState.WaitingForCharacterSelect;

                        client.PendingEnterGameAction = null;
                        client.EnterGameClickAt = null;
                        client.EnterGameSubmittedAt = null;
                        client.CharacterSelectionObservedAt =
                            DateTimeOffset.UtcNow;

                        client.AutomationStatus =
                            "Enter game did not advance; restarting character selection";

                        return;
                    }

                    this.TryEnterGame(client, launch);
                    return;
                }

                client.AutomationStatus =
                    $"Entering game; observed {client.LifecycleState}";

                return;

            case ClientState.Ready:
                client.AutomationStatus =
                    $"Gameplay transition; observed {client.LifecycleState}";

                return;

            case ClientState.WaitingForGameWindow:
            case ClientState.Docked:
            case ClientState.WaitingForTos:
            case ClientState.AcceptingTos:
            case ClientState.Closing:
            case ClientState.Stopped:
            default:
                return;
        }
    }

    private void TickWaitingForIntro(
        ClientInstance client,
        ManagedClientLaunchRequest launch)
    {
        switch (client.LifecycleState)
        {
            case ClientLifecycleState.IntroScene:
                if (!launch.SkipIntro)
                {
                    client.State = ClientState.Docked;
                    client.AutomationStatus = "Intro ready; automation off";
                    return;
                }

                client.AutomationStatus = "Skipping intro";

                if (client.LastIntroSkipClickAt == null ||
                    DateTimeOffset.UtcNow -
                    client.LastIntroSkipClickAt.Value >=
                    introSkipClickInterval)
                {
                    this.TrySkipIntroWithSafeClick(client);
                }

                return;

            case ClientLifecycleState.LoginScreen:
                if (launch.AutoLogin)
                {
                    client.State = ClientState.WaitingForLogin;
                    client.AutomationStatus = "Login screen ready";
                }
                else
                {
                    client.State = ClientState.Docked;
                    client.AutomationStatus =
                        "Login screen ready; auto login off";
                }

                return;

            case ClientLifecycleState.CharacterSelection:
                if (!launch.AutoLogin)
                {
                    client.State = ClientState.Docked;
                    client.AutomationStatus =
                        "Character selection ready; auto login off";
                    return;
                }

                client.State = launch.AutoEnterGame
                    ? ClientState.WaitingForCharacterSelect
                    : ClientState.LoginSubmitted;

                client.AutomationStatus = launch.AutoEnterGame
                    ? "Character selection ready"
                    : "Character selection ready; auto enter off";
                return;

            case ClientLifecycleState.InGame:
                client.State = ClientState.Ready;
                client.AutomationStatus = "In game";
                return;

            case ClientLifecycleState.Unknown:
            case ClientLifecycleState.ApplicationStarted:
            default:
                client.AutomationStatus =
                    $"Waiting for intro or login screen; observed {client.LifecycleState}";
                return;
        }
    }

    private void TrySkipIntroWithSafeClick(ClientInstance client)
    {
        if (client.GameWindowHandle == IntPtr.Zero ||
            client.LifecycleState != ClientLifecycleState.IntroScene ||
            !this.foregroundInputCoordinator.TryAcquire(
                out var foregroundLease))
        {
            return;
        }

        using (foregroundLease)
        {
            // Lifecycle observations can change between timer ticks. Recheck
            // after acquiring foreground-input ownership, then click a quiet
            // corner that remains harmless if the intro has just disappeared.
            if (client.GameWindowHandle == IntPtr.Zero ||
                client.LifecycleState != ClientLifecycleState.IntroScene ||
                !NativeMethods.TryGetClientSize(
                    client.GameWindowHandle,
                    out var clientSize))
            {
                return;
            }

            var safeX = Math.Clamp(clientSize.Width / 100, 8, 16);

            // Keep the pointer in the quiet left gutter, but well below the
            // hover-title-bar reveal strip and the native top menu. The old
            // top-corner click could leave the cursor parked in the reveal
            // zone long enough for the temporary title bar to appear before
            // the next intro-skip attempt.
            var safeY = Math.Clamp(
                clientSize.Height / 5,
                96,
                160);

            if (NativeMethods.ForegroundLeftClick(
                    client.GameWindowHandle,
                    safeX,
                    safeY))
            {
                client.LastIntroSkipClickAt = DateTimeOffset.UtcNow;
            }
        }
    }

    private void TryFillLoginName(
        ClientInstance client,
        ManagedClientLaunchRequest launch)
    {
        if (client.LifecycleState !=
            ClientLifecycleState.LoginScreen)
        {
            client.AutomationStatus =
                $"Login input deferred; observed {client.LifecycleState}";

            return;
        }

        var account = this.FindConfiguredAccount(launch.AccountId);

        if (account == null)
        {
            client.AutomationStatus = "Missing account";
            return;
        }

        if (string.IsNullOrWhiteSpace(account.LoginName))
        {
            client.AutomationStatus = "Missing login name";
            return;
        }

        var password = PasswordProtector.Unprotect(
            account.ProtectedPassword);

        if (string.IsNullOrEmpty(password))
        {
            client.AutomationStatus = "Missing password";
            return;
        }

        if (!this.TryClickNamedInputAction(
                client,
                LoginScreenUsernameClickActionName))
        {
            client.AutomationStatus =
                "Missing login screen username target";

            return;
        }

        SendKeys.SendWait("^a");
        SendKeys.SendWait(
            EscapeSendKeysText(account.LoginName));

        SendKeys.SendWait("{TAB}");
        SendKeys.SendWait("^a");
        SendKeys.SendWait(
            EscapeSendKeysText(password));

        SendKeys.SendWait("{ENTER}");

        var submittedAt = DateTimeOffset.UtcNow;

        client.LoginSubmittedAt = submittedAt;
        client.AutoLoginProvenance = new AutoLoginProvenance
        {
            AccountId = account.Id,
            LoginName = account.LoginName.Trim(),
            SubmittedAt = submittedAt,
        };

        client.HostForm?.RefreshRuntimeTitle();
        client.AutomationStatus = "Login submitted";

        client.State = launch.AutoEnterGame
            ? ClientState.WaitingForCharacterSelect
            : ClientState.LoginSubmitted;
    }

    private void TryEnterGame(
        ClientInstance client,
        ManagedClientLaunchRequest launch)
    {
        if (client.LifecycleState !=
                ClientLifecycleState.CharacterSelection ||
            !launch.AutoEnterGame)
        {
            return;
        }

        if (client.PendingEnterGameAction == null)
        {
            client.CharacterSelectionObservedAt ??=
                DateTimeOffset.UtcNow;

            var characterSelectionReadyAt =
                client.CharacterSelectionObservedAt.Value +
                characterSelectionScreenSettleDelay;

            if (DateTimeOffset.UtcNow <
                characterSelectionReadyAt)
            {
                client.AutomationStatus =
                    "Waiting for character screen to settle";

                return;
            }
        }

        var account = this.FindConfiguredAccount(launch.AccountId);
        var character = this.FindConfiguredCharacter(
            launch.AccountId,
            launch.CharacterId);

        if (account == null || character == null)
        {
            client.AutomationStatus = "Missing character";
            return;
        }

        var characterSelectClickActionName =
            GetCharacterSelectClickActionName(
                character.CharacterSlotNumber);

        var clickActions =
            new InputActionStore().LoadClickActions();

        var characterSlotAction =
            clickActions.FirstOrDefault(action =>
                string.Equals(
                    action.Name,
                    characterSelectClickActionName,
                    StringComparison.OrdinalIgnoreCase));

        var enterGameAction =
            clickActions.FirstOrDefault(action =>
                string.Equals(
                    action.Name,
                    "Character Screen Enter Game",
                    StringComparison.OrdinalIgnoreCase));

        if (characterSlotAction == null ||
            enterGameAction == null)
        {
            client.AutomationStatus =
                "Missing character screen actions";

            return;
        }

        if (client.PendingEnterGameAction != null)
        {
            if (client.EnterGameClickAt == null ||
                DateTimeOffset.UtcNow <
                client.EnterGameClickAt.Value)
            {
                client.AutomationStatus =
                    client.EnterGameSubmittedAt == null
                        ? "Waiting for character presentation animation"
                        : "Waiting to retry Enter Game";

                return;
            }

            var isRetry =
                client.EnterGameSubmittedAt != null;

            if (!this.ClickInputAction(
                    client,
                    client.PendingEnterGameAction))
            {
                return;
            }

            var clickedAt = DateTimeOffset.UtcNow;

            client.EnterGameSubmittedAt ??= clickedAt;
            client.EnterGameClickAt =
                clickedAt + enterGameRetryInterval;

            client.AutomationStatus = isRetry
                ? $"Retrying Enter Game for {character.Name}"
                : $"Enter Game clicked for {character.Name}";

            client.State = ClientState.EnteringGame;
            return;
        }

        if (!this.ClickInputAction(
                client,
                characterSlotAction))
        {
            return;
        }

        client.PendingEnterGameAction = enterGameAction;
        client.EnterGameSubmittedAt = null;
        client.EnterGameClickAt =
            DateTimeOffset.UtcNow +
            characterSelectionAnimationDelay;

        client.AutomationStatus =
            $"Selected character {character.Name}; waiting for presentation animation";
    }

    private static string GetCharacterSelectClickActionName(int characterSlotNumber)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Character Screen Slot {characterSlotNumber}");
    }

    private bool TryClickNamedInputAction(ClientInstance client, string actionName)
    {
        var clickActions = new InputActionStore().LoadClickActions();

        var action = clickActions.FirstOrDefault(action =>
                                                     string.Equals(
                                                         action.Name,
                                                         actionName,
                                                         StringComparison.OrdinalIgnoreCase));

        if (action == null)
        {
            client.AutomationStatus = $"Missing click target: {actionName}";
            return false;
        }

        return this.ClickInputAction(client, action);
    }

    private bool ClickInputAction(ClientInstance client, InputActionDefinition action)
    {
        if (action.Kind != InputActionKind.MouseClick)
        {
            client.AutomationStatus = $"Action is not a click: {action.Name}";
            return false;
        }

        if (client.GameWindowHandle == IntPtr.Zero)
        {
            client.AutomationStatus = "Missing game window";
            return false;
        }

        if (!NativeMethods.TryGetClientSize(client.GameWindowHandle, out var clientSize))
        {
            client.AutomationStatus = "Could not get client size";
            return false;
        }

        var x = (int)Math.Round(action.BaseX * clientSize.Width / action.BaseWidth);
        var y = (int)Math.Round(action.BaseY * clientSize.Height / action.BaseHeight);

        var clicked = NativeMethods.ForegroundLeftClick(
            client.GameWindowHandle,
            x,
            y);

        if (!clicked)
        {
            client.AutomationStatus = $"Click failed: {action.Name}";
            return false;
        }

        client.AutomationStatus = $"Clicked {action.Name}";
        return true;
    }

    private static string EscapeSendKeysText(string value)
    {
        return value
            .Replace("{", "{{}", StringComparison.Ordinal)
            .Replace("}", "{}}", StringComparison.Ordinal)
            .Replace("+", "{+}", StringComparison.Ordinal)
            .Replace("^", "{^}", StringComparison.Ordinal)
            .Replace("%", "{%}", StringComparison.Ordinal)
            .Replace("~", "{~}", StringComparison.Ordinal)
            .Replace("(", "{(}", StringComparison.Ordinal)
            .Replace(")", "{)}", StringComparison.Ordinal);
    }

    private void CloseClient(ClientInstance client, CloseReason reason)
    {
        if (client.State is ClientState.Closing or ClientState.Stopped)
        {
            return;
        }

        client.State = ClientState.Closing;

        try
        {
            switch (reason)
            {
                case CloseReason.UserRequested:
                    this.KillClientProcess(client);
                    break;

                case CloseReason.ApplicationExit:
                    this.KillClientProcess(client);
                    break;

                case CloseReason.ProcessExited:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(reason), reason, message: null);
            }
        }
        finally
        {
            client.State = ClientState.Stopped;

            lock (this.lockObject)
            {
                this.clients.Remove(client.ProcessId);
            }

            this.CloseAddonCenter(client.ProcessId);
            this.navigationAutoPilotCoordinator.ForgetProcess(
                client.ProcessId);
            client.HostForm?.CloseFromManager();
            this.gameKeyBindingResolver.ForgetProcess(client.ProcessId);
            this.clientObservationCoordinator.Detach(client.ProcessId);
            this.NavigationRoutes.DetachProcess(client.ProcessId);
            this.addonRuntimeCoordinator.DetachOwner(client.ProcessId);
        }
    }

    private async ValueTask<AddonCommandResult>
        ExecuteAddonActionAsync(
            AddonActionRequest request,
            CancellationToken cancellationToken)
    {
        ClientInstance? client;
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(
                request.OwnerProcessId,
                out client);

            hostForm = client?.HostForm;
        }

        if (client == null || hostForm == null)
        {
            return AddonCommandResult.Failure(
                "The addon owner is no longer hosted");
        }

        switch (request.Kind)
        {
            case AddonActionKind.SelectNearbyTarget:
                return await this.nearbyTargetSelectionService
                    .SelectAsync(
                        client,
                        hostForm,
                        request,
                        cancellationToken)
                    .ConfigureAwait(false);

            case AddonActionKind.FireAllWeapons:
                return await this.ExecuteAddonGameCommandAsync(
                        client,
                        GameCommand.FireAll,
                        cancellationToken)
                    .ConfigureAwait(false);

            case AddonActionKind.TargetNearestNavigation:
                if (this.navigationAutoPilotCoordinator
                    .GetSnapshot(request.OwnerProcessId)
                    .IsActive)
                {
                    return AddonCommandResult.Failure(
                        "Auto Pilot already owns navigation targeting for this client.");
                }

                return await this.ExecuteAddonGameCommandAsync(
                        client,
                        GameCommand.TargetNearestNavigation,
                        cancellationToken)
                    .ConfigureAwait(false);

            case AddonActionKind.PreviousTarget:
                if (this.navigationAutoPilotCoordinator
                    .GetSnapshot(request.OwnerProcessId)
                    .IsActive)
                {
                    return AddonCommandResult.Failure(
                        "Auto Pilot already owns navigation targeting for this client.");
                }

                return await this.ExecuteAddonGameCommandAsync(
                        client,
                        GameCommand.PreviousTarget,
                        cancellationToken)
                    .ConfigureAwait(false);

            case AddonActionKind.OpenNavigationPlanner:
                this.NavigationPlannerRequested?.Invoke(
                    this,
                    new NavigationPlannerRequestedEventArgs(
                        request.OwnerProcessId));
                return AddonCommandResult.Success();

            case AddonActionKind.SelectNextNavigationTarget:
                {
                    if (this.navigationAutoPilotCoordinator
                        .GetSnapshot(request.OwnerProcessId)
                        .IsActive)
                    {
                        return AddonCommandResult.Failure(
                            "Auto Pilot already owns route-target selection for this client.");
                    }

                    var result = await this
                        .navigationTargetSelectionService
                        .SelectNextTargetAsync(
                            client,
                            hostForm,
                            request.ExpectedSectorId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    return result.Succeeded
                        ? AddonCommandResult.Success()
                        : AddonCommandResult.Failure(result.Error);
                }

            case AddonActionKind.StartNavigationAutoPilot:
                {
                    var result = this.StartNavigationAutoPilot(
                        request.OwnerProcessId,
                        request.ExpectedSectorId);

                    return result.Succeeded
                        ? AddonCommandResult.Success()
                        : AddonCommandResult.Failure(result.Error);
                }

            case AddonActionKind.StopNavigationAutoPilot:
                {
                    var result = this.navigationAutoPilotCoordinator
                        .Stop(request.OwnerProcessId);

                    return result.Succeeded
                        ? AddonCommandResult.Success()
                        : AddonCommandResult.Failure(result.Error);
                }

            case AddonActionKind.PlanNavigationReturnTrip:
                {
                    var result = this.PlanNavigationReturnTrip(
                        request.OwnerProcessId);

                    return result.Succeeded
                        ? AddonCommandResult.Success()
                        : AddonCommandResult.Failure(result.Error);
                }

            case AddonActionKind.ClearNavigationRoute:
                {
                    if (this.navigationAutoPilotCoordinator
                        .GetSnapshot(request.OwnerProcessId)
                        .IsActive)
                    {
                        return AddonCommandResult.Failure(
                            "Stop Auto Pilot before clearing the active route.");
                    }

                    var result = this.NavigationRoutes
                        .ClearRoute(request.OwnerProcessId);

                    return result.Succeeded
                        ? AddonCommandResult.Success()
                        : AddonCommandResult.Failure(result.Error);
                }

            default:
                return AddonCommandResult.Failure(
                    "Unsupported addon action");
        }
    }

    private async ValueTask<AddonCommandResult>
        ExecuteAddonGameCommandAsync(
            ClientInstance client,
            GameCommand command,
            CancellationToken cancellationToken)
    {
        var result = await this.gameCommandCoordinator
            .ExecuteAsync(
                client,
                command,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded
            ? AddonCommandResult.Success()
            : AddonCommandResult.Failure(result.Error);
    }

    private void AddonRuntimeCoordinator_OnUiCommandEmitted(
        object? sender,
        AddonUiCommandEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.Command.OwnerProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        hostForm?.ApplyAddonUiCommand(e.Command);
    }

    private void ClientObservationCoordinator_OnMissionPresentationChanged(
        object? sender,
        ClientMissionPresentationChangedEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.ProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        if (this.clientObservationCoordinator.TryGetSnapshot(
                e.ProcessId,
                out var snapshot))
        {
            this.missionJournalCoordinator.Observe(snapshot);
        }

        hostForm?.UpdateMissionWikiPresentation(
            e.PanelPresentation,
            e.Missions);
    }

    private void ClientObservationCoordinator_OnFactionPresentationChanged(
        object? sender,
        ClientFactionPresentationChangedEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.ProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        hostForm?.UpdateFactionDetailsPresentation(
            this.ResolveFactionDetailsPresentation(
                e.PanelPresentation,
                e.LocalPlayer));
    }

    private void ClientObservationCoordinator_OnSkillPresentationChanged(
        object? sender,
        ClientSkillPresentationChangedEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.ProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        hostForm?.UpdateSkillPlannerPanelPresentation(
            e.PanelPresentation);
    }

    private void ClientObservationCoordinator_OnInventoryPresentationChanged(
        object? sender,
        ClientInventoryPresentationChangedEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.ProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        hostForm?.UpdateSkillPlannerPanelPresentation(
            e.PanelPresentation);
    }

    private void ClientObservationCoordinator_OnTooltipHoverChanged(
        object? sender,
        ClientTooltipHoverChangedEventArgs e)
    {
        ClientHostForm? hostForm;

        lock (this.lockObject)
        {
            hostForm = this.clients.TryGetValue(
                    e.ProcessId,
                    out var client)
                ? client.HostForm
                : null;
        }

        hostForm?.UpdateGameItemToolTipHover(e.Current);
    }

    private void ClientObservationCoordinator_OnSnapshotChanged(
        object? sender,
        ClientObservationSnapshotChangedEventArgs e)
    {
        ClientInstance? client;
        bool titleChanged;

        lock (this.lockObject)
        {
            if (!this.clients.TryGetValue(
                    e.Snapshot.ProcessId,
                    out client))
            {
                return;
            }

            var previousTitleState =
                CaptureHostedTitleState(client);

            client.LifecycleState =
                e.Snapshot.LifecycleState;

            client.LoadingOrTransitionFlag =
                e.Snapshot.LoadingOrTransitionFlag;

            if (e.Snapshot.LifecycleState ==
                ClientLifecycleState.CharacterSelection)
            {
                client.CharacterSelectionObservedAt ??=
                    e.Snapshot.ObservedAt;
            }
            else
            {
                client.CharacterSelectionObservedAt = null;
            }

            UpdateAutoLoginProvenance(
                client,
                e.Snapshot);

            UpdateLiveCharacterSession(
                client,
                e.Snapshot);

            client.ObservationStatus =
                e.Snapshot.StatusText;

            client.ObservationSequence =
                e.Snapshot.Sequence;

            client.LastObservedAt =
                e.Snapshot.ObservedAt;

            titleChanged = previousTitleState !=
                CaptureHostedTitleState(client);
        }

        if (titleChanged)
        {
            client.HostForm?.RefreshRuntimeTitle();
        }

        client.HostForm?.SetAddonPresentationState(
            e.Snapshot.LifecycleState,
            e.Snapshot.LoadingOrTransitionFlag != 0);
        client.HostForm?.UpdateGameItemToolTipSnapshot(
            e.Snapshot);
        client.HostForm?.UpdateVendorShoppingCompanionPresentation(
            this.ResolveVendorShoppingCompanionPresentation(
                e.Snapshot));

        this.missionJournalCoordinator.Observe(e.Snapshot);
        this.combatJournalCoordinator.Observe(e.Snapshot);
        this.activityJournalCoordinator.Observe(e.Snapshot);

        client.HostForm?.UpdateFactionDetailsPresentation(
            this.ResolveFactionDetailsPresentation(
                e.Snapshot.PanelPresentation,
                e.Snapshot.LocalPlayer));

        client.HostForm?.UpdateSkillPlannerPresentation(
            e.Snapshot);

        if (e.Snapshot.LifecycleState != ClientLifecycleState.InGame)
        {
            this.CloseGroupSkillWindowsForProcess(e.Snapshot.ProcessId);
        }

        client.HostForm?.UpdateMissionWikiPresentation(
            e.Snapshot);

        var navigationRoute = this.NavigationRoutes.Observe(
            e.Snapshot,
            ClientLiveCharacterIdentityResolver.Resolve(e.Snapshot),
            this.BuildNavigationWormholeAvailability(e.Snapshot));

        this.navigationAutoPilotCoordinator
            .ReconcileDestinationArrival(e.Snapshot.ProcessId);

        client.HostForm?.UpdateJobTerminalRoutePresentation(
            this.ResolveJobTerminalRoutePresentation(
                e.Snapshot.ProcessId,
                e.Snapshot));

        this.addonRuntimeCoordinator.UpdateOwnerSnapshot(
            e.Snapshot,
            this.BuildAddonOwnerRegistration(client),
            this.BuildAddonNavigationRouteSnapshot(navigationRoute, e.Snapshot));

        this.pilotArchiveCoordinator.Observe(e.Snapshot);
        this.forgeContributionCoordinator.Observe(e.Snapshot);
        this.socialCoordinator.Observe(e.Snapshot);
    }

    private void ActivityJournalCoordinator_OnEntryRecorded(
        object? sender,
        ActivityJournalEntryRecordedEventArgs e)
    {
        var activity = e.Activity;
        var common = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["kind"] = NormalizeAddonEnum(activity.Kind),
            ["category"] = activity.CategoryDisplay,
            ["pilot_name"] = activity.PilotName,
            ["summary"] = activity.Summary,
            ["details"] = activity.Details,
            ["system_name"] = activity.SystemName,
            ["sector_name"] = activity.SectorName,
            ["starbase_name"] = activity.StarbaseName,
            ["nearest_nav_name"] = activity.NearestNavName,
        };

        this.addonRuntimeCoordinator.PublishCharacterEvent(
            activity.CharacterId,
            "activity.recorded",
            common,
            activity.OccurredAt);

        if (e.Reputation != null)
        {
            var reputation = e.Reputation;
            this.addonRuntimeCoordinator.PublishCharacterEvent(
                activity.CharacterId,
                "activity.reputation_changed",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["name"] = reputation.DisplayName,
                    ["previous"] = reputation.PreviousReaction,
                    ["current"] = reputation.CurrentReaction,
                    ["change"] = reputation.Delta,
                    ["reason"] = reputation.Reason,
                    ["system_name"] = reputation.SystemName,
                    ["sector_name"] = reputation.SectorName,
                    ["starbase_name"] = reputation.StarbaseName,
                    ["nearest_nav_name"] = reputation.NearestNavName,
                },
                reputation.OccurredAt);
        }

        if (e.Loot != null)
        {
            var loot = e.Loot;
            this.addonRuntimeCoordinator.PublishCharacterEvent(
                activity.CharacterId,
                "activity.loot_updated",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["source_name"] = loot.SourceName,
                    ["credits"] = loot.Credits,
                    ["started_at"] = loot.StartedAt
                        .ToUnixTimeMilliseconds(),
                    ["last_updated_at"] = loot.LastUpdatedAt
                        .ToUnixTimeMilliseconds(),
                    ["items"] = loot.Items.Select(item =>
                        (object?)new Dictionary<string, object?>(
                            StringComparer.Ordinal)
                        {
                            ["name"] = item.Name,
                            ["quantity"] = item.Quantity,
                            ["quality_percent"] = item.QualityPercent,
                        }).ToArray(),
                    ["system_name"] = loot.SystemName,
                    ["sector_name"] = loot.SectorName,
                    ["starbase_name"] = loot.StarbaseName,
                    ["nearest_nav_name"] = loot.NearestNavName,
                },
                loot.LastUpdatedAt);
        }
    }

    private void MissionJournalCoordinator_OnLifecycleEvent(
        object? sender,
        MissionJournalLifecycleEventArgs e)
    {
        this.activityJournalCoordinator.RecordMissionEvent(e);

        var eventName = e.Kind switch
        {
            MissionJournalEventKind.Accepted => "mission.accepted",
            MissionJournalEventKind.SourceIdentified =>
                "mission.source_identified",
            MissionJournalEventKind.Progressed => "mission.progressed",
            MissionJournalEventKind.Completed => "mission.completed",
            MissionJournalEventKind.Forfeited => "mission.forfeited",
            MissionJournalEventKind.Failed => "mission.failed",
            MissionJournalEventKind.Expired => "mission.expired",
            MissionJournalEventKind.NoLongerActive =>
                "mission.no_longer_active",
            _ => null,
        };

        if (eventName == null)
        {
            return;
        }

        var entry = e.Entry;
        this.addonRuntimeCoordinator.PublishCharacterEvent(
            entry.CharacterId,
            eventName,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pilot_name"] = entry.PilotName,
                ["source"] = NormalizeAddonEnum(entry.Source),
                ["status"] = NormalizeAddonEnum(entry.Status),
                ["job_category"] = NormalizeAddonEnum(entry.JobCategory),
                ["name"] = entry.Name,
                ["summary"] = entry.Summary,
                ["reward"] = entry.RewardText,
                ["failure_consequence"] = entry.FailureConsequence,
                ["issuing_faction"] = entry.IssuingFaction,
                ["stage"] = entry.Stage,
                ["stage_count"] = entry.StageCount,
                ["objective"] = entry.CurrentObjective,
                ["issuer_npc_name"] = entry.IssuerNpcName,
                ["accepted_at"] = entry.AcceptedAt?
                    .ToUnixTimeMilliseconds(),
                ["ended_at"] = entry.EndedAt?
                    .ToUnixTimeMilliseconds(),
                ["duration_milliseconds"] = entry.Duration?
                    .TotalMilliseconds,
                ["system_name"] = e.SystemName,
                ["sector_name"] = e.SectorName,
                ["starbase_name"] = e.StarbaseName,
            },
            e.OccurredAt);
    }

    private void CombatJournalCoordinator_OnEncounterEnded(
        object? sender,
        CombatEncounterEndedEventArgs e)
    {
        this.activityJournalCoordinator.RecordCombatEncounter(e);

        var encounter = e.Encounter;
        this.addonRuntimeCoordinator.PublishCharacterEvent(
            encounter.CharacterId,
            "combat.encounter_ended",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["pilot_name"] = encounter.PilotName,
                ["outcome"] = NormalizeAddonEnum(encounter.Outcome),
                ["target_name"] = encounter.TargetName,
                ["target_combat_level"] = encounter.TargetCombatLevel,
                ["started_at"] = encounter.StartedAt
                    .ToUnixTimeMilliseconds(),
                ["ended_at"] = encounter.EndedAt?
                    .ToUnixTimeMilliseconds(),
                ["duration_milliseconds"] =
                    encounter.Duration.TotalMilliseconds,
                ["system_name"] = encounter.SystemName,
                ["sector_name"] = encounter.SectorName,
                ["starbase_name"] = encounter.StarbaseName,
                ["nearest_nav_name"] = encounter.NearestNavName,
                ["outgoing_damage"] = encounter.OutgoingDamage,
                ["incoming_damage"] = encounter.IncomingDamage,
                ["outgoing_hit_count"] = encounter.OutgoingHitCount,
                ["incoming_hit_count"] = encounter.IncomingHitCount,
                ["outgoing_critical_count"] =
                    encounter.OutgoingCriticalCount,
                ["incoming_critical_count"] =
                    encounter.IncomingCriticalCount,
                ["largest_outgoing_hit"] = encounter.LargestOutgoingHit,
                ["largest_incoming_hit"] = encounter.LargestIncomingHit,
            },
            encounter.EndedAt ?? encounter.LastEventAt);
    }

    private void ForgeContributionCoordinator_OnStatisticsChanged(
        object? sender,
        ForgeContributionStatisticsChangedEventArgs e)
    {
        var previous = this.addonLastForgeContributionSession;
        this.addonLastForgeContributionSession = e.Session;

        if (previous == null)
        {
            return;
        }

        var successfulBatches =
            e.Session.SuccessfulBatches - previous.SuccessfulBatches;
        var failedBatches = e.Session.FailedBatches - previous.FailedBatches;

        if (successfulBatches > 0)
        {
            this.addonRuntimeCoordinator.PublishGlobalEvent(
                "forge.contribution_succeeded",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "installation",
                    ["batch_count"] = successfulBatches,
                    ["facts_submitted"] =
                        GetSubmittedFactCount(e.Session) -
                        GetSubmittedFactCount(previous),
                    ["evidence_accepted"] =
                        GetAcceptedEvidenceCount(e.Session) -
                        GetAcceptedEvidenceCount(previous),
                    ["status"] = e.Session.Status,
                },
                e.Session.LastSuccessfulContributionUtc ??
                DateTimeOffset.UtcNow);
        }

        if (failedBatches > 0)
        {
            this.addonRuntimeCoordinator.PublishGlobalEvent(
                "forge.contribution_failed",
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["scope"] = "installation",
                    ["batch_count"] = failedBatches,
                    ["status"] = e.Session.Status,
                },
                e.Session.LastFailedContributionUtc ??
                DateTimeOffset.UtcNow);
        }
    }

    private void ForgeContributionCoordinator_OnRevisionPublished(
        object? sender,
        ForgeContributionRevisionPublishedEventArgs e)
    {
        this.addonRuntimeCoordinator.PublishGlobalEvent(
            "forge.revision_published",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scope"] = "installation",
                ["revision"] = e.Revision,
            },
            DateTimeOffset.UtcNow);

        if (e.Revision > this.NavigationData.AuthorityRevision)
        {
            _ = this.QueueNavigationDataUpdateCheck();
        }
    }

    private void SocialCoordinator_OnSnapshotRefreshed(
        object? sender,
        SocialSnapshotRefreshedEventArgs e)
    {
        if (!e.Previous.RefreshedAtUtc.HasValue)
        {
            return;
        }

        var now = e.Current.RefreshedAtUtc ?? DateTimeOffset.UtcNow;
        var previous = e.Previous.Presence
            .GroupBy(
                item => item.PilotName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);
        var current = e.Current.Presence
            .GroupBy(
                item => item.PilotName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var pair in current)
        {
            var currentFreshness = this.socialCoordinator.GetFreshness(
                pair.Value.UpdatedAtUtc,
                now);
            var wasOnline = previous.TryGetValue(
                    pair.Key,
                    out var previousRecord) &&
                this.socialCoordinator.GetFreshness(
                    previousRecord.UpdatedAtUtc,
                    e.Previous.RefreshedAtUtc) ==
                SocialPresenceFreshness.Online;

            if (!wasOnline &&
                currentFreshness == SocialPresenceFreshness.Online)
            {
                this.PublishSocialPresenceEvent(
                    "social.pilot_online",
                    pair.Value,
                    currentFreshness,
                    now);
            }
        }

        foreach (var pair in previous)
        {
            var wasOnline = this.socialCoordinator.GetFreshness(
                    pair.Value.UpdatedAtUtc,
                    e.Previous.RefreshedAtUtc) ==
                SocialPresenceFreshness.Online;
            var isOnline = current.TryGetValue(
                    pair.Key,
                    out var currentRecord) &&
                this.socialCoordinator.GetFreshness(
                    currentRecord.UpdatedAtUtc,
                    now) == SocialPresenceFreshness.Online;

            if (wasOnline && !isOnline)
            {
                this.PublishSocialPresenceEvent(
                    "social.pilot_offline",
                    currentRecord ?? pair.Value,
                    currentRecord == null
                        ? SocialPresenceFreshness.Offline
                        : this.socialCoordinator.GetFreshness(
                            currentRecord.UpdatedAtUtc,
                            now),
                    now);
            }
        }
    }

    private void PublishSocialPresenceEvent(
        string eventName,
        SocialPresenceRecord record,
        SocialPresenceFreshness freshness,
        DateTimeOffset occurredAt)
    {
        // Presence events intentionally contain no Atlas coordinates, sector,
        // station or nearest-navigation details. Addons may react to the
        // public online lifecycle without receiving another pilot's position.
        this.addonRuntimeCoordinator.PublishGlobalEvent(
            eventName,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scope"] = "installation",
                ["pilot_name"] = record.PilotName,
                ["freshness"] = NormalizeAddonEnum(freshness),
                ["presence_updated_at"] = record.UpdatedAtUtc
                    .ToUnixTimeMilliseconds(),
            },
            occurredAt);
    }

    private static long GetSubmittedFactCount(
        ForgeContributionStatisticsSnapshot statistics)
    {
        return statistics.NpcFactsSubmitted +
               statistics.StationFacilityFactsSubmitted +
               statistics.NavigationObjectFactsSubmitted +
               statistics.VendorItemFactsSubmitted +
               statistics.MobSightingFactsSubmitted +
               statistics.MobLootFactsSubmitted +
               statistics.HarvestableFactsSubmitted +
               statistics.ProductionRecipeFactsSubmitted +
               statistics.MissionFactsSubmitted +
               statistics.JobOfferFactsSubmitted;
    }

    private static long GetAcceptedEvidenceCount(
        ForgeContributionStatisticsSnapshot statistics)
    {
        return statistics.EvidenceAccepted +
               statistics.StationFacilityEvidenceAccepted +
               statistics.NavigationObjectEvidenceAccepted +
               statistics.VendorItemEvidenceAccepted +
               statistics.MobSightingEvidenceAccepted +
               statistics.MobLootEvidenceAccepted +
               statistics.HarvestableEvidenceAccepted +
               statistics.ProductionRecipeEvidenceAccepted +
               statistics.MissionEvidenceAccepted +
               statistics.JobOfferEvidenceAccepted;
    }

    private static string NormalizeAddonEnum<T>(T value)
        where T : struct, Enum
    {
        var text = value.ToString();
        var result = new System.Text.StringBuilder(text.Length + 8);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(character));
        }

        return result.ToString();
    }

    private void NavigationRouteCoordinator_OnRouteChanged(
        object? sender,
        NavigationRouteChangedEventArgs e)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(
                e.Snapshot.ProcessId,
                out client);
        }

        if (client == null)
        {
            return;
        }

        var observation = this.clientObservationCoordinator
            .GetSnapshots()
            .FirstOrDefault(snapshot =>
                snapshot.ProcessId == e.Snapshot.ProcessId);

        if (observation == null)
        {
            return;
        }

        this.addonRuntimeCoordinator.UpdateOwnerSnapshot(
            observation,
            this.BuildAddonOwnerRegistration(client),
            this.BuildAddonNavigationRouteSnapshot(e.Snapshot, observation));
    }

    private void NavigationAutoPilotCoordinator_OnStateChanged(
        object? sender,
        NavigationAutoPilotStateChangedEventArgs e)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(
                e.Snapshot.ProcessId,
                out client);
        }

        if (client == null ||
            !this.clientObservationCoordinator.TryGetSnapshot(
                e.Snapshot.ProcessId,
                out var observation))
        {
            return;
        }

        var route = this.NavigationRoutes.GetSnapshot(
            e.Snapshot.ProcessId);

        this.addonRuntimeCoordinator.UpdateOwnerSnapshot(
            observation,
            this.BuildAddonOwnerRegistration(client),
            this.BuildAddonNavigationRouteSnapshot(
                route,
                observation));
    }

    private AddonNavigationRouteSnapshot
        BuildAddonNavigationRouteSnapshot(
            NavigationRouteSnapshot snapshot,
            ClientObservationSnapshot observation)
    {
        var route = snapshot.Route;
        var autoPilot = this.navigationAutoPilotCoordinator
            .GetSnapshot(snapshot.ProcessId);
        var terminalGateStepWasOvertaken =
            route != null &&
            !autoPilot.IsActive &&
            (autoPilot.StopReason is
                NavigationAutoPilotStopReason.GateActivationFailed or
                NavigationAutoPilotStopReason.SectorTransitionTimedOut or
                NavigationAutoPilotStopReason.WormholeActivationFailed or
                NavigationAutoPilotStopReason.WormholeTransitionTimedOut) &&
            !string.IsNullOrWhiteSpace(
                autoPilot.ExpectedSectorKey) &&
            string.Equals(
                autoPilot.ExpectedSectorKey,
                route.Current.Key,
                StringComparison.Ordinal);
        var autoPilotApplies =
            autoPilot.State != NavigationAutoPilotState.Inactive &&
            (autoPilot.IsActive ||
             (!terminalGateStepWasOvertaken &&
              (route == null ||
               autoPilot.RouteId == route.RouteId) &&
              (route == null ||
               autoPilot.StopReason ==
                   NavigationAutoPilotStopReason.DestinationReached ||
               AutoPilotExpectedTargetMatchesStep(
                   autoPilot,
                   route.NextStep))));
        var autoPilotRunning = autoPilot.IsActive;
        var autoPilotActive =
            autoPilotApplies &&
            autoPilotRunning;
        var hasStableSpaceContext =
            snapshot.IsAvailable &&
            observation is { IsAvailable: true, LifecycleState: ClientLifecycleState.InGame, LoadingOrTransitionFlag: 0, World: { IsAvailable: true, Environment: ClientWorldEnvironment.Space, ActiveSectorNumber: not 0 } };
        var runtime = observation.LocalPlayer.Operational.Runtime;
        var isObservedWarping =
            observation.LocalPlayer.IsAvailable &&
            observation.LocalPlayer.Operational.IsAvailable &&
            runtime.PrivateWarpState.HasValue &&
            runtime.GlobalWarpState.HasValue &&
            runtime.BlocksAutoPilotStart;
        var supportsCurrentAutoPilotStep =
            NavigationAutoPilotCoordinator.SupportsStep(
                route?.NextStep);
        var isManualFinalLeg =
            route?.NextStep?.Kind ==
                NavigationRouteStepKind.FinalTarget &&
            !supportsCurrentAutoPilotStep;
        var canStartAutoPilot =
            hasStableSpaceContext &&
            !isObservedWarping &&
            !autoPilotRunning &&
            supportsCurrentAutoPilotStep;
        var canResumeAutoPilot =
            canStartAutoPilot &&
            autoPilotApplies &&
            autoPilot.State == NavigationAutoPilotState.Stopped;
        var canSelectNextTarget =
            hasStableSpaceContext &&
            !autoPilotRunning &&
            route?.NextStep != null &&
            route.NextStep.Kind !=
                NavigationRouteStepKind.WormholeTransition;
        var destinationArrivalConfirmed =
            route != null &&
            (snapshot.Status ==
                 NavigationRouteStatus.DestinationReached ||
             (autoPilot.RouteId == route.RouteId &&
              autoPilot.State == NavigationAutoPilotState.Arrived));
        var canPlanReturnTrip =
            route != null &&
            !autoPilotRunning &&
            destinationArrivalConfirmed &&
            NavigationDestinationsDiffer(
                route.OriginDestination,
                route.Destination);

        return new AddonNavigationRouteSnapshot
        {
            IsAvailable = snapshot.IsAvailable,
            Status = NormalizeNavigationRouteStatus(snapshot.Status),
            StatusText = snapshot.StatusText,
            HasRoute = snapshot.HasRoute,
            Route = route == null
                ? null
                : new AddonNavigationPlannedRouteSnapshot
                {
                    Id = route.RouteId.ToString("D"),
                    CreatedAt = route.CreatedAt,
                    UpdatedAt = route.UpdatedAt,
                    Origin = BuildAddonNavigationLocation(route.Origin),
                    OriginDestination =
                        BuildAddonNavigationDestination(
                            route.OriginDestination),
                    Current = BuildAddonNavigationLocation(route.Current),
                    Destination =
                        BuildAddonNavigationDestination(
                            route.Destination),
                    CompletedHopCount = route.CompletedHopCount,
                    RemainingHopCount = route.RemainingHopCount,
                    TotalHopCount = route.TotalHopCount,
                    NextStep = route.NextStep == null
                        ? null
                        : BuildAddonNavigationRouteStep(
                            route.NextStep),
                    Steps =
                    [
                        .. route.Steps.Select(
                            BuildAddonNavigationRouteStep),
                    ],
                    Warnings = route.Warnings,
                },
            Journey = new AddonNavigationJourneySnapshot
            {
                State = autoPilotApplies
                    ? NormalizeNavigationAutoPilotState(
                        autoPilot.State)
                    : !snapshot.HasRoute
                        ? "not_started"
                        : route == null
                            ? "suspended"
                            : snapshot.Status ==
                                NavigationRouteStatus.NoRoute
                                ? "blocked"
                                : snapshot.Status ==
                                    NavigationRouteStatus.DestinationReached
                                    ? "destination_reached"
                                    : isManualFinalLeg
                                        ? "manual_final_leg"
                                        : "planned",
                StatusText = autoPilotApplies
                    ? autoPilot.StatusText
                    : isManualFinalLeg
                        ? $"The final route target {route?.NextStep?.FinalTargetName ?? route?.Destination.DisplayName ?? "destination"} remains manual."
                        : snapshot.StatusText,
                StopReason = autoPilotApplies &&
                             autoPilot.StopReason !=
                                 NavigationAutoPilotStopReason.None
                    ? NormalizeNavigationAutoPilotStopReason(
                        autoPilot.StopReason)
                    : null,
                StartedAt = autoPilotApplies
                    ? autoPilot.StartedAt
                    : null,
                IsActive = autoPilotActive,
                ExpectedTargetName = autoPilotApplies
                    ? autoPilot.ExpectedTargetName
                    : null,
                ExpectedSectorName = autoPilotApplies
                    ? autoPilot.ExpectedSectorName
                    : null,
                CurrentEnergy = autoPilotApplies
                    ? autoPilot.CurrentEnergy
                    : null,
                RequiredEnergy = autoPilotApplies
                    ? autoPilot.RequiredEnergy
                    : null,
                CanStart = canStartAutoPilot,
                CanStop = autoPilotActive,
                CanSelectNextTarget =
                    canSelectNextTarget,
                CanPause = false,
                CanResume = canResumeAutoPilot,
                CanClear = snapshot.HasRoute &&
                           !autoPilotRunning,
                CanPlanReturnTrip = canPlanReturnTrip,
            },
        };
    }

    private static bool NavigationDestinationsDiffer(
        NavigationDestination left,
        NavigationDestination right)
    {
        if (!string.Equals(
                left.SectorKey,
                right.SectorKey,
                StringComparison.Ordinal))
        {
            return true;
        }

        return left.Kind == NavigationDestinationKind.Target &&
               right.Kind == NavigationDestinationKind.Target &&
               !string.Equals(
                   left.TargetKey,
                   right.TargetKey,
                   StringComparison.Ordinal);
    }

    private static bool AutoPilotExpectedTargetMatchesStep(
        NavigationAutoPilotSnapshot autoPilot,
        NavigationRouteStep? step)
    {
        if (step == null ||
            string.IsNullOrWhiteSpace(
                autoPilot.ExpectedTargetName))
        {
            return false;
        }

        var stepTargetName = step.Kind switch
        {
            NavigationRouteStepKind.SectorTransition =>
                step.DepartureTargetName,
            NavigationRouteStepKind.WormholeTransition =>
                step.WormholeAbilityName,
            _ => step.FinalTargetName,
        };

        return !string.IsNullOrWhiteSpace(stepTargetName) &&
            string.Equals(
                autoPilot.ExpectedTargetName,
                stepTargetName,
                StringComparison.Ordinal);
    }

    private static AddonNavigationDestinationSnapshot
        BuildAddonNavigationDestination(
            NavigationDestination destination)
    {
        return new AddonNavigationDestinationSnapshot
        {
            Kind = destination.Kind ==
                NavigationDestinationKind.Target
                    ? "target"
                    : "sector",
            Sector = new AddonNavigationLocationSnapshot
            {
                SectorKey = destination.SectorKey,
                SectorName = destination.SectorName,
                SystemName = destination.SystemName,
            },
            Target = destination is
                { Kind: NavigationDestinationKind.Target, TargetRawObjectType: not null }
                    ? new AddonNavigationTargetSnapshot
                    {
                        Key = destination.TargetKey!,
                        Name = destination.TargetName!,
                        Type = destination.TargetType!,
                        RawObjectType =
                            destination.TargetRawObjectType.Value,
                        HasPosition = destination.HasTargetPosition,
                        X = destination.TargetX,
                        Y = destination.TargetY,
                        Z = destination.TargetZ,
                    }
                    : null,
        };
    }

    private static AddonNavigationLocationSnapshot
        BuildAddonNavigationLocation(
            GalaxySectorDefinition sector)
    {
        return new AddonNavigationLocationSnapshot
        {
            SectorKey = sector.Key,
            SectorName = sector.Name,
            SystemName = sector.SystemName,
        };
    }

    private static AddonNavigationRouteStepSnapshot
        BuildAddonNavigationRouteStep(
            NavigationRouteStep step)
    {
        return new AddonNavigationRouteStepSnapshot
        {
            Number = step.Number,
            Kind = step.Kind switch
            {
                NavigationRouteStepKind.SectorTransition =>
                    "sector_transition",
                NavigationRouteStepKind.WormholeTransition =>
                    "wormhole_transition",
                _ => "final_target",
            },
            From = new AddonNavigationLocationSnapshot
            {
                SectorKey = step.FromSectorKey,
                SectorName = step.FromSectorName,
                SystemName = step.FromSystemName,
            },
            To = new AddonNavigationLocationSnapshot
            {
                SectorKey = step.ToSectorKey,
                SectorName = step.ToSectorName,
                SystemName = step.ToSystemName,
            },
            DepartureTarget = string.IsNullOrWhiteSpace(
                    step.DepartureTargetKey) ||
                string.IsNullOrWhiteSpace(
                    step.DepartureTargetName) ||
                string.IsNullOrWhiteSpace(
                    step.DepartureTargetType) ||
                !step.DepartureTargetRawObjectType.HasValue
                ? null
                : new AddonNavigationTargetSnapshot
                {
                    Key = step.DepartureTargetKey,
                    Name = step.DepartureTargetName,
                    Type = step.DepartureTargetType,
                    RawObjectType = step
                        .DepartureTargetRawObjectType.Value,
                    HasPosition =
                        step.HasDepartureTargetPosition,
                    X = step.DepartureTargetX,
                    Y = step.DepartureTargetY,
                    Z = step.DepartureTargetZ,
                },
            Wormhole = step.Kind !=
                    NavigationRouteStepKind.WormholeTransition ||
                string.IsNullOrWhiteSpace(
                    step.WormholeSkillFamilyName) ||
                string.IsNullOrWhiteSpace(
                    step.WormholeAbilityName)
                ? null
                : new AddonNavigationWormholeSnapshot
                {
                    SkillFamilyName =
                        step.WormholeSkillFamilyName,
                    AbilityName = step.WormholeAbilityName,
                    RequiredRank = step.WormholeRequiredRank,
                    HasReadyCaster =
                        step.WormholeHasReadyCaster,
                    CasterNames = step.WormholeCasterNames,
                },
            FinalTarget = string.IsNullOrWhiteSpace(
                    step.FinalTargetKey) ||
                string.IsNullOrWhiteSpace(
                    step.FinalTargetName) ||
                string.IsNullOrWhiteSpace(
                    step.FinalTargetType) ||
                !step.FinalTargetRawObjectType.HasValue
                ? null
                : new AddonNavigationTargetSnapshot
                {
                    Key = step.FinalTargetKey,
                    Name = step.FinalTargetName,
                    Type = step.FinalTargetType,
                    RawObjectType = step
                        .FinalTargetRawObjectType.Value,
                    HasPosition = step.HasFinalTargetPosition,
                    X = step.FinalTargetX,
                    Y = step.FinalTargetY,
                    Z = step.FinalTargetZ,
                },
            AccessRequirement = string.IsNullOrWhiteSpace(
                    step.AccessRequirement)
                ? null
                : step.AccessRequirement,
        };
    }

    private static string NormalizeNavigationRouteStatus(
        NavigationRouteStatus status)
    {
        if (status == NavigationRouteStatus.Ready)
        {
            return "ready";
        }

        if (status == NavigationRouteStatus.Planned)
        {
            return "planned";
        }

        if (status == NavigationRouteStatus.DestinationReached)
        {
            return "destination_reached";
        }

        if (status == NavigationRouteStatus.NoRoute)
        {
            return "no_route";
        }

        return "unavailable";
    }

    private static string NormalizeNavigationAutoPilotState(
        NavigationAutoPilotState state)
    {
        return state switch
        {
            NavigationAutoPilotState.Inactive => "inactive",
            NavigationAutoPilotState.Starting => "starting",
            NavigationAutoPilotState.SelectingTarget =>
                "selecting_target",
            NavigationAutoPilotState.WaitingForWarp =>
                "waiting_for_warp",
            NavigationAutoPilotState.WaitingForEnergy =>
                "waiting_for_energy",
            NavigationAutoPilotState.EngagingWarp =>
                "engaging_warp",
            NavigationAutoPilotState.Warping => "warping",
            NavigationAutoPilotState.VerifyingArrival =>
                "verifying_arrival",
            NavigationAutoPilotState.ActivatingGate =>
                "activating_gate",
            NavigationAutoPilotState.ActivatingWormhole =>
                "activating_wormhole",
            NavigationAutoPilotState.WaitingForSector =>
                "waiting_for_sector",
            NavigationAutoPilotState.ActivatingDestination =>
                "activating_destination",
            NavigationAutoPilotState.WaitingForDestination =>
                "waiting_for_destination",
            NavigationAutoPilotState.ManualFinalLeg =>
                "manual_final_leg",
            NavigationAutoPilotState.Arrived => "arrived",
            NavigationAutoPilotState.Stopped => "stopped",
            _ => "unknown",
        };
    }

    private static string NormalizeNavigationAutoPilotStopReason(
        NavigationAutoPilotStopReason reason)
    {
        return reason switch
        {
            NavigationAutoPilotStopReason.UserStopped =>
                "user_stopped",
            NavigationAutoPilotStopReason.DestinationReached =>
                "destination_reached",
            NavigationAutoPilotStopReason.TargetSelectionFailed =>
                "target_selection_failed",
            NavigationAutoPilotStopReason.WarpUnavailable =>
                "warp_unavailable",
            NavigationAutoPilotStopReason.WarpDidNotEngage =>
                "warp_did_not_engage",
            NavigationAutoPilotStopReason.WarpInterrupted =>
                "warp_interrupted",
            NavigationAutoPilotStopReason.GateUnavailable =>
                "gate_unavailable",
            NavigationAutoPilotStopReason.GateActivationFailed =>
                "gate_activation_failed",
            NavigationAutoPilotStopReason.WormholeUnavailable =>
                "wormhole_unavailable",
            NavigationAutoPilotStopReason.WormholeActivationFailed =>
                "wormhole_activation_failed",
            NavigationAutoPilotStopReason.WormholeTransitionTimedOut =>
                "wormhole_transition_timed_out",
            NavigationAutoPilotStopReason.SectorTransitionTimedOut =>
                "sector_transition_timed_out",
            NavigationAutoPilotStopReason.DestinationUnavailable =>
                "destination_unavailable",
            NavigationAutoPilotStopReason.DestinationActivationFailed =>
                "destination_activation_failed",
            NavigationAutoPilotStopReason.DestinationTransitionTimedOut =>
                "destination_transition_timed_out",
            NavigationAutoPilotStopReason.UnexpectedSector =>
                "unexpected_sector",
            NavigationAutoPilotStopReason.RouteChanged =>
                "route_changed",
            NavigationAutoPilotStopReason.ObservationUnavailable =>
                "observation_unavailable",
            NavigationAutoPilotStopReason.ClientUnavailable =>
                "client_unavailable",
            NavigationAutoPilotStopReason.EnergyUnavailable =>
                "energy_unavailable",
            NavigationAutoPilotStopReason.InternalError =>
                "internal_error",
            _ => "none",
        };
    }

    private static void UpdateAutoLoginProvenance(
        ClientInstance client,
        ClientObservationSnapshot snapshot)
    {
        var provenance = client.AutoLoginProvenance;

        if (snapshot.LifecycleState is
            ClientLifecycleState.CharacterSelection or
            ClientLifecycleState.InGame)
        {
            if (provenance is { IsConfirmed: false })
            {
                client.AutoLoginProvenance = provenance with
                {
                    ConfirmedAt = snapshot.ObservedAt,
                };
            }

            return;
        }

        if (snapshot.LifecycleState ==
                ClientLifecycleState.LoginScreen &&
            provenance is { IsConfirmed: true })
        {
            client.AutoLoginProvenance = null;
            return;
        }

        if (snapshot.LifecycleState is
            ClientLifecycleState.ApplicationStarted or
            ClientLifecycleState.IntroScene)
        {
            client.AutoLoginProvenance = null;
        }
    }

    private static void UpdateLiveCharacterSession(
        ClientInstance client,
        ClientObservationSnapshot snapshot)
    {
        var resolvedIdentity =
            ClientLiveCharacterIdentityResolver.Resolve(snapshot);

        if (snapshot.LifecycleState ==
            ClientLifecycleState.InGame)
        {
            client.InGameSince ??= snapshot.ObservedAt;

            if (resolvedIdentity.IsAvailable ||
                snapshot.LoadingOrTransitionFlag == 0)
            {
                client.LiveCharacterIdentity = resolvedIdentity;
            }

            return;
        }

        if (snapshot.LifecycleState is
            ClientLifecycleState.ApplicationStarted or
            ClientLifecycleState.IntroScene or
            ClientLifecycleState.LoginScreen or
            ClientLifecycleState.CharacterSelection)
        {
            client.InGameSince = null;
            client.LiveCharacterIdentity = resolvedIdentity;
            return;
        }

        if (client.InGameSince == null)
        {
            client.LiveCharacterIdentity = resolvedIdentity;
        }
    }

    private static HostedTitleState CaptureHostedTitleState(
        ClientInstance client)
    {
        var accountLogin = client.AutoLoginProvenance is
        { IsConfirmed: true } provenance
            ? provenance.LoginName
            : null;

        return new HostedTitleState(
            accountLogin,
            client.InGameSince,
            client.LiveCharacterIdentity.Name,
            client.LiveCharacterIdentity.Profession);
    }

    private sealed record HostedTitleState(
        string? AccountLogin,
        DateTimeOffset? InGameSince,
        string? CharacterName,
        string? Profession);

    private void ClientObservationCoordinator_OnChatMessageObserved(
        object? sender,
        ClientChatMessageObservedEventArgs e)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(
                e.Message.ProcessId,
                out client);
        }

        if (client == null)
        {
            return;
        }

        this.forgeContributionCoordinator.Observe(e.Message);

        this.addonRuntimeCoordinator.PublishChatMessage(
            e.Message);
    }

    private void KillClientProcess(ClientInstance client)
    {
        try
        {
            if (client.Process.HasExited)
            {
                return;
            }

            client.Process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (Exception)
        {
            // Later: write to app log.
        }
    }

    private void AutoAssignSlotsIfEnabled()
    {
        if (!this.settings.AutoAssignNewClients)
        {
            return;
        }

        this.ReconcileClientAssignments();
        this.SaveSettings();
    }

    private void ReconcileClientAssignments()
    {
        lock (this.lockObject)
        {
            var activeProfile = this.ActiveProfile;
            var activeSlots = activeProfile?.Slots ?? [];
            var assignedSlotIds = new HashSet<Guid>();

            // An exact slot launch reserves its destination as soon as the
            // launcher starts. This prevents an unrelated newly attached
            // client from winning the slot during the launcher handoff.
            if (this.pendingManagedClientLaunch is
                {
                    Kind: ManagedClientLaunchKind.ProfileSlot,
                    ProfileId: { } pendingProfileId,
                    TargetSlotId: { } pendingSlotId,
                } &&
                activeProfile?.Id == pendingProfileId &&
                activeSlots.Exists(slot => slot.Id == pendingSlotId))
            {
                assignedSlotIds.Add(pendingSlotId);
            }

            // First pass: preserve valid unique assignments from the active
            // profile. Explicit exact-slot clients win any unexpected collision
            // over clients that arrived through ordinary auto-assignment.
            foreach (var client in this.clients.Values
                         .OrderByDescending(candidate =>
                             candidate.ManagedLaunchRequest is
                             {
                                 Kind: ManagedClientLaunchKind.ProfileSlot,
                                 ProfileId: { } requestProfileId,
                                 TargetSlotId: { } requestSlotId,
                             } &&
                             activeProfile?.Id == requestProfileId &&
                             candidate.AssignedSlotId == requestSlotId)
                         .ThenBy(candidate =>
                             candidate.StartedByManagerAt ??
                             DateTimeOffset.MaxValue)
                         .ThenBy(candidate => candidate.ProcessId))
            {
                if (client.AssignedSlotId is not { } assignedSlotId)
                {
                    continue;
                }

                if (!activeSlots.Exists(slot => slot.Id == assignedSlotId) ||
                    !assignedSlotIds.Add(assignedSlotId))
                {
                    client.AssignedSlotId = null;
                }
            }

            // Second pass: assign only clients that permit automatic profile
            // placement. Generic and Pilot Archive launches deliberately stay
            // unassigned even when a profile has free slots.
            foreach (var client in this.clients.Values
                         .Where(client =>
                             client.AllowProfileAutoAssignment &&
                             client.AssignedSlotId == null)
                         .OrderBy(client =>
                             client.StartedByManagerAt ??
                             DateTimeOffset.MaxValue)
                         .ThenBy(client => client.ProcessId))
            {
                var nextSlot = activeSlots.FirstOrDefault(
                    slot => !assignedSlotIds.Contains(slot.Id));

                if (nextSlot == null)
                {
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                client.AssignedSlotId = nextSlot.Id;
                assignedSlotIds.Add(nextSlot.Id);
            }

            // Third pass: apply active-profile placements. Unassigned clients
            // retain the natural bounds of the game window that was hosted.
            foreach (var client in this.clients.Values)
            {
                if (client.AssignedSlotId is not { } assignedSlotId)
                {
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                var slot = activeSlots.FirstOrDefault(
                    candidate => candidate.Id == assignedSlotId);

                if (slot == null)
                {
                    client.AssignedSlotId = null;
                    client.HostForm?.SetUnassignedTitle();
                    continue;
                }

                client.HostForm?.ApplySlot(slot);
            }
        }
    }

    private void OpenGalaxyAtlas(ClientInstance client)
    {
        this.GalaxyAtlasRequested?.Invoke(
            this,
            new GalaxyAtlasRequestedEventArgs(
                client.ProcessId));
    }

    private void OpenWorldFind(
        ClientInstance client,
        string? query = null)
    {
        this.WorldFindRequested?.Invoke(
            this,
            new WorldFindRequestedEventArgs(
                client.ProcessId,
                query));
    }

    private void OpenForgeContributions(
        ClientInstance client,
        IWin32Window owner)
    {
        _ = client;
        this.OpenForgeContributionsCore(owner, tourClosed: null);
    }

    internal void OpenForgeContributionsForHelp(
        IWin32Window owner,
        Action? tourClosed)
    {
        this.OpenForgeContributionsCore(owner, tourClosed);
    }

    private void OpenForgeContributionsCore(
        IWin32Window owner,
        Action? tourClosed)
    {
        ForgeContributionsForm form;

        if (this.forgeContributionsForm is
            {
                IsDisposed: false,
                Disposing: false,
            } existing)
        {
            form = existing;

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            if (!form.Visible)
            {
                form.Show(owner);
            }

            form.BringToFront();
            form.Activate();
        }
        else
        {
            form = new ForgeContributionsForm(this, owner);
            this.forgeContributionsForm = form;
            form.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(this.forgeContributionsForm, form))
                {
                    this.forgeContributionsForm = null;
                }
            };
            form.Show(owner);
        }

        if (tourClosed != null)
        {
            form.BeginInvoke(() => form.ShowHelpTour(tourClosed));
        }
    }

    private void OpenSocial(
        ClientInstance client,
        IWin32Window owner)
    {
        if (client.LifecycleState != ClientLifecycleState.InGame)
        {
            return;
        }

        if (this.socialForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            if (this.socialForm.WindowState ==
                FormWindowState.Minimized)
            {
                this.socialForm.WindowState =
                    FormWindowState.Normal;
            }

            if (!this.socialForm.Visible)
            {
                this.socialForm.Show(owner);
            }

            this.socialForm.SelectPilot(client.ProcessId);
            this.socialForm.BringToFront();
            this.socialForm.Activate();
            return;
        }

        var form = new SocialForm(this, client.ProcessId);
        this.socialForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(this.socialForm, form))
            {
                this.socialForm = null;
            }
        };
        form.Show(owner);
        form.SelectPilot(client.ProcessId);
    }

    public bool OpenSocialForHelp(
        int processId,
        IWin32Window owner)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(processId, out client);
        }

        if (client == null ||
            client.LifecycleState != ClientLifecycleState.InGame)
        {
            return false;
        }

        this.OpenSocial(client, owner);
        var form = this.socialForm;
        form?.BeginInvoke(() => form.ShowHelpTour());
        return true;
    }

    private void OpenPilotArchive(
        ClientInstance client,
        IWin32Window owner)
    {
        this.OpenPilotArchive(
            owner,
            client.LiveCharacterIdentity.CharacterObjectId);
    }

    public void OpenPilotArchive(
        IWin32Window owner,
        uint? characterId = null)
    {
        if (this.pilotArchiveForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            if (this.pilotArchiveForm.WindowState ==
                FormWindowState.Minimized)
            {
                this.pilotArchiveForm.WindowState =
                    FormWindowState.Normal;
            }

            if (!this.pilotArchiveForm.Visible)
            {
                this.pilotArchiveForm.Show();
            }

            if (characterId.HasValue)
            {
                this.pilotArchiveForm.SelectPilot(characterId.Value);
            }

            this.pilotArchiveForm.BringToFront();
            this.pilotArchiveForm.Activate();
            return;
        }

        var form = new PilotArchiveForm(this, owner);
        this.pilotArchiveForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(this.pilotArchiveForm, form))
            {
                this.pilotArchiveForm = null;
            }
        };
        form.Show();

        if (characterId.HasValue)
        {
            form.SelectPilot(characterId.Value);
        }
    }

    public void OpenPilotArchiveForHelp(
        IWin32Window owner,
        uint? characterId = null)
    {
        this.OpenPilotArchive(owner, characterId);
        var form = this.pilotArchiveForm;
        form?.BeginInvoke(() => form.ShowHelpTour());
    }

    public string? GetPilotArchiveActiveBuildDisplayName(
        uint characterId) =>
        this.skillBuildLocalWorkspace.GetActiveBuildDisplayName(characterId);

    public bool OpenPilotArchiveBuilds(
        uint characterId,
        IWin32Window owner,
        out string status)
    {
        var details = this.pilotArchiveStore.GetPilot(characterId);

        if (details == null)
        {
            status = "The archived pilot could not be found.";
            return false;
        }

        var presentation =
            this.pilotArchiveBuildPresentationBuilder.Build(details);

        this.EnsurePilotArchiveBuildBoardForm();
        var form = this.pilotArchiveBuildBoardForm!;
        form.SetPresentation(presentation);
        form.RestorePlacement(ResolveOwnerBounds(owner));

        if (form.WindowState == FormWindowState.Minimized)
        {
            form.WindowState = FormWindowState.Normal;
        }

        if (!form.Visible)
        {
            // Keep the archive Build Board independent. It must remain usable
            // without a game host and must never inherit the game's z-order.
            form.Show();
        }

        form.BringToFront();
        form.Activate();

        status = "";
        return true;
    }

    private void EnsurePilotArchiveBuildBoardForm()
    {
        if (this.pilotArchiveBuildBoardForm is
            {
                IsDisposed: false,
                Disposing: false,
            })
        {
            return;
        }

        var form = new SkillBuildBoardForm(
            this.skillBuildLocalWorkspace,
            this.forgeContributionCoordinator,
            this.GetArchiveBuildWindowPlacement,
            this.SaveArchiveBuildWindowPlacement,
            (itemTemplateId, size) =>
                this.GetPilotArchiveItemIcon(
                    this.LocateGameOutputDirectory(),
                    itemTemplateId,
                    size));

        this.pilotArchiveBuildBoardForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(this.pilotArchiveBuildBoardForm, form))
            {
                this.pilotArchiveBuildBoardForm = null;
            }
        };
    }

    private AddonWindowPlacement? GetArchiveBuildWindowPlacement(
        string addonId,
        string widgetId)
    {
        lock (this.lockObject)
        {
            var placement = this.settings.AddonCenter
                .UnassignedAddonWindowPlacements
                .LastOrDefault(candidate =>
                    string.Equals(
                        candidate.AddonId,
                        addonId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.WidgetId,
                        widgetId,
                        StringComparison.Ordinal));

            return placement == null
                ? null
                : new AddonWindowPlacement
                {
                    AddonId = placement.AddonId,
                    WidgetId = placement.WidgetId,
                    OffsetX = placement.OffsetX,
                    OffsetY = placement.OffsetY,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsClosed = placement.IsClosed,
                    IsVisible = placement.IsVisible,
                    IsMinimized = placement.IsMinimized,
                    IsMaximized = placement.IsMaximized,
                    HorizontalEdge = placement.HorizontalEdge,
                    MinimizedOffsetX = placement.MinimizedOffsetX,
                    MinimizedOffsetY = placement.MinimizedOffsetY,
                };
        }
    }

    private void SaveArchiveBuildWindowPlacement(
        string addonId,
        string widgetId,
        AddonWindowPlacement placement)
    {
        var changed = false;

        lock (this.lockObject)
        {
            var placements = this.settings.AddonCenter
                .UnassignedAddonWindowPlacements;
            var existing = placements.LastOrDefault(candidate =>
                string.Equals(
                    candidate.AddonId,
                    addonId,
                    StringComparison.Ordinal) &&
                string.Equals(
                    candidate.WidgetId,
                    widgetId,
                    StringComparison.Ordinal));

            if (existing == null)
            {
                placements.Add(new AddonWindowPlacement
                {
                    AddonId = addonId,
                    WidgetId = widgetId,
                    OffsetX = placement.OffsetX,
                    OffsetY = placement.OffsetY,
                    Width = placement.Width,
                    Height = placement.Height,
                    IsClosed = placement.IsClosed,
                    IsVisible = placement.IsVisible,
                    IsMinimized = placement.IsMinimized,
                    IsMaximized = placement.IsMaximized,
                    HorizontalEdge = placement.HorizontalEdge,
                    MinimizedOffsetX = placement.MinimizedOffsetX,
                    MinimizedOffsetY = placement.MinimizedOffsetY,
                });
                changed = true;
            }
            else if (existing.OffsetX != placement.OffsetX ||
                     existing.OffsetY != placement.OffsetY ||
                     existing.Width != placement.Width ||
                     existing.Height != placement.Height ||
                     existing.IsClosed != placement.IsClosed ||
                     existing.IsVisible != placement.IsVisible ||
                     existing.IsMinimized != placement.IsMinimized ||
                     existing.IsMaximized != placement.IsMaximized ||
                     existing.HorizontalEdge != placement.HorizontalEdge ||
                     existing.MinimizedOffsetX != placement.MinimizedOffsetX ||
                     existing.MinimizedOffsetY != placement.MinimizedOffsetY)
            {
                existing.OffsetX = placement.OffsetX;
                existing.OffsetY = placement.OffsetY;
                existing.Width = placement.Width;
                existing.Height = placement.Height;
                existing.IsClosed = placement.IsClosed;
                existing.IsVisible = placement.IsVisible;
                existing.IsMinimized = placement.IsMinimized;
                existing.IsMaximized = placement.IsMaximized;
                existing.HorizontalEdge = placement.HorizontalEdge;
                existing.MinimizedOffsetX = placement.MinimizedOffsetX;
                existing.MinimizedOffsetY = placement.MinimizedOffsetY;
                changed = true;
            }
        }

        if (changed)
        {
            this.SaveSettings();
        }
    }

    private static Rectangle ResolveOwnerBounds(IWin32Window owner)
    {
        if (owner is Form form &&
            !form.IsDisposed &&
            !form.Disposing)
        {
            return form.Bounds;
        }

        if (owner.Handle != IntPtr.Zero)
        {
            return Screen.FromHandle(owner.Handle).WorkingArea;
        }

        return Screen.PrimaryScreen?.WorkingArea ??
            new Rectangle(100, 100, 1280, 900);
    }

    public (
        bool ShowButton,
        bool CanStart,
        string ButtonText,
        string Status)
        GetPilotArchiveLaunchStatus(uint characterId)
    {
        var pilot = this.pilotArchiveStore.GetPilot(characterId)?.Pilot;

        if (pilot == null)
        {
            return (
                false,
                false,
                "",
                "The archived character could not be found.");
        }

        var matches = this.FindConfiguredCharacterMatches(pilot.Name);

        if (matches.Count == 0)
        {
            return (
                false,
                false,
                "",
                "No configured account character has this live-observed name.");
        }

        if (matches.Count > 1)
        {
            return (
                false,
                false,
                "",
                "More than one configured character has this name; resolve the duplicate configuration first.");
        }

        var match = matches[0];
        var running = this.FindRunningConfiguredCharacterClient(
            characterId,
            match);

        if (running != null)
        {
            return (
                true,
                true,
                "Focus",
                "Character is already running; focus its client.");
        }

        var configuredClient = this.FindPreGameConfiguredClient(match);

        if (configuredClient != null)
        {
            return (
                true,
                true,
                "Focus",
                "This configured character is already starting; focus its client.");
        }

        var onlinePilotName = this.FindOnlinePilotNameForAccount(
            match.Account.Id);

        if (onlinePilotName != null)
        {
            return (
                true,
                false,
                "Start",
                BuildAccountAlreadyOnlineStatus(onlinePilotName));
        }

        if (this.IsManagedClientLaunchInProgress)
        {
            return (
                true,
                false,
                "Start",
                "Another managed client is currently starting.");
        }

        return (
            true,
            true,
            "Start",
            "Start this configured character without assigning a profile slot.");
    }

    public bool StartPilotArchiveCharacter(
        uint characterId,
        IWin32Window owner,
        out string status)
    {
        var pilot = this.pilotArchiveStore.GetPilot(characterId)?.Pilot;

        if (pilot == null)
        {
            status = "The archived character could not be found.";
            return false;
        }

        var matches = this.FindConfiguredCharacterMatches(pilot.Name);

        if (matches.Count != 1)
        {
            status = matches.Count == 0
                ? "No configured account character has this live-observed name."
                : "More than one configured character has this name.";
            return false;
        }

        var match = matches[0];
        var running = this.FindRunningConfiguredCharacterClient(
            characterId,
            match);

        if (running != null)
        {
            FocusClient(running);
            status = "Focused the already running character.";
            return true;
        }

        var configuredClient = this.FindPreGameConfiguredClient(match);

        if (configuredClient != null)
        {
            FocusClient(configuredClient);
            status =
                "Focused the client already starting this configured character.";
            return true;
        }

        var onlinePilotName = this.FindOnlinePilotNameForAccount(
            match.Account.Id);

        if (onlinePilotName != null)
        {
            status = BuildAccountAlreadyOnlineStatus(onlinePilotName);
            return false;
        }

        if (this.IsManagedClientLaunchInProgress)
        {
            status = "Another managed client is currently starting.";
            return false;
        }

        var (hostResolution, gameResolution) =
            this.ResolveQuickLaunchResolutions();
        var request = new ManagedClientLaunchRequest
        {
            Kind = ManagedClientLaunchKind.PilotArchive,
            PlacementPolicy = ManagedClientPlacementPolicy.NativeWindow,
            AccountId = match.Account.Id,
            CharacterId = match.Character.Id,
            SkipIntro = true,
            AutoLogin = true,
            AutoEnterGame = true,
            HostWidth = hostResolution.Width,
            HostHeight = hostResolution.Height,
            GameResolutionWidth = gameResolution.Width,
            GameResolutionHeight = gameResolution.Height,
            DisplayName = string.Concat("Pilot Archive · ", pilot.Name),
        };

        if (!this.StartClientFromLauncher(owner, request))
        {
            status = "The Net7 launcher was not started.";
            return false;
        }

        status = string.Concat(
            "Starting ",
            pilot.Name,
            " through ",
            UiObfuscationMode.AccountName(match.Account.ToString()),
            ".");
        return true;
    }

    private ClientInstance? FindRunningConfiguredCharacterClient(
        uint archiveCharacterId,
        ConfiguredCharacterMatch match)
    {
        return this.Clients.FirstOrDefault(client =>
        {
            if (client.State is ClientState.Closing or ClientState.Stopped)
            {
                return false;
            }

            if (client.LiveCharacterIdentity.CharacterObjectId ==
                archiveCharacterId)
            {
                return true;
            }

            return client.LifecycleState == ClientLifecycleState.InGame &&
                   this.ResolveConfiguredAccountId(client) ==
                   match.Account.Id &&
                   string.Equals(
                       client.LiveCharacterIdentity.Name?.Trim(),
                       match.Character.Name.Trim(),
                       StringComparison.OrdinalIgnoreCase);
        });
    }

    private ClientInstance? FindPreGameConfiguredClient(
        ConfiguredCharacterMatch match)
    {
        return this.Clients.FirstOrDefault(client =>
        {
            if (client.State is ClientState.Closing or ClientState.Stopped ||
                client.LiveCharacterIdentity.CharacterObjectId.HasValue)
            {
                return false;
            }

            var launch = this.ResolveClientLaunchRequest(client);
            return launch?.AccountId == match.Account.Id &&
                   launch.CharacterId == match.Character.Id;
        });
    }

    private string? FindOnlinePilotNameForAccount(Guid accountId)
    {
        foreach (var client in this.Clients)
        {
            if (client.State is ClientState.Closing or ClientState.Stopped ||
                client.LifecycleState != ClientLifecycleState.InGame ||
                this.ResolveConfiguredAccountId(client) != accountId)
            {
                continue;
            }

            var pilotName = client.LiveCharacterIdentity.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(pilotName))
            {
                return pilotName;
            }

            var launch = this.ResolveClientLaunchRequest(client);
            var configuredCharacter = this.FindConfiguredCharacter(
                launch?.AccountId,
                launch?.CharacterId);
            var configuredPilotName = configuredCharacter?.Name?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredPilotName))
            {
                return configuredPilotName;
            }

            return "a character";
        }

        return null;
    }

    private Guid? ResolveConfiguredAccountId(ClientInstance client)
    {
        var livePilotName = client.LiveCharacterIdentity.Name?.Trim();
        if (!string.IsNullOrWhiteSpace(livePilotName))
        {
            var matches = this.FindConfiguredCharacterMatches(livePilotName);
            if (matches.Count == 1)
            {
                return matches[0].Account.Id;
            }
        }

        if (client.AutoLoginProvenance is
            { IsConfirmed: true } provenance)
        {
            return provenance.AccountId;
        }

        return this.ResolveClientLaunchRequest(client)?.AccountId;
    }

    private static string BuildAccountAlreadyOnlineStatus(
        string pilotName)
    {
        return string.Concat(
            "Unavailable because ",
            pilotName,
            " is already online on this account.");
    }

    private static void FocusClient(ClientInstance client)
    {
        var form = client.HostForm;

        if (form != null)
        {
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Show();
            form.BringToFront();
            form.Activate();
            return;
        }

        var windowHandle = client.GameWindowHandle;

        if (windowHandle == IntPtr.Zero)
        {
            try
            {
                client.Process.Refresh();
                windowHandle = client.Process.MainWindowHandle;
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        if (windowHandle != IntPtr.Zero)
        {
            NativeMethods.FocusWindow(windowHandle);
        }
    }

    private List<ConfiguredCharacterMatch>
        FindConfiguredCharacterMatches(string pilotName)
    {
        return this.accounts
            .SelectMany(account => account.Characters.Select(
                character => new ConfiguredCharacterMatch(
                    account,
                    character)))
            .Where(match => string.Equals(
                match.Character.Name?.Trim(),
                pilotName.Trim(),
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private sealed record ConfiguredCharacterMatch(
        GameAccount Account,
        GameCharacter Character);

    private void RequestHelp(
        ClientInstance client,
        IWin32Window owner)
    {
        this.HelpRequested?.Invoke(
            this,
            new HelpRequestedEventArgs(
                client.ProcessId,
                owner));
    }

    private void RequestInGameOptions(
        ClientInstance client,
        IWin32Window owner)
    {
        this.InGameOptionsRequested?.Invoke(
            this,
            new InGameOptionsRequestedEventArgs(
                client.ProcessId,
                owner));
    }

    private void OpenAddonCenter(
        ClientInstance client,
        IWin32Window owner)
    {
        if (this.addonCenterForms.TryGetValue(
                client.ProcessId,
                out var existing) &&
            existing is { IsDisposed: false, Disposing: false })
        {
            if (existing.WindowState == FormWindowState.Minimized)
            {
                existing.WindowState = FormWindowState.Normal;
            }

            if (!existing.Visible)
            {
                existing.Show(owner);
            }

            existing.BringToFront();
            existing.Activate();
            return;
        }

        var form = new AddonCenterForm(
            this,
            client.ProcessId);

        this.addonCenterForms[client.ProcessId] = form;

        form.FormClosed += (_, _) =>
        {
            if (this.addonCenterForms.TryGetValue(
                    client.ProcessId,
                    out var current) &&
                ReferenceEquals(current, form))
            {
                this.addonCenterForms.Remove(client.ProcessId);
            }
        };

        form.Show(owner);
    }

    public void OpenAddonCenterForHelp(
        int ownerProcessId,
        IWin32Window owner,
        string? addonId,
        bool discover)
    {
        ClientInstance? client;

        lock (this.lockObject)
        {
            this.clients.TryGetValue(ownerProcessId, out client);
        }

        if (client == null)
        {
            return;
        }

        this.OpenAddonCenter(client, owner);

        if (this.addonCenterForms.TryGetValue(
                ownerProcessId,
                out var form) &&
            form is { IsDisposed: false, Disposing: false })
        {
            form.ShowAddonGuidance(addonId, discover);
        }
    }

    private void CloseAddonCenter(int ownerProcessId)
    {
        if (!this.addonCenterForms.Remove(
                ownerProcessId,
                out var form))
        {
            return;
        }

        if (form is { IsDisposed: false, Disposing: false })
        {
            form.Close();
        }
    }

    private VendorShoppingCompanionPresentation
        ResolveVendorShoppingCompanionPresentation(
            ClientObservationSnapshot snapshot)
    {
        if (!this.settings.WorldFind.ShowVendorCompanion)
        {
            return VendorShoppingCompanionPresentation.Hidden;
        }

        try
        {
            return this.vendorShoppingCompanionCoordinator.Build(
                snapshot,
                () =>
                {
                    var identity =
                        ClientLiveCharacterIdentityResolver.Resolve(snapshot);
                    return this.shoppingListCoordinator.BuildActivePlan(
                        identity.CharacterObjectId,
                        snapshot.ProcessId);
                });
        }
        catch (Exception exception)
        {
            Debug.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"[Shopping] Could not build the vendor companion presentation: {exception}"));
            return VendorShoppingCompanionPresentation.Hidden;
        }
    }

    private FactionDetailsPresentation ResolveFactionDetailsPresentation(
        ClientPanelPresentationObservation panelPresentation,
        ClientLocalPlayerObservation localPlayer)
    {
        return FactionDetailsPresentationBuilder.Build(
            panelPresentation,
            localPlayer,
            this.GetReputationHistory);
    }

    private NavigationDestination?
        ResolveMissionWikiDestination(
            MissionWikiLocationHint hint)
    {
        return MissionWikiLocationResolver.Resolve(
            this.NavigationData,
            hint);
    }

    private JobTerminalRoutePresentation
        ResolveJobTerminalRoutePresentation(
            int processId,
            ClientObservationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var terminal = snapshot.JobTerminal;
        var selectedDescription = terminal.SelectedDescription;

        if (snapshot.LifecycleState != ClientLifecycleState.InGame ||
            !terminal.IsAvailable ||
            !terminal.IsOpen ||
            terminal.SelectedJobId == 0)
        {
            return JobTerminalRoutePresentation.Hidden;
        }

        if (selectedDescription == null)
        {
            return new JobTerminalRoutePresentation
            {
                IsVisible = true,
                JobId = terminal.SelectedJobId,
                StatusText =
                    "Reading the selected job destination.",
            };
        }

        var destination = MissionJobDestinationResolver.Resolve(
            this.NavigationData,
            selectedDescription.Description,
            selectedDescription.Title);

        if (destination == null)
        {
            return new JobTerminalRoutePresentation
            {
                IsVisible = true,
                JobId = terminal.SelectedJobId,
                StatusText =
                    "The destination could not be identified for this job.",
            };
        }

        var preview = this.PreviewNavigationRoute(
            processId,
            destination.RouteDestination);

        if (!preview.Succeeded || preview.Plan == null)
        {
            return new JobTerminalRoutePresentation
            {
                IsVisible = true,
                JobId = terminal.SelectedJobId,
                Destination = destination.RouteDestination,
                DestinationName =
                    destination.RouteDestination.DisplayName,
                StatusText = string.IsNullOrWhiteSpace(preview.Error)
                    ? "No route is available for this destination."
                    : preview.Error,
            };
        }

        var actionableHopCount = preview.Plan.Steps.Count;

        return new JobTerminalRoutePresentation
        {
            IsVisible = true,
            JobId = terminal.SelectedJobId,
            HopCount = actionableHopCount,
            Destination = destination.RouteDestination,
            DestinationName =
                destination.RouteDestination.DisplayName,
            StatusText = string.Concat(
                actionableHopCount == 1
                    ? "1 hop to "
                    : $"{actionableHopCount} hops to ",
                destination.RouteDestination.DisplayName,
                "."),
        };
    }

    private MissionJobGuidance? ResolveMissionJobGuidance(
        int processId,
        ClientMissionObservation mission)
    {
        ArgumentNullException.ThrowIfNull(mission);

        if (!this.missionJournalCoordinator.TryGetActiveContext(
                processId,
                mission.Address,
                out var journalContext) ||
            journalContext.Source != MissionJournalSource.JobTerminal)
        {
            return null;
        }

        var objective = mission.CurrentStageText.Trim();
        var summary = mission.Summary.Trim();
        var destination =
            MissionJobDestinationResolver.Resolve(
                this.NavigationData,
                objective,
                summary);

        return new MissionJobGuidance
        {
            MissionName = mission.Name.Trim(),
            Summary = summary,
            Objective = objective,
            Stage = mission.Stage,
            StageCount = mission.StageCount,
            Reward = string.IsNullOrWhiteSpace(mission.Reward)
                ? journalContext.RewardText
                : mission.Reward.Trim(),
            JobCategory = journalContext.JobCategory,
            AcceptedAt = journalContext.AcceptedAt,
            AcceptedSystem = journalContext.AcceptedSystem,
            AcceptedSector = journalContext.AcceptedSector,
            AcceptedStarbase = journalContext.AcceptedStarbase,
            Destination = destination,
        };
    }

    private void SetMissionWikiFeatureEnabled(
        ClientInstance client,
        bool enabled,
        bool persist = true)
    {
        ArgumentNullException.ThrowIfNull(client);

        var changed = false;

        lock (this.lockObject)
        {
            var enabledAddonIds = this.GetMutableEnabledAddonIds(client);

            if (enabled)
            {
                if (!enabledAddonIds.Contains(
                        MissionWikiFeature.AddonId,
                        StringComparer.Ordinal))
                {
                    enabledAddonIds.Add(MissionWikiFeature.AddonId);
                    changed = true;
                }
            }
            else
            {
                changed = enabledAddonIds.RemoveAll(
                    addonId => string.Equals(
                        addonId,
                        MissionWikiFeature.AddonId,
                        StringComparison.Ordinal)) > 0;
            }
        }

        if (changed && persist)
        {
            this.SaveSettings();
        }

        client.HostForm?.SetMissionWikiEnabled(
            enabled && !this.AddonsSuspendedForSession);
    }

    private AddonOwnerRegistration BuildAddonOwnerRegistration(
        ClientInstance client)
    {
        var slot = this.GetAssignedSlot(client);
        var displayName = client.LiveCharacterIdentity.Name ??
            BuildNeutralClientLabel(client.ProcessId);

        return new AddonOwnerRegistration
        {
            ProcessId = client.ProcessId,
            OwnerKey = slot == null
                ? "unassigned"
                : $"slot:{slot.Id:N}",
            DisplayName = displayName,
            EnabledAddonIds = new HashSet<string>(
                this.addonsSuspendedForSession
                    ? Enumerable.Empty<string>()
                    : this.GetEnabledAddonIds(client)
                        .Where(addonId =>
                            !string.Equals(
                                addonId,
                                MissionWikiFeature.AddonId,
                                StringComparison.Ordinal)),
                StringComparer.Ordinal),
        };
    }

    private string? ResolveLauncherPath(IWin32Window owner)
    {
        if (IsValidLauncherPath(this.settings.PathToNet7Launcher))
        {
            return this.settings.PathToNet7Launcher;
        }

        using var dialog = new OpenFileDialog();
        dialog.Title = "Select LaunchNet7.exe";
        dialog.FileName = "LaunchNet7.exe";
        dialog.Filter = "Net7 Launcher|LaunchNet7.exe|Executable files|*.exe|All files|*.*";
        dialog.CheckFileExists = true;

        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }

        if (!IsValidLauncherPath(dialog.FileName))
        {
            return null;
        }

        this.settings.PathToNet7Launcher = dialog.FileName;
        this.SaveSettings();

        return dialog.FileName;
    }

    private static bool IsValidLauncherPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
               && File.Exists(path)
               && string.Equals(
                   Path.GetFileName(path),
                   "LaunchNet7.exe",
                   StringComparison.OrdinalIgnoreCase);
    }

    private void TickLauncherAutomation()
    {
        var session = this.launcherSession;

        if (session == null ||
            session.State == LauncherAutomationState.Stopped)
        {
            return;
        }

        if (session.State ==
            LauncherAutomationState.WaitingForClientProcess)
        {
            _ = this.IsWaitingForExpectedManagerStartedClient();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        using var launcherProcess = FindRunningLauncherProcess(
            session.LauncherPath,
            session.ProcessId);

        if (launcherProcess == null)
        {
            this.HandleUnavailableLauncherProcess(session, now);
            return;
        }

        int launcherProcessId;
        IntPtr windowHandle;

        try
        {
            launcherProcess.Refresh();

            if (launcherProcess.HasExited)
            {
                this.HandleUnavailableLauncherProcess(session, now);
                return;
            }

            launcherProcessId = launcherProcess.Id;
            windowHandle = launcherProcess.MainWindowHandle;
        }
        catch (Exception)
        {
            this.HandleUnavailableLauncherProcess(session, now);
            return;
        }

        if (launcherProcessId != session.ProcessId)
        {
            session.ProcessId = launcherProcessId;
            Debug.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[Launcher] Rebound singleton launcher session to PID {launcherProcessId}."));
        }

        switch (session.State)
        {
            case LauncherAutomationState.WaitingForLauncherWindow:
                if (windowHandle == IntPtr.Zero)
                {
                    if (now - session.StartedAt >= launcherWindowTimeout)
                    {
                        this.FailManagedClientLaunch(
                            "Net7 Launcher did not show its window within 30 seconds.");
                    }

                    return;
                }

                TransitionLauncherSession(
                    session,
                    LauncherAutomationState.WaitingForReadiness,
                    "Net7 Launcher window found; waiting for update checks.");
                return;

            case LauncherAutomationState.WaitingForReadiness:
                if (now - session.StartedAt >= launcherReadinessTimeout)
                {
                    this.FailManagedClientLaunch(
                        "Net7 Launcher did not finish its checks within 10 minutes.");
                    return;
                }

                if (windowHandle == IntPtr.Zero ||
                    !NativeMethods.TryObserveLauncherWindow(
                        windowHandle,
                        out var readinessObservation) ||
                    !readinessObservation.IsReady)
                {
                    session.ReadyObservedAt = null;
                    return;
                }

                session.ReadyObservedAt ??= now;

                if (now - session.ReadyObservedAt.Value <
                    launcherReadyStabilityDelay)
                {
                    return;
                }

                TransitionLauncherSession(
                    session,
                    LauncherAutomationState.ActivatingPlay,
                    string.Concat(
                        "Launcher ready at ",
                        readinessObservation.ProgressPosition.ToString(
                            CultureInfo.InvariantCulture),
                        "/",
                        readinessObservation.ProgressMaximum.ToString(
                            CultureInfo.InvariantCulture),
                        "; activating Play."));
                return;

            case LauncherAutomationState.ActivatingPlay:
                if (windowHandle == IntPtr.Zero ||
                    !NativeMethods.TryObserveLauncherWindow(
                        windowHandle,
                        out var activationObservation) ||
                    !activationObservation.IsReady)
                {
                    session.ReadyObservedAt = null;
                    TransitionLauncherSession(
                        session,
                        LauncherAutomationState.WaitingForReadiness,
                        "Launcher readiness changed before Play; observing again.");
                    return;
                }

                if (!NativeMethods.ClickLauncherPlayButton(windowHandle))
                {
                    if (now - session.StateEnteredAt >=
                        launcherPlayInvocationTimeout)
                    {
                        this.FailManagedClientLaunch(
                            "Net7 Launcher Play could not be activated within 15 seconds.");
                    }

                    return;
                }

                session.PlayAttemptCount++;
                session.PlayAttemptedAt = now;
                this.expectManagerStartedClientUntil =
                    now + managerStartedClientDetectionWindow;

                TransitionLauncherSession(
                    session,
                    LauncherAutomationState.WaitingForPlayAcceptance,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Play attempt {session.PlayAttemptCount} sent; observing launcher handoff."));
                return;

            case LauncherAutomationState.WaitingForPlayAcceptance:
                if (!this.IsWaitingForExpectedManagerStartedClient())
                {
                    return;
                }

                if (windowHandle == IntPtr.Zero ||
                    !NativeMethods.TryObserveLauncherWindow(
                        windowHandle,
                        out var acceptanceObservation) ||
                    !acceptanceObservation.HasPlayButton ||
                    !acceptanceObservation.IsPlayButtonVisible ||
                    !acceptanceObservation.IsPlayButtonEnabled)
                {
                    TransitionLauncherSession(
                        session,
                        LauncherAutomationState.WaitingForClientProcess,
                        "Net7 Launcher acknowledged Play; waiting for client.exe.");
                    return;
                }

                if (session.PlayAttemptedAt == null ||
                    now - session.PlayAttemptedAt.Value <
                    launcherPlayAcceptanceDelay)
                {
                    return;
                }

                if (session.PlayAttemptCount < LauncherPlayAttemptLimit)
                {
                    TransitionLauncherSession(
                        session,
                        LauncherAutomationState.ActivatingPlay,
                        "Play showed no handoff after ten seconds; retrying once.");
                    return;
                }

                TransitionLauncherSession(
                    session,
                    LauncherAutomationState.WaitingForClientProcess,
                    "Two Play attempts sent; waiting for client.exe until the bounded handoff expires.");
                return;

            case LauncherAutomationState.WaitingForClientProcess:
            case LauncherAutomationState.Stopped:
            default:
                return;
        }
    }

    private void HandleUnavailableLauncherProcess(
        LauncherAutomationSession session,
        DateTimeOffset now)
    {
        if (this.launcherSession != session ||
            this.pendingManagedClientLaunch == null)
        {
            return;
        }

        if (session.State ==
                LauncherAutomationState.WaitingForLauncherWindow)
        {
            if (now - session.StartedAt < launcherSingletonRebindGrace)
            {
                return;
            }

            this.FailManagedClientLaunch(
                "Net7 Launcher exited before its window appeared.");
            return;
        }

        this.expectManagerStartedClientUntil =
            now + managerStartedClientDetectionWindow;

        TransitionLauncherSession(
            session,
            LauncherAutomationState.WaitingForClientProcess,
            session.PlayAttemptCount == 0
                ? "Net7 Launcher closed; waiting for the game to start."
                : "Net7 Launcher exited after Play; waiting for client.exe.");
    }

    private static Process? FindRunningLauncherProcess(
        string launcherPath,
        int preferredProcessId)
    {
        Process? preferredProcess = null;

        if (preferredProcessId > 0)
        {
            try
            {
                preferredProcess =
                    Process.GetProcessById(preferredProcessId);

                preferredProcess.Refresh();

                if (preferredProcess.HasExited)
                {
                    preferredProcess.Dispose();
                    preferredProcess = null;
                }
                else if (preferredProcess.MainWindowHandle != IntPtr.Zero)
                {
                    return preferredProcess;
                }
            }
            catch (Exception)
            {
                preferredProcess?.Dispose();
                preferredProcess = null;
            }
        }

        var processName = Path.GetFileNameWithoutExtension(launcherPath);
        Process[] candidates;

        try
        {
            candidates = Process.GetProcessesByName(processName);
        }
        catch (Exception)
        {
            return preferredProcess;
        }

        int? visibleMatchedProcessId = null;
        int? fallbackMatchedProcessId = null;

        foreach (var candidate in candidates)
        {
            using (candidate)
            {
                try
                {
                    if (candidate.Id == preferredProcessId ||
                        !IsMatchingLauncherProcess(
                            candidate,
                            launcherPath,
                            processName))
                    {
                        continue;
                    }

                    fallbackMatchedProcessId ??= candidate.Id;

                    if (visibleMatchedProcessId == null &&
                        candidate.MainWindowHandle != IntPtr.Zero)
                    {
                        visibleMatchedProcessId = candidate.Id;
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        var matchedProcessId =
            visibleMatchedProcessId ??
            fallbackMatchedProcessId;

        if (matchedProcessId == null)
        {
            return preferredProcess;
        }

        preferredProcess?.Dispose();

        try
        {
            return Process.GetProcessById(matchedProcessId.Value);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool IsMatchingLauncherProcess(
        Process process,
        string launcherPath,
        string processName)
    {
        try
        {
            process.Refresh();

            if (process.HasExited)
            {
                return false;
            }

            var runningPath = process.MainModule?.FileName;

            if (!string.IsNullOrWhiteSpace(runningPath))
            {
                return string.Equals(
                    Path.GetFullPath(runningPath),
                    launcherPath,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
        }

        try
        {
            return string.Equals(
                       process.ProcessName,
                       processName,
                       StringComparison.OrdinalIgnoreCase) &&
                   process.MainWindowHandle != IntPtr.Zero &&
                   process.MainWindowTitle.StartsWith(
                       "LaunchNet7",
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TransitionLauncherSession(
        LauncherAutomationSession session,
        LauncherAutomationState state,
        string status)
    {
        session.State = state;
        session.StateEnteredAt = DateTimeOffset.UtcNow;
        session.Status = status;
        Debug.WriteLine(string.Concat("[Launcher] ", status));
    }

    private void FailManagedClientLaunch(
        string reason)
    {
        var request = this.pendingManagedClientLaunch;

        if (!this.TryRestoreGameRenderResolution(out var restoreStatus))
        {
            reason = string.Concat(
                reason,
                " ",
                restoreStatus);
        }

        var failure = string.Concat("Last launch failed: ", reason);

        if (request?.TargetSlotId is { } targetSlotId)
        {
            lock (this.lockObject)
            {
                this.managedClientLaunchFailures[targetSlotId] = failure;
            }
        }

        Debug.WriteLine(
            string.Concat(
                "[Launcher] ",
                request?.DisplayName ?? "Managed client",
                " failed: ",
                reason));

        this.launcherSession = null;
        this.pendingManagedClientLaunch = null;
        this.expectManagerStartedClientUntil = null;
    }

    private bool TryGetManagedClientLaunchFailure(
        Guid slotId,
        out string failure)
    {
        lock (this.lockObject)
        {
            return this.managedClientLaunchFailures.TryGetValue(
                slotId,
                out failure!);
        }
    }

    private void ClearManagedClientLaunchFailure(
        Guid slotId)
    {
        lock (this.lockObject)
        {
            _ = this.managedClientLaunchFailures.Remove(slotId);
        }
    }

    private void ClearManagedClientLaunchFailures()
    {
        lock (this.lockObject)
        {
            this.managedClientLaunchFailures.Clear();
        }
    }

    private void TickStartedClientAutomation(ClientInstance client)
    {
        if (!client.StartedByManager)
        {
            client.State = ClientState.WaitingForGameWindow;
            return;
        }

        switch (client.State)
        {
            case ClientState.WaitingForTos:
                if (!NativeMethods.IsTosWindowDisplayed(
                        client.ProcessId))
                {
                    return;
                }

                client.State = ClientState.AcceptingTos;
                client.AutomationStatus = "Accepting TOS";
                return;

            case ClientState.AcceptingTos:
                if (!NativeMethods.AcceptTos(
                        client.ProcessId))
                {
                    return;
                }

                client.AutomationStatus = "Waiting for window";
                client.State = ClientState.WaitingForGameWindow;
                return;

            case ClientState.WaitingForGameWindow:
            case ClientState.Docked:
            case ClientState.WaitingForIntro:
            case ClientState.WaitingForLogin:
            case ClientState.LoginSubmitted:
            case ClientState.WaitingForCharacterSelect:
            case ClientState.EnteringGame:
            case ClientState.Ready:
            case ClientState.Closing:
            case ClientState.Stopped:
            default:
                return;
        }
    }

    private bool IsSlotSatisfied(ClientSlot slot)
    {
        var client = this.clients.Values.FirstOrDefault(
            client => client.AssignedSlotId == slot.Id);

        if (client == null ||
            client.State is ClientState.Closing or
                ClientState.Stopped)
        {
            return false;
        }

        if (!slot.AutoLogin)
        {
            return client.GameWindowHandle != IntPtr.Zero;
        }

        if (!slot.AutoEnterGame)
        {
            return client.LifecycleState is
                ClientLifecycleState.CharacterSelection or
                ClientLifecycleState.InGame;
        }

        return client.LifecycleState ==
            ClientLifecycleState.InGame;
    }

    /// <summary>
    /// Resolves optional pre-game account configuration. This data is for
    /// configuration UI and login automation only, never runtime identity.
    /// </summary>
    public GameAccount? FindConfiguredAccount(Guid? accountId)
    {
        if (accountId == null)
        {
            return null;
        }

        return this.accounts.FirstOrDefault(account => account.Id == accountId.Value);
    }

    /// <summary>
    /// Resolves optional pre-game character-selection configuration. This data
    /// must never be used as an in-game character identity fallback.
    /// </summary>
    public GameCharacter? FindConfiguredCharacter(
        Guid? accountId,
        Guid? characterId)
    {
        var account = this.FindConfiguredAccount(accountId);

        if (account == null || characterId == null)
        {
            return null;
        }

        return account.Characters.FirstOrDefault(character => character.Id == characterId.Value);
    }

    private void GalaxyKnowledgeCoordinator_OnSnapshotChanged(
        object? sender,
        GalaxyKnowledgeSnapshotChangedEventArgs e)
    {
        this.GalaxyKnowledgeChanged?.Invoke(this, e);
    }

    private sealed record FleetFormationFollower(
        ClientInstance Client,
        bool IsFullyFormed);

    private sealed record FleetFormationContext(
        ClientInstance Leader,
        IReadOnlyList<ClientInstance> Participants,
        IReadOnlyList<FleetFormationFollower> EligibleFollowers)
    {
        public bool HasAnyFormedFollower =>
            this.EligibleFollowers.Any(follower => follower.IsFullyFormed);

        public bool IsFullyFormed =>
            this.EligibleFollowers.Count > 0 &&
            this.EligibleFollowers.All(follower => follower.IsFullyFormed);
    }

    private sealed class FleetLootCorpseAssignment
    {
        public FleetLootCorpseAssignment(
            uint corpseObjectId,
            int looterProcessId)
        {
            this.CorpseObjectId = corpseObjectId;
            this.LooterProcessId = looterProcessId;
        }

        public uint CorpseObjectId { get; }

        public int LooterProcessId { get; }

        public bool IsGameLootWindowOpen { get; set; }
    }

    private sealed class LauncherAutomationSession
    {
        public required int ProcessId { get; set; }

        public required string LauncherPath { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset StateEnteredAt { get; set; }

        public required string Status { get; set; }

        public DateTimeOffset? ReadyObservedAt { get; set; }

        public DateTimeOffset? PlayAttemptedAt { get; set; }

        public int PlayAttemptCount { get; set; }

        public LauncherAutomationState State { get; set; } =
            LauncherAutomationState.WaitingForLauncherWindow;
    }

    private enum LauncherAutomationState
    {
        WaitingForLauncherWindow,
        WaitingForReadiness,
        ActivatingPlay,
        WaitingForPlayAcceptance,
        WaitingForClientProcess,
        Stopped,
    }
}

