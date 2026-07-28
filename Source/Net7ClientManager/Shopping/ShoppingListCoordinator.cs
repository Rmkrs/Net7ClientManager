namespace Net7ClientManager.Shopping;

using Net7ClientManager.GalaxyKnowledge;
using Net7ClientManager.Observations;
using Net7ClientManager.PilotArchive;

internal sealed class ShoppingListCoordinator
{
    private readonly ShoppingListStore store;
    private readonly PilotArchiveStore pilotArchive;
    private readonly Func<GalaxyKnowledgeSnapshot> getKnowledge;
    private readonly Func<IReadOnlyList<ClientObservationSnapshot>>
        getObservationSnapshots;

    public ShoppingListCoordinator(
        ShoppingListStore store,
        PilotArchiveStore pilotArchive,
        Func<GalaxyKnowledgeSnapshot> getKnowledge,
        Func<IReadOnlyList<ClientObservationSnapshot>> getObservationSnapshots)
    {
        this.store = store;
        this.pilotArchive = pilotArchive;
        this.getKnowledge = getKnowledge;
        this.getObservationSnapshots = getObservationSnapshots;
    }

    public event EventHandler<ShoppingListsChangedEventArgs>? Changed;

    public IReadOnlyList<ShoppingListSummary> GetLists() =>
        this.store.GetLists();

    public ShoppingListDocument? GetList(string listId) =>
        this.store.GetList(listId);

    public ShoppingListDocument? GetPreferredList()
    {
        var activeId = this.store.GetActiveListId();
        if (activeId != null &&
            this.store.GetList(activeId) is { } active)
        {
            return active;
        }

        var first = this.store.GetLists().FirstOrDefault();
        return first == null
            ? null
            : this.store.GetList(first.ListId);
    }

    public ShoppingListDocument AddRequestedOutput(
        int itemTemplateId,
        long quantity,
        string? listId = null)
    {
        if (itemTemplateId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemTemplateId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity));
        }

        var list = !string.IsNullOrWhiteSpace(listId)
            ? this.store.GetList(listId)
            : this.GetPreferredList();
        if (list == null)
        {
            list = this.store.Create("Shopping List");
            this.store.SetActiveList(list.ListId);
        }

        var outputs = list.RequestedOutputs
            .ToDictionary(
                output => output.ItemTemplateId,
                output => output.Quantity);
        outputs[itemTemplateId] = checked(
            outputs.GetValueOrDefault(itemTemplateId) + quantity);
        var saved = this.store.Save(
            list with
            {
                RequestedOutputs = outputs
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new ShoppingListRequestedOutput
                    {
                        ItemTemplateId = pair.Key,
                        Quantity = pair.Value,
                    })
                    .ToArray(),
            });
        this.Changed?.Invoke(
            this,
            new ShoppingListsChangedEventArgs(saved.ListId));
        return saved;
    }

    public ShoppingListDocument Create(string name)
    {
        var document = this.store.Create(name);
        this.Changed?.Invoke(
            this,
            new ShoppingListsChangedEventArgs(document.ListId));
        return document;
    }

    public ShoppingListDocument Save(ShoppingListDocument document)
    {
        var saved = this.store.Save(document);
        this.Changed?.Invoke(
            this,
            new ShoppingListsChangedEventArgs(saved.ListId));
        return saved;
    }

    public bool Delete(string listId)
    {
        var deleted = this.store.Delete(listId);
        if (deleted)
        {
            this.Changed?.Invoke(
                this,
                new ShoppingListsChangedEventArgs(listId));
        }

        return deleted;
    }

    public string? GetActiveListId() =>
        this.store.GetActiveListId();

    public void SetActiveList(string? listId)
    {
        this.store.SetActiveList(listId);
        this.Changed?.Invoke(
            this,
            new ShoppingListsChangedEventArgs(listId));
    }

    public ShoppingPlanSnapshot? BuildPlan(
        string listId,
        uint? activeCharacterId = null,
        int? activeProcessId = null)
    {
        var list = this.store.GetList(listId);
        if (list == null)
        {
            return null;
        }

        var ownership = ShoppingOwnershipBuilder.Build(
            this.pilotArchive,
            this.getObservationSnapshots(),
            activeCharacterId,
            activeProcessId);
        return ShoppingPlanBuilder.Build(
            list,
            this.getKnowledge(),
            ownership);
    }

    public ShoppingPlanSnapshot? BuildActivePlan(
        uint? activeCharacterId = null,
        int? activeProcessId = null)
    {
        var listId = this.store.GetActiveListId();
        return listId == null
            ? null
            : this.BuildPlan(
                listId,
                activeCharacterId,
                activeProcessId);
    }
}
