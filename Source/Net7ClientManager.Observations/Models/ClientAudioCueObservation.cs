namespace Net7ClientManager.Observations.Models;

public sealed record ClientAudioCueObservation
{
    public bool IsAvailable { get; init; }

    public string Status { get; init; } = "";

    public uint CueAddress { get; init; }

    public uint VTableAddress { get; init; }

    public uint DescriptorAddress { get; init; }

    public uint NameAddress { get; init; }

    public string ResourceName { get; init; } = "";

    public bool IsCurrent =>
        this.IsAvailable &&
        this.CueAddress != 0 &&
        !string.IsNullOrWhiteSpace(this.ResourceName);

    public static ClientAudioCueObservation Unavailable(string status)
    {
        return new ClientAudioCueObservation
        {
            Status = status,
        };
    }
}
