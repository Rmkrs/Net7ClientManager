// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using System.Runtime.InteropServices;
using Net7ClientManager.Observations.Models;

internal sealed class ClientTargetInteractionObserver
{
    /*
     * Stable ownership path, validated live on 2026-06-27:
     *   SClient +0x1354 -> MainView
     *   MainView +0x1D0 -> CockpitHud
     *   CockpitHud +0xD4 -> ScannerHudController
     *   ScannerHudController +0x19C -> SelectedTargetVerbController
     *
     * SelectedTargetVerbController is validated by relocated vtable
     * 0x00AF94A4. Its relevant fields are:
     *   +0x7C active byte
     *   +0x88 selected ClientGameObject pointer
     *   +0xD4 VerbButton* vector begin
     *   +0xD8 VerbButton* vector end
     *   +0xDC VerbButton* vector capacity
     *
     * Clearing the vector does not zero its retained storage. Only entries
     * before vector end are live; reading toward capacity observes stale
     * button pointers from the previous target.
     *
     * VerbButton is validated by relocated vtable 0x00AF954C. Its packed
     * state at +0x64 stores VerbEnum in the high word and an unavailable
     * reason in the low word. A zero low word is executable. Gate is
     * 0x000A0000 and Too Far is reason 0x0002.
     */
    private const uint ImageBase = 0x00400000;

    private const uint TargetVerbControllerVTableStatic =
        0x00af94a4;

    private const uint TargetVerbControllerVTableRva =
        TargetVerbControllerVTableStatic - ImageBase;

    private const uint TargetVerbButtonVTableStatic =
        0x00af954c;

    private const uint TargetVerbButtonVTableRva =
        TargetVerbButtonVTableStatic - ImageBase;

    private const uint ClientContextMainViewOffset =
        0x1354;

    private const uint MainViewCockpitHudOffset =
        0x1d0;

    private const uint CockpitHudScannerHudControllerOffset =
        0xd4;

    private const uint ScannerHudTargetVerbControllerOffset =
        0x19c;

    private const uint ObjectVTableOffset =
        0x00;

    private const uint TargetVerbControllerHeaderOffset =
        0x7c;

    private const int TargetVerbControllerHeaderLength =
        0x64;

    private const int HeaderActiveOffset =
        0x00;

    private const int HeaderTargetClientObjectOffset =
        0x0c;

    private const int HeaderVerbButtonVectorBeginOffset =
        0x58;

    private const int HeaderVerbButtonVectorEndOffset =
        0x5c;

    private const int HeaderVerbButtonVectorCapacityOffset =
        0x60;

    private const uint ClientObjectObjectIdOffset =
        0x90;

    private const uint VerbButtonPackedStateOffset =
        0x64;

    private const uint VerbTypeMask =
        0xffff0000;

    private const uint UnavailableReasonMask =
        0x0000ffff;

    private const int MaximumVerbButtonCount =
        5;

    private const int MaximumVerbButtonCapacity =
        32;

    private const int MaximumStableReadAttempts =
        3;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        if (!state.HasDirectClientState ||
            state.ModuleBaseAddress == 0 ||
            state.ClientContextAddress == 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    "Direct SClient state is unavailable");

