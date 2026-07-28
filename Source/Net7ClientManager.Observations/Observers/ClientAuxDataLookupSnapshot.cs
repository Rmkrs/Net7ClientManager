namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientAuxDataLookupSnapshot(
    uint LookupAddress,
    uint FloatPropertyTypeDescriptor,
    uint DeltaFloatPropertyTypeDescriptor,
    uint BooleanPropertyTypeDescriptor,
    uint Int32PropertyTypeDescriptor,
    uint StringPropertyVTable,
    IReadOnlyDictionary<string, uint> Properties)
{
    // Backward-compatible constructor used by the on-demand census when it
    // evaluates one delta-interpolated float property in isolation.
    public ClientAuxDataLookupSnapshot(
        uint lookupAddress,
        uint floatPropertyTypeDescriptor,
        uint deltaFloatPropertyTypeDescriptor,
        IReadOnlyDictionary<string, uint> properties)
        : this(
            lookupAddress,
            floatPropertyTypeDescriptor,
            deltaFloatPropertyTypeDescriptor,
            0,
            0,
            0,
            properties)
    {
    }
}
