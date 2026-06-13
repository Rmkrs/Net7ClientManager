namespace Net7ClientManager.Models;

public sealed class LayoutProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Default";

    public List<ClientSlot> Slots { get; set; } = [];
}
