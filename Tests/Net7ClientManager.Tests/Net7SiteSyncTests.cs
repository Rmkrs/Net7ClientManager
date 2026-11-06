namespace Net7ClientManager.Tests;

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Net7ClientManager.Core;
using Net7ClientManager.Forms;
using Net7ClientManager.Models;
using Net7ClientManager.Observations;
using Net7ClientManager.Net7Site;
using Net7ClientManager.Services;
using Xunit;

public sealed class Net7SiteSyncTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "Net7SyncTests", Guid.NewGuid().ToString("N"));

    public Net7SiteSyncTests() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, recursive: true);

    private static JsonObject Reply() => (JsonObject)JsonNode.Parse("""
        {"result":{"accounts":[{"id":1,"username":"alpha"}],"avatars":[
        {"id":10,"account_id":1,"slot":0,"avatar":{"name":"Pilot","race":"Terran","prof":"Tradesman"}}]}}
        """)!;

    private static GameAccount Local() => new()
    {
        LoginName = "alpha", DisplayName = "Account A", ProtectedPassword = "synthetic-password",
        Characters = [new() { Name = "Pilot", CharacterSlotNumber = 1, Race = "Terran", Profession = "Trader" }],
    };

    public static TheoryData<string> InvalidShapes =>
    [
        "missing-accounts", "null-accounts", "missing-avatars", "null-avatars", "null-account", "null-avatar",
        "account-id", "account-name", "missing-slot", "null-slot", "negative-slot", "large-slot", "avatar-id",
        "account-reference", "missing-details", "null-details", "empty-name", "duplicate-slot", "duplicate-avatar-id",
        "duplicate-name", "duplicate-account-id", "duplicate-login", "wrong-type", "malformed-json",
    ];

    [Theory]
    [MemberData(nameof(InvalidShapes))]
    public void InvalidRosterCannotProduceADestructivePreview(string shape)
    {
        var document = Reply();
        var result = document["result"]!;
        var accounts = result["accounts"]!.AsArray();
        var avatars = result["avatars"]!.AsArray();
        var avatar = avatars[0]!;
        switch (shape)
        {
            case "missing-accounts":
                result.AsObject().Remove("accounts");
                break;
            case "null-accounts":
                result["accounts"] = null;
                break;
            case "missing-avatars":
                result.AsObject().Remove("avatars");
                break;
            case "null-avatars":
                result["avatars"] = null;
                break;
            case "null-account":
                accounts[0] = null;
                break;
            case "null-avatar":
                avatars[0] = null;
                break;
            case "account-id":
                accounts[0]!["id"] = 0;
                break;
            case "account-name":
                accounts[0]!["username"] = " ";
                break;
            case "missing-slot":
                avatar.AsObject().Remove("slot");
                break;
            case "null-slot":
                avatar["slot"] = null;
                break;
            case "negative-slot":
                avatar["slot"] = -1;
                break;
            case "large-slot":
                avatar["slot"] = 5;
                break;
            case "avatar-id":
                avatar["id"] = 0;
                break;
            case "account-reference":
                avatar["account_id"] = 2;
                break;
            case "missing-details":
                avatar.AsObject().Remove("avatar");
                break;
            case "null-details":
                avatar["avatar"] = null;
                break;
            case "empty-name":
                avatar["avatar"]!["name"] = " ";
                break;
            case "duplicate-slot":
                var sameSlot = avatar.DeepClone();
                sameSlot["id"] = 11;
                sameSlot["avatar"]!["name"] = "Other";
                avatars.Add(sameSlot);
                break;
            case "duplicate-avatar-id":
                var sameId = avatar.DeepClone();
                sameId["slot"] = 1;
                sameId["avatar"]!["name"] = "Other";
                avatars.Add(sameId);
                break;
            case "duplicate-name":
                var sameName = avatar.DeepClone();
                sameName["slot"] = 1;
                sameName["id"] = 11;
                sameName["avatar"]!["name"] = " pilot ";
                avatars.Add(sameName);
                break;
            case "duplicate-account-id":
                accounts.Add(new JsonObject { ["id"] = 1, ["username"] = "beta" });
                break;
            case "duplicate-login":
                accounts.Add(new JsonObject { ["id"] = 2, ["username"] = " ALPHA " });
                break;
            case "wrong-type":
                avatar["slot"] = "invalid";
                break;
            case "malformed-json":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        var original = Local();
        var json = string.Equals(shape, "malformed-json", StringComparison.Ordinal) ? "invalid json" : document.ToJsonString();
        Assert.Throws<Net7SiteException>(() => Net7SiteAccountSync.Merge([original], Net7SiteApiParser.ParseAccounts(json)));
        Assert.Single(original.Characters);
        Assert.Equal("synthetic-password", original.ProtectedPassword);
    }

    [Fact]
    public void ValidNoOpPreservesIdentityPasswordsAndInput()
    {
        var a = Local();
        var merged = Net7SiteAccountSync.Merge([a], Net7SiteApiParser.ParseAccounts(Reply().ToJsonString()));
        var saved = Assert.Single(merged.Accounts);
        Assert.Empty(merged.Changes);
        Assert.Equal(a.Id, saved.Id);
        Assert.Equal(a.Characters[0].Id, saved.Characters[0].Id);
        Assert.Equal(a.ProtectedPassword, saved.ProtectedPassword);
        Assert.NotSame(a, saved);
        Assert.NotSame(a.Characters[0], saved.Characters[0]);
        Assert.Equal("Trader", saved.Characters[0].Profession);
    }

    [Fact]
    public void StringNumbersUnknownClassesAndAllFiveSlotsAreSupported()
    {
        var document = Reply();
        document["result"]!["accounts"]![0]!["id"] = "1";
        var avatars = document["result"]!["avatars"]!.AsArray();
        var template = avatars[0]!.DeepClone();
        avatars.Clear();
        for (var slot = 0; slot < 5; slot++)
        {
            var item = template.DeepClone();
            item["id"] = (10 + slot).ToString(System.Globalization.CultureInfo.InvariantCulture);
            item["account_id"] = "1";
            item["slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item["avatar"]!["name"] = slot == 0 ? "Pilot" : "Pilot" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            item["avatar"]!["prof"] = "Unknown";
            avatars.Add(item);
        }

        var merged = Net7SiteAccountSync.Merge([Local()], Net7SiteApiParser.ParseAccounts(document.ToJsonString()));
        Assert.Equal(Enumerable.Range(1, 5), merged.Accounts[0].Characters.Select(c => c.CharacterSlotNumber));
        Assert.Equal("Trader", merged.Accounts[0].Characters[0].Profession);
        Assert.Null(merged.Accounts[0].Characters[1].Profession);
    }

    [Fact]
    public void ExplicitEmptyListPreviewsRemovalAndLeavesOtherAccountsAlone()
    {
        var a = Local();
        var other = Local();
        other.LoginName = "other";
        var document = Reply();
        document["result"]!["avatars"] = new JsonArray();
        var merged = Net7SiteAccountSync.Merge([a, other], Net7SiteApiParser.ParseAccounts(document.ToJsonString()));
        Assert.Equal(1, merged.RemovedCharacterCount);
        Assert.Single(merged.Changes);
        Assert.Empty(merged.Accounts[0].Characters);
        Assert.Equal(other.Characters[0].Id, merged.Accounts[1].Characters[0].Id);
        Assert.Equal(other.ProtectedPassword, merged.Accounts[1].ProtectedPassword);
        Assert.Single(a.Characters);
    }

    [Fact]
    public void NewAccountHasNoInventedPassword()
    {
        var merged = Net7SiteAccountSync.Merge([], Net7SiteApiParser.ParseAccounts(Reply().ToJsonString()));
        Assert.Equal("alpha", merged.Accounts[0].LoginName);
        Assert.Null(merged.Accounts[0].ProtectedPassword);
        Assert.Equal(1, merged.Accounts[0].Characters[0].CharacterSlotNumber);
    }

    [Fact]
    public void ImportedSlotSurvivesAccountStoreReloadWithLegacyField()
    {
        var a = Local();
        a.Characters[0].CharacterSelectClickActionName = "Character Screen Slot 1";
        var document = Reply();
        document["result"]!["avatars"]![0]!["slot"] = 4;
        var merged = Net7SiteAccountSync.Merge([a], Net7SiteApiParser.ParseAccounts(document.ToJsonString()));
        var store = new GameAccountStore(Path.Combine(this.directory, "accounts.json"));
        store.Save(merged.Accounts);
        Assert.Equal(5, store.Load()[0].Characters[0].CharacterSlotNumber);
        Assert.Equal(1, a.Characters[0].CharacterSlotNumber);
    }

    [Fact]
    public void TransfersPersistAllProfilesAndRebindPendingLaunchWithoutChangingDestinationPassword()
    {
        var a = Local();
        var b = new GameAccount { LoginName = "beta", ProtectedPassword = "destination-password" };
        var roster = new[]
        {
            new Net7SiteGameAccount { AccountId = 1, LoginName = "alpha", Avatars = [] },
            new Net7SiteGameAccount { AccountId = 2, LoginName = "beta", Avatars = [new() { AvatarId = 10, Name = "Pilot", Slot = 3 }] },
        };
        var merged = Net7SiteAccountSync.Merge([a, b], roster);
        Assert.Equal(0, merged.RemovedCharacterCount);
        var id = a.Characters[0].Id;
        var profiles = new List<LayoutProfile>
        {
            new() { Slots = [new() { AccountId = a.Id, CharacterId = id, AutoLogin = true, AutoEnterGame = true }] },
            new() { Slots = [new() { AccountId = a.Id, CharacterId = id }, new() { AccountId = a.Id }] },
        };
        var pending = new ManagedClientLaunchRequest { AccountId = a.Id, CharacterId = id, AutoLogin = true, AutoEnterGame = true, HostWidth = 800, DisplayName = "Pending" };
        var transfers = new ConfiguredAccountTransfers([a, b], merged.Accounts);
        Assert.True(transfers.ApplyToProfiles(profiles));
        transfers.ApplyToLaunch(pending);
        Assert.False(transfers.ApplyToProfiles(profiles));
        Assert.Equal(b.Id, pending.AccountId);
        Assert.Equal(id, pending.CharacterId);
        Assert.True(pending.AutoLogin);
        Assert.True(pending.AutoEnterGame);
        Assert.Equal(800, pending.HostWidth);
        Assert.Equal(a.Id, profiles[1].Slots[1].AccountId);
        var store = new SettingsStore(Path.Combine(this.directory, "settings.json"));
        store.Save(new AppSettings { Profiles = profiles });
        var reloaded = store.Load();
        foreach (var slot in reloaded.Profiles.SelectMany(p => p.Slots).Where(s => s.CharacterId == id))
        {
            var account = Assert.Single(merged.Accounts, account => account.Id == slot.AccountId);
            Assert.Equal("destination-password", account.ProtectedPassword);
            Assert.Equal(4, Assert.Single(account.Characters, c => c.Id == slot.CharacterId).CharacterSlotNumber);
        }
    }

    [Fact]
    public void AccountSaveEntryPointPersistsTheNewLaunchAccount()
    {
        var source = Local();
        var destination = new GameAccount { LoginName = "beta" };
        var pilot = source.Characters[0];
        var roster = new[]
        {
            new Net7SiteGameAccount { AccountId = 1, LoginName = "alpha", Avatars = [] },
            new Net7SiteGameAccount { AccountId = 2, LoginName = "beta", Avatars = [new() { AvatarId = 10, Name = "Pilot", Slot = 2 }] },
        };
        var merged = Net7SiteAccountSync.Merge([source, destination], roster);
        var settings = new AppSettings
        {
            Profiles = [new() { Slots = [new() { AccountId = source.Id, CharacterId = pilot.Id }] }],
        };
        var pending = new ManagedClientLaunchRequest { AccountId = source.Id, CharacterId = pilot.Id };
        var accountsStore = new GameAccountStore(Path.Combine(this.directory, "accounts.json"));
        var settingsStore = new SettingsStore(Path.Combine(this.directory, "settings.json"));
        // Exercise the public save boundary used by both UI entry points, without
        // constructing unrelated live services, timers, or user-data stores.
        var manager = (ClientManager)RuntimeHelpers.GetUninitializedObject(typeof(ClientManager));
        void SetField<T>(string name, T value) => typeof(ClientManager)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(manager, value);
        SetField("accounts", new List<GameAccount> { source, destination });
        SetField("gameAccountStore", accountsStore);
        SetField("settings", settings);
        SetField("settingsStore", settingsStore);
        SetField("settingsSaveLock", new System.Threading.Lock());
        SetField("clients", new Dictionary<int, ClientInstance>());
        SetField("pendingManagedClientLaunch", pending);
        manager.SaveConfiguredAccounts(merged.Accounts);
        var savedSlot = settingsStore.Load().Profiles[0].Slots[0];
        Assert.Equal(destination.Id, savedSlot.AccountId);
        Assert.Equal(destination.Id, pending.AccountId);
        Assert.Equal(3, manager.FindConfiguredCharacter(savedSlot.AccountId, savedSlot.CharacterId)!.CharacterSlotNumber);
        Assert.Equal(pilot.Id, Assert.Single(accountsStore.Load(), a => a.Id == destination.Id).Characters[0].Id);
    }

    [Fact]
    public void ExplicitlyEmptyAccountAndAvatarArraysAreValid()
    {
        Assert.Empty(Net7SiteApiParser.ParseAccounts("""{"result":{"accounts":[],"avatars":[]}}"""));
    }

    [Fact]
    public void DeletionAndAmbiguousIdsDoNotRebindAnotherAccount()
    {
        var a = Local();
        var id = a.Characters[0].Id;
        var b = new GameAccount { LoginName = "beta", Characters = [new() { Id = id, Name = "Pilot" }] };
        Assert.False(new ConfiguredAccountTransfers([a], []).TryGetDestination(a.Id, id, out _));
        Assert.False(new ConfiguredAccountTransfers([a], [a, b]).TryGetDestination(a.Id, id, out _));
        Assert.False(new ConfiguredAccountTransfers([a, b], [b]).TryGetDestination(a.Id, id, out _));
        Assert.False(new ConfiguredAccountTransfers([a], [b]).TryGetDestination(Guid.NewGuid(), id, out _));
    }

    [Fact]
    public void ActiveTransferClearsPendingClickAndBlocksActualCharacterSelectionPath()
    {
        var a = Local();
        var id = a.Characters[0].Id;
        var b = new GameAccount { LoginName = "beta", Characters = [new() { Id = id, Name = "Pilot" }] };
        using var process = Process.GetCurrentProcess();
        var launch = new ManagedClientLaunchRequest { AccountId = a.Id, CharacterId = id, AutoEnterGame = true };
        var client = new ClientInstance(0, process)
        {
            ManagedLaunchRequest = launch, LifecycleState = ClientLifecycleState.CharacterSelection,
            PendingEnterGameAction = new(), EnterGameClickAt = DateTimeOffset.UtcNow,
        };
        new ConfiguredAccountTransfers([a], [b]).ApplyToClient(client, launch);
        Assert.True(client.RequiresLoginAfterAccountTransfer);
        Assert.Null(client.PendingEnterGameAction);
        Assert.Null(client.EnterGameClickAt);
        Assert.Equal(b.Id, launch.AccountId);
        // Invoke our own method without starting the full client manager or any native input.
        // It must return at the transfer guard before touching other manager services.
        var manager = (ClientManager)RuntimeHelpers.GetUninitializedObject(typeof(ClientManager));
        typeof(ClientManager).GetMethod("TryEnterGame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(manager, [client, launch]);
        Assert.Contains("log in again", client.AutomationStatus, StringComparison.Ordinal);
        Assert.Null(client.PendingEnterGameAction);
    }

    [Fact]
    public void ForgetLoginDiscardsPreviewAndDisablesApplyWithoutShowingAWindow()
    {
        RunSta(() =>
        {
            var store = new Net7SiteSessionStore(Path.Combine(this.directory, "session.json"));
            store.Save(Session());
            using var form = new Net7SiteSyncForm([], store);
            form.ShowPreview(Net7SiteApiParser.ParseAccounts(Reply().ToJsonString()));
            var apply = Assert.Single(Controls(form).OfType<Button>(), button => string.Equals(button.Text, "Apply", StringComparison.Ordinal));
            Assert.True(apply.Enabled);
            Assert.NotNull(form.SyncResult);
            form.ForgetLogin();
            Assert.Null(form.SyncResult);
            Assert.False(apply.Enabled);
            Assert.Null(store.Load());
            form.ShowPreview(Net7SiteApiParser.ParseAccounts(Reply().ToJsonString()));
            Assert.True(apply.Enabled);
            Assert.NotNull(form.SyncResult);
        });
    }

    [Fact]
    public void RememberedSessionIsProtectedRoundTripsAndCanBeForgotten()
    {
        var path = Path.Combine(this.directory, "session.json");
        var store = new Net7SiteSessionStore(path);
        Assert.Null(store.Load());
        var session = Session();
        store.Save(session);
        Assert.DoesNotContain("synthetic-cookie-secret", File.ReadAllText(path), StringComparison.Ordinal);
        var restored = Assert.IsType<Net7SiteSession>(store.Load());
        Assert.Equal(session.UserName, restored.UserName);
        Assert.Equal(session.Cookies, restored.Cookies);
        using var client = new Net7SiteClient();
        client.RestoreCookies(restored.Cookies);
        Assert.Equal("synthetic-cookie-secret", Assert.Single(client.ExportCookies()).Value);
        store.Delete();
        Assert.Null(store.Load());
    }

    [Theory]
    [InlineData("broken json")]
    [InlineData("{\"ProtectedCookies\":\"not base64\"}")]
    [InlineData("null")]
    public void CorruptSessionDoesNotCrash(string content)
    {
        var path = Path.Combine(this.directory, "session.json");
        File.WriteAllText(path, content);
        Assert.Null(new Net7SiteSessionStore(path).Load());
    }

    private static Net7SiteSession Session() => new("synthetic-user", [new()
    {
        Name = "PHPSESSID", Value = "synthetic-cookie-secret", Domain = "www.net-7.org", Secure = true, HttpOnly = true,
    }]);

    private static IEnumerable<Control> Controls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (var child in Controls(control))
            { yield return child; }
        }
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
        { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
}
