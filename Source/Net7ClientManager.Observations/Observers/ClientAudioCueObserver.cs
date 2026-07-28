namespace Net7ClientManager.Observations.Observers;

using Net7ClientManager.Observations.Models;

internal sealed class ClientAudioCueObserver
{
    // Runtime-validated retained cue chain:
    // client.exe + 0x007DAC6C -> cue +0x80 -> descriptor +0x04 -> name.
    private const uint CurrentCueGlobalRva = 0x007DAC6C;
    private const uint CueVTableOffset = 0x00;
    private const uint CueDescriptorOffset = 0x80;
    private const uint DescriptorNameOffset = 0x04;
    private const int MaximumResourceNameLength = 128;

    public void Refresh(ProcessMemoryReader memory, ObservedClientState state)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(state);

        state.AudioCue = this.Observe(memory, state.ModuleBaseAddress);
    }

    private ClientAudioCueObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress)
    {
        if (moduleBaseAddress == 0)
        {
            return ClientAudioCueObservation.Unavailable(
                "The client module base is unavailable");
        }

        uint cueGlobalAddress;

        try
        {
            cueGlobalAddress = checked(
                moduleBaseAddress + CurrentCueGlobalRva);
        }
        catch (OverflowException)
        {
            return ClientAudioCueObservation.Unavailable(
                "The retained audio-cue global address overflowed");
        }

        if (!memory.TryReadUInt32(cueGlobalAddress, out var cueAddress))
        {
            return ClientAudioCueObservation.Unavailable(
                "The retained audio-cue global could not be read");
        }

        if (cueAddress == 0)
        {
            return new ClientAudioCueObservation
            {
                IsAvailable = true,
                Status = "No retained audio cue is active",
            };
        }

        if (!TryAdd(cueAddress, CueVTableOffset, out var vtableFieldAddress) ||
            !memory.TryReadUInt32(vtableFieldAddress, out var vtableAddress) ||
            !TryAdd(cueAddress, CueDescriptorOffset, out var descriptorFieldAddress) ||
            !memory.TryReadUInt32(descriptorFieldAddress, out var descriptorAddress) ||
            descriptorAddress == 0 ||
            !TryAdd(descriptorAddress, DescriptorNameOffset, out var nameFieldAddress) ||
            !memory.TryReadUInt32(nameFieldAddress, out var nameAddress) ||
            nameAddress == 0 ||
            !memory.TryReadNullTerminatedLatin1String(
                nameAddress,
                MaximumResourceNameLength,
                out var resourceName))
        {
            return ClientAudioCueObservation.Unavailable(
                "The retained audio-cue descriptor chain could not be resolved");
        }

        resourceName = resourceName.Trim();

        if (resourceName.Length == 0 ||
            resourceName.Any(character =>
                character < ' ' || character > '~'))
        {
            return ClientAudioCueObservation.Unavailable(
                "The retained audio-cue resource name was invalid");
        }

        return new ClientAudioCueObservation
        {
            IsAvailable = true,
            Status = "Available",
            CueAddress = cueAddress,
            VTableAddress = vtableAddress,
            DescriptorAddress = descriptorAddress,
            NameAddress = nameAddress,
            ResourceName = resourceName,
        };
    }

    private static bool TryAdd(uint address, uint offset, out uint result)
    {
        try
        {
            result = checked(address + offset);
            return true;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }
    }
}
