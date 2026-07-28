namespace Net7ClientManager.Observations.Observers;

internal readonly record struct ClientAuxDataEnumerationSnapshot(
    uint LookupAddress,
    uint FloatPropertyTypeDescriptor,
    uint DeltaFloatPropertyTypeDescriptor,
    uint BooleanPropertyTypeDescriptor,
    uint Int32PropertyTypeDescriptor,
    uint StringPropertyVTable,
    IReadOnlyList<ClientAuxDataPropertyReference> Properties);