            return;
        }

        if (state.LoadingOrTransitionFlag != 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    "Target verb state is suspended during world transition");

            return;
        }

        if (!TryReadPointer(
                memory,
                state.ClientContextAddress,
                ClientContextMainViewOffset,
                "SClient.MainView",
                out var mainViewAddress,
                out var error) ||
            mainViewAddress == 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    mainViewAddress == 0 && string.IsNullOrEmpty(error)
                        ? "SClient MainView is null"
                        : error,
                    mainViewAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                mainViewAddress,
                MainViewCockpitHudOffset,
                "MainView.CockpitHud",
                out var cockpitHudAddress,
                out error) ||
            cockpitHudAddress == 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    cockpitHudAddress == 0 && string.IsNullOrEmpty(error)
                        ? "MainView CockpitHud is null"
                        : error,
                    mainViewAddress,
                    cockpitHudAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                cockpitHudAddress,
                CockpitHudScannerHudControllerOffset,
                "CockpitHud.ScannerHudController",
                out var scannerHudControllerAddress,
                out error) ||
            scannerHudControllerAddress == 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    scannerHudControllerAddress == 0 &&
                    string.IsNullOrEmpty(error)
                        ? "CockpitHud ScannerHudController is null"
                        : error,
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                scannerHudControllerAddress,
                ScannerHudTargetVerbControllerOffset,
                "ScannerHudController.TargetVerbController",
                out var targetVerbControllerAddress,
                out error) ||
            targetVerbControllerAddress == 0)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    targetVerbControllerAddress == 0 &&
                    string.IsNullOrEmpty(error)
                        ? "ScannerHudController TargetVerbController is null"
                        : error,
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress,
                    targetVerbControllerAddress);

            return;
        }

        if (!TryReadPointer(
                memory,
                targetVerbControllerAddress,
                ObjectVTableOffset,
                "TargetVerbController.VTable",
                out var targetVerbControllerVTableAddress,
                out error))
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress,
                    targetVerbControllerAddress);

            return;
        }

        if (!TryResolveRelocatedAddress(
                state.ModuleBaseAddress,
                TargetVerbControllerVTableRva,
                "TargetVerbController vtable",
                out var expectedTargetVerbControllerVTableAddress,
                out error))
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress,
                    targetVerbControllerAddress,
                    targetVerbControllerVTableAddress);

            return;
        }

        if (targetVerbControllerVTableAddress !=
            expectedTargetVerbControllerVTableAddress)
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                        $"Unexpected TargetVerbController vtable {FormatPointer(targetVerbControllerVTableAddress)}; expected {FormatPointer(expectedTargetVerbControllerVTableAddress)}",
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress,
                    targetVerbControllerAddress,
                    targetVerbControllerVTableAddress);

            return;
        }

        if (!TryResolveRelocatedAddress(
                state.ModuleBaseAddress,
                TargetVerbButtonVTableRva,
                "TargetVerbButton vtable",
                out var expectedTargetVerbButtonVTableAddress,
                out error))
        {
            state.TargetInteraction =
                ClientTargetInteractionObservation.Unavailable(
                    error,
                    mainViewAddress,
                    cockpitHudAddress,
                    scannerHudControllerAddress,
                    targetVerbControllerAddress,
                    targetVerbControllerVTableAddress);

            return;
        }

        var lastError =
            "Target verb state changed while it was being read";

        for (var attempt = 0;
             attempt < MaximumStableReadAttempts;
             attempt++)
        {
            if (!TryReadSample(
                    memory,
                    targetVerbControllerAddress,
                    expectedTargetVerbButtonVTableAddress,
                    out var first,
                    out lastError) ||
                !TryReadSample(
                    memory,
                    targetVerbControllerAddress,
                    expectedTargetVerbButtonVTableAddress,
                    out var second,
                    out lastError))
            {
                continue;
            }

            if (!SamplesMatch(first, second))
            {
                lastError =
                    "Target verb state changed while it was being read";

                continue;
            }

            if (!TargetMatchesCurrentObservation(
                    state.Target,
                    second,
                    out lastError))
            {
                continue;
            }

            var actions = second.Actions
                .Select(ToObservation)
                .ToArray();

            var executableCount = actions.Count(
                action => action.IsExecutable);

            var status = !second.IsActive
                ? "Available; selected-target verb controller is inactive"
                : second.TargetClientObjectAddress == 0
                    ? "Available; no selected target"
                    : string.Create(
                        CultureInfo.InvariantCulture,
                        $"Available; {actions.Length} selected-target verb(s), {executableCount} executable");

            state.TargetInteraction =
                new ClientTargetInteractionObservation
                {
                    IsAvailable = true,
                    Status = status,
                    MainViewAddress = mainViewAddress,
                    CockpitHudAddress = cockpitHudAddress,
                    ScannerHudControllerAddress =
                        scannerHudControllerAddress,
                    TargetVerbControllerAddress =
                        targetVerbControllerAddress,
                    TargetVerbControllerVTableAddress =
                        targetVerbControllerVTableAddress,
                    IsActive = second.IsActive,
                    HasTarget =
                        second.TargetClientObjectAddress != 0,
                    TargetClientObjectAddress =
                        second.TargetClientObjectAddress,
                    TargetObjectId = second.TargetObjectId,
                    VerbButtonVectorBeginAddress =
                        second.VerbButtonVectorBeginAddress,
                    VerbButtonVectorEndAddress =
                        second.VerbButtonVectorEndAddress,
                    VerbButtonVectorCapacityAddress =
                        second.VerbButtonVectorCapacityAddress,
                    Actions = actions,
                };

            return;
        }

        state.TargetInteraction =
            ClientTargetInteractionObservation.Unavailable(
                lastError,
                mainViewAddress,
                cockpitHudAddress,
                scannerHudControllerAddress,
                targetVerbControllerAddress,
                targetVerbControllerVTableAddress);
    }

    private static bool TryReadSample(
        ProcessMemoryReader memory,
        uint controllerAddress,
        uint expectedButtonVTableAddress,
        out TargetVerbSample sample,
        out string error)
    {
        sample = null!;
        error = "";

        uint headerAddress;

        try
        {
            headerAddress = checked(
                controllerAddress +
                TargetVerbControllerHeaderOffset);
        }
        catch (OverflowException)
        {
            error =
                "TargetVerbController header address overflow";

            return false;
        }

        if (!memory.TryReadBytes(
                headerAddress,
                TargetVerbControllerHeaderLength,
                out var header))
        {
            error = $"Could not read TargetVerbController header at {FormatPointer(headerAddress)}";

            return false;
        }

        var activeRaw =
            header[HeaderActiveOffset];

        if (activeRaw > 1)
        {
            error = $"Unexpected TargetVerbController active value {activeRaw}";

            return false;
        }

        var targetClientObjectAddress =
            ReadUInt32(
                header,
                HeaderTargetClientObjectOffset);

        var vectorBeginAddress =
            ReadUInt32(
                header,
                HeaderVerbButtonVectorBeginOffset);

        var vectorEndAddress =
            ReadUInt32(
                header,
                HeaderVerbButtonVectorEndOffset);

        var vectorCapacityAddress =
            ReadUInt32(
                header,
                HeaderVerbButtonVectorCapacityOffset);

        if (!TryValidateVector(
                vectorBeginAddress,
                vectorEndAddress,
                vectorCapacityAddress,
                out var buttonCount,
                out error))
        {
            return false;
        }

        var targetObjectId = 0u;

        if (targetClientObjectAddress != 0)
        {
            if (!TryReadUInt32(
                    memory,
                    targetClientObjectAddress,
                    ClientObjectObjectIdOffset,
                    "TargetClientObject.ObjectId",
                    out targetObjectId,
                    out error))
            {
                return false;
            }

            if (ClientObjectResolver.IsAbsentObjectId(
                    targetObjectId))
            {
                error = $"TargetVerbController target has absent ObjectId 0x{targetObjectId:X8}";

                return false;
            }
        }

        var actionSamples =
            new TargetVerbActionSample[buttonCount];

        if (buttonCount > 0)
        {
            if (!memory.TryReadBytes(
                    vectorBeginAddress,
                    buttonCount * sizeof(uint),
                    out var buttonPointers))
            {
                error = $"Could not read target verb button vector at {FormatPointer(vectorBeginAddress)}";

                return false;
            }

            HashSet<uint> seenVerbTypes = [];

            for (var index = 0;
                 index < buttonCount;
                 index++)
            {
                var buttonAddress =
                    ReadUInt32(
                        buttonPointers,
                        index * sizeof(uint));

                if (buttonAddress == 0)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Target verb button {index} is null");

                    return false;
                }

                if (!TryReadUInt32(
                        memory,
                        buttonAddress,
                        ObjectVTableOffset,
                        string.Create(CultureInfo.InvariantCulture, $"TargetVerbButton[{index}].VTable"),
                        out var buttonVTableAddress,
                        out error) ||
                    !TryReadUInt32(
                        memory,
                        buttonAddress,
                        VerbButtonPackedStateOffset,
                        string.Create(CultureInfo.InvariantCulture, $"TargetVerbButton[{index}].PackedState"),
                        out var packedState,
                        out error))
                {
                    return false;
                }

                if (buttonVTableAddress !=
                    expectedButtonVTableAddress)
                {
                    error = string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unexpected TargetVerbButton[{index}] vtable {FormatPointer(buttonVTableAddress)}; expected {FormatPointer(expectedButtonVTableAddress)}");

                    return false;
                }

                var rawVerbType =
                    packedState & VerbTypeMask;

                if (!seenVerbTypes.Add(rawVerbType))
                {
                    error = $"Duplicate target verb type 0x{rawVerbType:X8}";

                    return false;
                }

                actionSamples[index] =
                    new TargetVerbActionSample(
                        buttonAddress,
                        buttonVTableAddress,
                        packedState);
            }
        }

        sample = new TargetVerbSample(
            activeRaw != 0,
            targetClientObjectAddress,
            targetObjectId,
            vectorBeginAddress,
            vectorEndAddress,
            vectorCapacityAddress,
            actionSamples);

        return true;
    }

    private static bool TryValidateVector(
        uint beginAddress,
        uint endAddress,
        uint capacityAddress,
        out int count,
        out string error)
    {
        count = 0;
        error = "";

        if (beginAddress == 0)
        {
            if (endAddress == 0 &&
                capacityAddress == 0)
            {
                return true;
            }

            error =
                "Target verb vector begin is null while end or capacity is not";

            return false;
        }

        if (endAddress < beginAddress ||
            capacityAddress < endAddress)
        {
            error = $"Invalid target verb vector range {FormatPointer(beginAddress)}..{FormatPointer(endAddress)} capacity {FormatPointer(capacityAddress)}";

            return false;
        }

        var usedBytes =
            endAddress - beginAddress;

        var capacityBytes =
            capacityAddress - beginAddress;

        if (usedBytes % sizeof(uint) != 0 ||
            capacityBytes % sizeof(uint) != 0)
        {
            error =
                "Target verb vector range is not pointer aligned";

            return false;
        }

        var unsignedCount =
            usedBytes / sizeof(uint);

        var unsignedCapacity =
            capacityBytes / sizeof(uint);

        if (unsignedCount > MaximumVerbButtonCount)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Target verb vector contains {unsignedCount} entries; maximum is {MaximumVerbButtonCount}");

            return false;
        }

        if (unsignedCapacity > MaximumVerbButtonCapacity)
        {
            error = string.Create(
                CultureInfo.InvariantCulture,
                $"Target verb vector capacity {unsignedCapacity} exceeds validation maximum {MaximumVerbButtonCapacity}");

            return false;
        }

        count = checked((int)unsignedCount);
        return true;
    }

    private static bool SamplesMatch(
        TargetVerbSample left,
        TargetVerbSample right)
    {
        return left.IsActive == right.IsActive &&
            left.TargetClientObjectAddress ==
                right.TargetClientObjectAddress &&
            left.TargetObjectId == right.TargetObjectId &&
            left.VerbButtonVectorBeginAddress ==
                right.VerbButtonVectorBeginAddress &&
            left.VerbButtonVectorEndAddress ==
                right.VerbButtonVectorEndAddress &&
            left.VerbButtonVectorCapacityAddress ==
                right.VerbButtonVectorCapacityAddress &&
            left.Actions.SequenceEqual(right.Actions);
    }

    private static bool TargetMatchesCurrentObservation(
        ClientTargetObservation target,
        TargetVerbSample sample,
        out string error)
    {
        error = "";

        if (!target.IsAvailable)
        {
            return true;
        }

        var sampleHasTarget =
            sample.TargetClientObjectAddress != 0;

        if (target.HasTarget != sampleHasTarget)
        {
            error =
                "Selected target changed between target and verb observations";

            return false;
        }

        if (!target.HasTarget)
        {
            return true;
        }

        if (target.ClientObjectAddress !=
                sample.TargetClientObjectAddress ||
            target.ObjectId != sample.TargetObjectId)
        {
            error = $"Target verb controller refers to ObjectId {sample.TargetObjectId} at {FormatPointer(sample.TargetClientObjectAddress)}, but target observation refers to ObjectId {target.ObjectId} at {FormatPointer(target.ClientObjectAddress)}";

            return false;
        }

        return true;
    }

    private static ClientTargetVerbActionObservation ToObservation(
        TargetVerbActionSample sample)
    {
        var rawVerbType =
            sample.PackedState & VerbTypeMask;

        var unavailableReasonCode = checked(
            (ushort)(sample.PackedState &
                UnavailableReasonMask));

        return new ClientTargetVerbActionObservation
        {
            ButtonAddress = sample.ButtonAddress,
            ButtonVTableAddress =
                sample.ButtonVTableAddress,
            PackedState = sample.PackedState,
            RawVerbType = rawVerbType,
            Verb = Enum.IsDefined(
                    typeof(ClientTargetVerb),
                    rawVerbType)
                ? (ClientTargetVerb)rawVerbType
                : ClientTargetVerb.Unknown,
            UnavailableReasonCode =
                unavailableReasonCode,
            KnownUnavailableReason =
                TryGetKnownUnavailableReason(
                    unavailableReasonCode),
        };
    }

    private static ClientTargetVerbUnavailableReason?
        TryGetKnownUnavailableReason(
            ushort reasonCode)
    {
        return reasonCode switch
        {
            0x0000 =>
                ClientTargetVerbUnavailableReason.None,
            0x0001 =>
                ClientTargetVerbUnavailableReason.PlayerAlreadyInGroup,
            0x0002 =>
                ClientTargetVerbUnavailableReason.TooFar,
            _ => null,
        };
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        return TryReadUInt32(
            memory,
            baseAddress,
            offset,
            fieldName,
            out value,
            out error);
    }

    private static bool TryReadUInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string fieldName,
        out uint value,
        out string error)
    {
        uint address;

        try
        {
            address = checked(
                baseAddress + offset);
        }
        catch (OverflowException)
        {
            value = 0;
            error =
                $"Address overflow while reading {fieldName}";

            return false;
        }

        if (!memory.TryReadUInt32(
                address,
                out value))
        {
            error = $"Could not read {fieldName} at {FormatPointer(address)}";

            return false;
        }

        error = "";
        return true;
    }

    private static bool TryResolveRelocatedAddress(
        uint moduleBaseAddress,
        uint relativeVirtualAddress,
        string fieldName,
        out uint address,
        out string error)
    {
        try
        {
            address = checked(
                moduleBaseAddress +
                relativeVirtualAddress);

            error = "";
            return true;
        }
        catch (OverflowException)
        {
            address = 0;
            error =
                $"Address overflow while resolving {fieldName}";

            return false;
        }
    }

    private static uint ReadUInt32(
        byte[] bytes,
        int offset)
    {
        return BitConverter.ToUInt32(
            bytes,
            offset);
    }

    private static string FormatPointer(
        uint address)
    {
        return address == 0
            ? "None"
            : $"0x{address:X8}";
    }

    private sealed record TargetVerbSample(
        bool IsActive,
        uint TargetClientObjectAddress,
        uint TargetObjectId,
        uint VerbButtonVectorBeginAddress,
        uint VerbButtonVectorEndAddress,
        uint VerbButtonVectorCapacityAddress,
        IReadOnlyList<TargetVerbActionSample> Actions);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TargetVerbActionSample(
        uint ButtonAddress,
        uint ButtonVTableAddress,
        uint PackedState);
}
