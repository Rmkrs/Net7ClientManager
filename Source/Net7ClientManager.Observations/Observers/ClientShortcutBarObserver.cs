namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal sealed class ClientShortcutBarObserver
{
    private const uint ImageBase = 0x00400000;
    private const uint MaximumExpectedImageSpan = 0x01000000;
    private const uint MinimumProcessAddress = 0x00010000;

    private const uint CockpitControllerVTableStatic = 0x00af0cfc;
    private const uint CockpitControllerVTableRva =
        CockpitControllerVTableStatic - ImageBase;

    private const uint ClientContextShortcutIdentityLowOffset = 0x12e8;
    private const uint ClientContextShortcutIdentityHighOffset = 0x12ec;
    private const uint ClientContextMainViewOffset = 0x1354;
    private const uint MainViewCockpitControllerOffset = 0x1d0;
    private const uint ObjectVTableOffset = 0x00;
    private const uint CockpitControllerShortcutBankOffset = 0xdc;
    private const uint ShortcutBankBar0Offset = 0xac;
    private const uint ShortcutBankBar1Offset = 0xb0;
    private const uint ShortcutBarVisibleButtonsOffset = 0x10;
    private const uint ShortcutBarCurrentGroupOffset = 0x4c;
    private const uint ShortcutBarGroup0CountOffset = 0x34;
    private const uint ShortcutBarGroup1CountOffset = 0x38;
    private const uint ShortcutBarToggleWidgetOffset = 0x40;
    private const uint ShortcutBarGroup0GadgetsOffset = 0x1c;
    private const uint ShortcutBarGroup1GadgetsOffset = 0x28;
    private const uint ShortcutBarPointerStride = 0x04;
    private const int ShortcutButtonsPerGroup = 3;

    // The native shortcut reconciliation path recreates gadget shells when
    // shortcuts move. Only the payload identities below are stable; none of
    // these runtime addresses may be cached as shortcut identity.
    private const uint GadgetRendererOffset = 0x60;
    private const uint GadgetPayloadOffset = 0x64;
    private const uint PayloadPrimaryIdentityOffset = 0x20;
    private const uint PayloadSecondaryIdentityOffset = 0x34;
    private const uint VisibleButtonActiveGadgetOffset = 0x64;

    private const uint SkillRendererVTableRva = 0x006ef4fc;
    private const uint SkillPayloadVTableRva = 0x006fd664;
    private const uint EquipmentRendererVTableRva = 0x006f4c74;
    private const uint EquipmentPayloadVTableRva = 0x006fcd04;

    // These relocated globals are pointer-vector bounds. The vectors and
    // their pointed records are process-owned and must be read fresh.
    private const uint StaticSkillCatalogBeginRva = 0x007e8e2c;
    private const uint StaticSkillCatalogEndRva = 0x007e8e30;
    private const uint StaticAbilityCatalogBeginRva = 0x007e8dac;
    private const uint StaticAbilityCatalogEndRva = 0x007e8db0;

    private const uint SkillDefinitionNameOffset = 0x00;
    private const uint SkillDefinitionIconResourceOffset = 0x04;
    private const uint AbilityDefinitionNameOffset = 0x04;
    private const uint AbilityDefinitionTintRedOffset = 0x20;
    private const uint AbilityDefinitionTintGreenOffset = 0x24;
    private const uint AbilityDefinitionTintBlueOffset = 0x28;

    private const int MaximumSkillDefinitionCount = 256;
    private const int MaximumAbilityDefinitionCount = 8192;
    private const int MaximumSkillNameLength = 160;
    private const int MaximumAbilityNameLength = 160;
    private const int MaximumIconResourceNameLength = 512;

    public void Refresh(
        ProcessMemoryReader memory,
        ObservedClientState state)
    {
        state.Shortcuts = this.Observe(
            memory,
            state.ModuleBaseAddress,
            state.ClientContextAddress,
            state.HasDirectClientState,
            state.LocalPlayer);
    }

    public ClientShortcutStateObservation Observe(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint clientContextAddress,
        bool hasDirectClientState,
        ClientLocalPlayerObservation localPlayer)
    {
        if (!hasDirectClientState ||
            moduleBaseAddress == 0 ||
            clientContextAddress == 0)
        {
            return ClientShortcutStateObservation.Unavailable(
                "Direct SClient state is unavailable");
        }

        var shortcutIdentity = this.ReadShortcutIdentity(
            memory,
            clientContextAddress);

        if (!TryReadPointer(
                memory,
                clientContextAddress,
                ClientContextMainViewOffset,
                "SClient.MainView",
                out var mainViewAddress,
                out var error))
        {
            return ClientShortcutStateObservation.Unavailable(
                error,
                shortcutIdentity);
        }

        if (mainViewAddress == 0)
        {
            return ClientShortcutStateObservation.Unavailable(
                "SClient MainView is null",
                shortcutIdentity);
        }

        if (!TryReadPointer(
                memory,
                mainViewAddress,
                MainViewCockpitControllerOffset,
                "MainView.CockpitController",
                out var cockpitControllerAddress,
                out error))
        {
            return ClientShortcutStateObservation.Unavailable(
                error,
                shortcutIdentity) with
            {
                MainViewAddress = mainViewAddress,
            };
        }

        if (cockpitControllerAddress == 0)
        {
            return ClientShortcutStateObservation.Unavailable(
                "MainView cockpit controller is null",
                shortcutIdentity) with
            {
                MainViewAddress = mainViewAddress,
            };
        }

        if (!TryReadPointer(
                memory,
                cockpitControllerAddress,
                ObjectVTableOffset,
                "CockpitController.VTable",
                out var cockpitControllerVTableAddress,
                out error) ||
            !TryValidateRelocatedAddress(
                moduleBaseAddress,
                CockpitControllerVTableRva,
                cockpitControllerVTableAddress,
                "CockpitController vtable",
                out error))
        {
            return ClientShortcutStateObservation.Unavailable(
                error,
                shortcutIdentity) with
            {
                MainViewAddress = mainViewAddress,
                CockpitControllerAddress = cockpitControllerAddress,
            };
        }

        if (!TryReadPointer(
                memory,
                cockpitControllerAddress,
                CockpitControllerShortcutBankOffset,
                "CockpitController.ShortcutBank",
                out var shortcutBankAddress,
                out error))
        {
            return ClientShortcutStateObservation.Unavailable(
                error,
                shortcutIdentity) with
            {
                MainViewAddress = mainViewAddress,
                CockpitControllerAddress = cockpitControllerAddress,
            };
        }

        if (shortcutBankAddress == 0)
        {
            return ClientShortcutStateObservation.Unavailable(
                "CockpitController shortcut bank is null",
                shortcutIdentity) with
            {
                MainViewAddress = mainViewAddress,
                CockpitControllerAddress = cockpitControllerAddress,
            };
        }

        var catalogs = ObserveNativeCatalogs(
            memory,
            moduleBaseAddress);

        var bar0 = this.ObserveBar(
            memory,
            moduleBaseAddress,
            shortcutBankAddress,
            bar: 0,
            ShortcutBankBar0Offset,
            catalogs,
            localPlayer);

        var bar1 = this.ObserveBar(
            memory,
            moduleBaseAddress,
            shortcutBankAddress,
            bar: 1,
            ShortcutBankBar1Offset,
            catalogs,
            localPlayer);

        var bars = new[]
        {
            bar0,
            bar1,
        };

        var isAvailable = bars.All(bar => bar.IsAvailable);
        var occupiedCount = bars
            .SelectMany(bar => bar.Slots)
            .Count(slot => slot.IsOccupied);
        var resolvedCount = bars
            .SelectMany(bar => bar.Slots)
            .Count(slot => slot.IsResolved);

        var status = isAvailable
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Shortcut bars available; resolved {resolvedCount}/{occupiedCount} occupied shortcuts")
            : string.Join(
                "; ",
                bars
                    .Where(bar => !bar.IsAvailable)
                    .Select(bar => $"bar{bar.Bar}: {bar.Status}"));

        return new ClientShortcutStateObservation
        {
            IsAvailable = isAvailable,
            Status = status,
            ShortcutIdentity = shortcutIdentity,
            SkillsSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(CultureInfo.InvariantCulture), "_Skills")
                : null,
            EquipmentSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(CultureInfo.InvariantCulture), "_PDA_EQUIP")
                : null,
            CargoSectionName = shortcutIdentity.HasValue
                ? string.Concat(shortcutIdentity.Value.ToString(CultureInfo.InvariantCulture), "_PDA_CARGO")
                : null,
            MainViewAddress = mainViewAddress,
            CockpitControllerAddress = cockpitControllerAddress,
            ShortcutBankAddress = shortcutBankAddress,
            SkillCatalogIsAvailable = catalogs.SkillCatalogIsAvailable,
            SkillCatalogStatus = catalogs.SkillCatalogStatus,
            SkillDefinitionCount = catalogs.SkillDefinitionCount,
            AbilityCatalogIsAvailable = catalogs.AbilityCatalogIsAvailable,
            AbilityCatalogStatus = catalogs.AbilityCatalogStatus,
            AbilityDefinitionCount = catalogs.AbilityDefinitionCount,
            Bars = bars,
        };
    }

    private ulong? ReadShortcutIdentity(
        ProcessMemoryReader memory,
        uint clientContextAddress)
    {
        if (!memory.TryReadUInt32(
                clientContextAddress + ClientContextShortcutIdentityLowOffset,
                out var low) ||
            !memory.TryReadUInt32(
                clientContextAddress + ClientContextShortcutIdentityHighOffset,
                out var high))
        {
            return null;
        }

        return ((ulong)high << 32) | low;
    }

    private ClientShortcutBarObservation ObserveBar(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint shortcutBankAddress,
        int bar,
        uint barOffset,
        NativeShortcutCatalogs catalogs,
        ClientLocalPlayerObservation localPlayer)
    {
        if (!TryReadPointer(
                memory,
                shortcutBankAddress,
                barOffset,
                $"ShortcutBank.Bar{bar}",
                out var barAddress,
                out var error))
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                error);
        }

        if (barAddress == 0)
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                $"ShortcutBar{bar} is null");
        }

        if (!TryReadInt32(
                memory,
                barAddress,
                ShortcutBarCurrentGroupOffset,
                $"ShortcutBar{bar}.CurrentGroup",
                out var currentGroup,
                out error) ||
            !TryReadInt32(
                memory,
                barAddress,
                ShortcutBarGroup0CountOffset,
                $"ShortcutBar{bar}.Group0Count",
                out var group0Count,
                out error) ||
            !TryReadInt32(
                memory,
                barAddress,
                ShortcutBarGroup1CountOffset,
                $"ShortcutBar{bar}.Group1Count",
                out var group1Count,
                out error) ||
            !TryReadPointer(
                memory,
                barAddress,
                ShortcutBarToggleWidgetOffset,
                $"ShortcutBar{bar}.ToggleWidget",
                out var toggleWidgetAddress,
                out error))
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                error) with
            {
                BarAddress = barAddress,
            };
        }

        if (currentGroup is < 0 or > 1)
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                $"ShortcutBar{bar} current group was {currentGroup}") with
            {
                BarAddress = barAddress,
                CurrentGroup = currentGroup,
                Group0Count = group0Count,
                Group1Count = group1Count,
                ToggleWidgetAddress = toggleWidgetAddress,
            };
        }

        if (group0Count is < 0 or > 3 ||
            group1Count is < 0 or > 3)
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                $"ShortcutBar{bar} group counts were {group0Count}/{group1Count}") with
            {
                BarAddress = barAddress,
                CurrentGroup = currentGroup,
                Group0Count = group0Count,
                Group1Count = group1Count,
                ToggleWidgetAddress = toggleWidgetAddress,
            };
        }

        if (!this.TryObserveSlots(
                memory,
                moduleBaseAddress,
                barAddress,
                bar,
                currentGroup,
                catalogs,
                localPlayer,
                out var slots,
                out error))
        {
            return ClientShortcutBarObservation.Unavailable(
                bar,
                error) with
            {
                BarAddress = barAddress,
                CurrentGroup = currentGroup,
                Group0Count = group0Count,
                Group1Count = group1Count,
                ToggleWidgetAddress = toggleWidgetAddress,
            };
        }

        return new ClientShortcutBarObservation
        {
            Bar = bar,
            IsAvailable = true,
            Status = "Available",
            BarAddress = barAddress,
            CurrentGroup = currentGroup,
            Group0Count = group0Count,
            Group1Count = group1Count,
            ToggleWidgetAddress = toggleWidgetAddress,
            Slots = slots,
        };
    }

    private bool TryObserveSlots(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint barAddress,
        int bar,
        int currentGroup,
        NativeShortcutCatalogs catalogs,
        ClientLocalPlayerObservation localPlayer,
        out IReadOnlyList<ClientShortcutSlotObservation> slots,
        out string error)
    {
        var visibleButtons = new uint[ShortcutButtonsPerGroup];

        for (var button = 0;
             button < ShortcutButtonsPerGroup;
             button++)
        {
            _ = TryReadPointer(
                memory,
                barAddress,
                ShortcutBarVisibleButtonsOffset +
                (uint)button * ShortcutBarPointerStride,
                $"ShortcutBar{bar}.VisibleButton{button}",
                out visibleButtons[button],
                out _);
        }

        var observedSlots = new List<ClientShortcutSlotObservation>(
            capacity: 2 * ShortcutButtonsPerGroup);

        for (var group = 0; group <= 1; group++)
        {
            var groupOffset = group == 0
                ? ShortcutBarGroup0GadgetsOffset
                : ShortcutBarGroup1GadgetsOffset;

            for (var button = 0;
                 button < ShortcutButtonsPerGroup;
                 button++)
            {
                var offset = groupOffset +
                             (uint)button * ShortcutBarPointerStride;

                if (!TryReadPointer(
                        memory,
                        barAddress,
                        offset,
                        $"ShortcutBar{bar}.Group{group}.Button{button}.Gadget",
                        out var gadgetAddress,
                        out error))
                {
                    slots = [];
                    return false;
                }

                observedSlots.Add(
                    this.ObserveSlot(
                        memory,
                        moduleBaseAddress,
                        bar,
                        group,
                        button,
                        currentGroup,
                        visibleButtons[button],
                        gadgetAddress,
                        catalogs,
                        localPlayer));
            }
        }

        slots = observedSlots;
        error = "";
        return true;
    }

    private ClientShortcutSlotObservation ObserveSlot(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        int bar,
        int group,
        int button,
        int currentGroup,
        uint visibleButtonAddress,
        uint gadgetAddress,
        NativeShortcutCatalogs catalogs,
        ClientLocalPlayerObservation localPlayer)
    {
        var isVisible = currentGroup == group;
        uint visibleButtonActiveGadgetAddress = 0;
        bool? visibleButtonGadgetMatches = null;

        if (isVisible &&
            visibleButtonAddress != 0 &&
            TryReadPointer(
                memory,
                visibleButtonAddress,
                VisibleButtonActiveGadgetOffset,
                $"ShortcutBar{bar}.VisibleButton{button}.ActiveGadget",
                out visibleButtonActiveGadgetAddress,
                out _))
        {
            visibleButtonGadgetMatches =
                visibleButtonActiveGadgetAddress == gadgetAddress;
        }

        if (gadgetAddress == 0)
        {
            return new ClientShortcutSlotObservation
            {
                Bar = bar,
                Group = group,
                Button = button,
                IsVisible = isVisible,
                VisibleButtonAddress = visibleButtonAddress,
                VisibleButtonActiveGadgetAddress =
                    visibleButtonActiveGadgetAddress,
                VisibleButtonGadgetMatches =
                    visibleButtonGadgetMatches,
                Status = "Empty shortcut slot",
            };
        }

        if (!TryReadPointer(
                memory,
                gadgetAddress,
                GadgetRendererOffset,
                "ShortcutGadget.Renderer",
                out var rendererAddress,
                out var error))
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                error);
        }

        if (rendererAddress == 0)
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                "Shortcut renderer is null");
        }

        if (!TryReadPointer(
                memory,
                gadgetAddress,
                GadgetPayloadOffset,
                "ShortcutGadget.Payload",
                out var payloadAddress,
                out error))
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                error) with
            {
                RendererAddress = rendererAddress,
            };
        }

        if (payloadAddress == 0)
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                "Shortcut payload is null") with
            {
                RendererAddress = rendererAddress,
            };
        }

        if (!TryReadPointer(
                memory,
                rendererAddress,
                ObjectVTableOffset,
                "ShortcutRenderer.VTable",
                out var rendererVTableAddress,
                out error) ||
            !TryReadPointer(
                memory,
                payloadAddress,
                ObjectVTableOffset,
                "ShortcutPayload.VTable",
                out var payloadVTableAddress,
                out error))
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                error) with
            {
                RendererAddress = rendererAddress,
                PayloadAddress = payloadAddress,
            };
        }

        var rendererVTableRva = GetImageRva(
            moduleBaseAddress,
            rendererVTableAddress);
        var payloadVTableRva = GetImageRva(
            moduleBaseAddress,
            payloadVTableAddress);

        var kind = ClassifyKind(
            rendererVTableRva,
            payloadVTableRva);

        if (!TryReadInt32(
                memory,
                payloadAddress,
                PayloadPrimaryIdentityOffset,
                "ShortcutPayload.PrimaryIdentity",
                out var primaryIdentity,
                out error))
        {
            return CreateUnreadableSlot(
                bar,
                group,
                button,
                isVisible,
                gadgetAddress,
                visibleButtonAddress,
                visibleButtonActiveGadgetAddress,
                visibleButtonGadgetMatches,
                error) with
            {
                RendererAddress = rendererAddress,
                PayloadAddress = payloadAddress,
                RendererVTableRva = rendererVTableRva,
                PayloadVTableRva = payloadVTableRva,
                Kind = kind,
            };
        }

        int? secondaryIdentity = null;

        if (kind == ClientShortcutKind.Skill &&
            TryReadInt32(
                memory,
                payloadAddress,
                PayloadSecondaryIdentityOffset,
                "ShortcutPayload.ResolvedAbilityIdentity",
                out var observedSecondaryIdentity,
                out _))
        {
            secondaryIdentity = observedSecondaryIdentity;
        }

        var resolution = kind switch
        {
            ClientShortcutKind.Skill => ResolveSkillIdentity(
                memory,
                catalogs,
                primaryIdentity,
                secondaryIdentity,
                localPlayer),
            ClientShortcutKind.Equipment => ResolveInventoryIdentity(
                primaryIdentity,
                localPlayer),
            _ => ShortcutIdentityResolution.Unavailable(
                "Renderer/payload vtable pair is not recognized"),
        };

        var resolvedKind = resolution.Kind == ClientShortcutKind.Unknown
            ? kind
            : resolution.Kind;

        var status = resolution.Status;

        if (isVisible &&
            visibleButtonGadgetMatches == false)
        {
            status = string.Concat(
                status,
                string.IsNullOrWhiteSpace(status) ? "" : "; ",
                "visible button points at a different gadget");
        }

        return new ClientShortcutSlotObservation
        {
            Bar = bar,
            Group = group,
            Button = button,
            IsVisible = isVisible,
            GadgetAddress = gadgetAddress,
            RendererAddress = rendererAddress,
            PayloadAddress = payloadAddress,
            RendererVTableRva = rendererVTableRva,
            PayloadVTableRva = payloadVTableRva,
            VisibleButtonAddress = visibleButtonAddress,
            VisibleButtonActiveGadgetAddress =
                visibleButtonActiveGadgetAddress,
            VisibleButtonGadgetMatches =
                visibleButtonGadgetMatches,
            Kind = resolvedKind,
            PrimaryIdentity = primaryIdentity,
            SecondaryIdentity = secondaryIdentity,
            FamilyName = resolution.FamilyName,
            ResolvedName = resolution.ResolvedName,
            ResolvedNameIsExact = resolution.ResolvedNameIsExact,
            IconResourceName = resolution.IconResourceName,
            TintRed = resolution.TintRed,
            TintGreen = resolution.TintGreen,
            TintBlue = resolution.TintBlue,
            InventoryCollection = resolution.InventoryCollection,
            ItemTemplateId = resolution.ItemTemplateId,
            EquippedItemTemplateIdCandidate =
                resolution.EquippedItemTemplateIdCandidate,
            EquippedItemNameCandidate =
                resolution.EquippedItemNameCandidate,
            CargoItemTemplateIdCandidate =
                resolution.CargoItemTemplateIdCandidate,
            CargoItemNameCandidate =
                resolution.CargoItemNameCandidate,
            Status = status,
        };
    }

    private static ClientShortcutSlotObservation CreateUnreadableSlot(
        int bar,
        int group,
        int button,
        bool isVisible,
        uint gadgetAddress,
        uint visibleButtonAddress,
        uint visibleButtonActiveGadgetAddress,
        bool? visibleButtonGadgetMatches,
        string status)
    {
        return new ClientShortcutSlotObservation
        {
            Bar = bar,
            Group = group,
            Button = button,
            IsVisible = isVisible,
            GadgetAddress = gadgetAddress,
            VisibleButtonAddress = visibleButtonAddress,
            VisibleButtonActiveGadgetAddress =
                visibleButtonActiveGadgetAddress,
            VisibleButtonGadgetMatches =
                visibleButtonGadgetMatches,
            Status = status,
        };
    }

    private static ClientShortcutKind ClassifyKind(
        uint rendererVTableRva,
        uint payloadVTableRva)
    {
        if (rendererVTableRva == SkillRendererVTableRva &&
            payloadVTableRva == SkillPayloadVTableRva)
        {
            return ClientShortcutKind.Skill;
        }

        if (rendererVTableRva == EquipmentRendererVTableRva &&
            payloadVTableRva == EquipmentPayloadVTableRva)
        {
            return ClientShortcutKind.Equipment;
        }

        return ClientShortcutKind.Unknown;
    }

    private static NativeShortcutCatalogs ObserveNativeCatalogs(
        ProcessMemoryReader memory,
        uint moduleBaseAddress)
    {
        var skillAvailable = TryReadPointerVectorBounds(
            memory,
            moduleBaseAddress,
            StaticSkillCatalogBeginRva,
            StaticSkillCatalogEndRva,
            MaximumSkillDefinitionCount,
            "skill-family catalog",
            out var skillBegin,
            out var skillEnd,
            out var skillCount,
            out var skillStatus);

        var abilityAvailable = TryReadPointerVectorBounds(
            memory,
            moduleBaseAddress,
            StaticAbilityCatalogBeginRva,
            StaticAbilityCatalogEndRva,
            MaximumAbilityDefinitionCount,
            "resolved-ability catalog",
            out var abilityBegin,
            out var abilityEnd,
            out var abilityCount,
            out var abilityStatus);

        return new NativeShortcutCatalogs
        {
            SkillCatalogIsAvailable = skillAvailable,
            SkillCatalogStatus = skillStatus,
            SkillVectorBeginAddress = skillBegin,
            SkillVectorEndAddress = skillEnd,
            SkillDefinitionCount = skillCount,
            AbilityCatalogIsAvailable = abilityAvailable,
            AbilityCatalogStatus = abilityStatus,
            AbilityVectorBeginAddress = abilityBegin,
            AbilityVectorEndAddress = abilityEnd,
            AbilityDefinitionCount = abilityCount,
        };
    }

    private static ShortcutIdentityResolution ResolveSkillIdentity(
        ProcessMemoryReader memory,
        NativeShortcutCatalogs catalogs,
        int primaryIdentity,
        int? secondaryIdentity,
        ClientLocalPlayerObservation localPlayer)
    {
        if (primaryIdentity < 0)
        {
            return ShortcutIdentityResolution.Unavailable(
                "Skill-family index is unavailable");
        }

        List<string> status = [];

        var liveSkill = localPlayer.CharacterProgression
            .Skills
            .Skills
            .FirstOrDefault(skill => skill.Index == primaryIdentity);

        var familyName = liveSkill?.Name ?? "";
        var resolvedName = familyName;
        var iconResourceName = "";
        var resolvedNameIsExact = false;
        float? tintRed = null;
        float? tintGreen = null;
        float? tintBlue = null;

        if (!catalogs.SkillCatalogIsAvailable)
        {
            status.Add(catalogs.SkillCatalogStatus);
        }
        else if (TryReadCatalogRecord(
                     memory,
                     catalogs.SkillVectorBeginAddress,
                     catalogs.SkillDefinitionCount,
                     primaryIdentity,
                     "skill-family",
                     out var skillFamilyRecordAddress,
                     out var skillRecordStatus))
        {
            if (TryReadPointerStringField(
                    memory,
                    skillFamilyRecordAddress,
                    SkillDefinitionNameOffset,
                    MaximumSkillNameLength,
                    "skill-family name",
                    out var catalogFamilyName,
                    out var familyNameStatus))
            {
                familyName = catalogFamilyName;
                resolvedName = catalogFamilyName;
                status.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"family {primaryIdentity}='{catalogFamilyName}'"));
            }
            else
            {
                status.Add(familyNameStatus);
            }

            if (TryReadPointerStringField(
                    memory,
                    skillFamilyRecordAddress,
                    SkillDefinitionIconResourceOffset,
                    MaximumIconResourceNameLength,
                    "skill-family icon resource",
                    out iconResourceName,
                    out var iconStatus))
            {
                status.Add(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"icon='{iconResourceName}'"));
            }
            else
            {
                status.Add(iconStatus);
            }
        }
        else
        {
            status.Add(skillRecordStatus);
        }

        if (string.IsNullOrWhiteSpace(familyName) &&
            liveSkill != null)
        {
            familyName = liveSkill.Name;
            resolvedName = liveSkill.Name;
            status.Add(
                "family name fell back to the live progression observation");
        }

        if (secondaryIdentity is >= 0)
        {
            if (!catalogs.AbilityCatalogIsAvailable)
            {
                status.Add(catalogs.AbilityCatalogStatus);
            }
            else if (TryReadCatalogRecord(
                         memory,
                         catalogs.AbilityVectorBeginAddress,
                         catalogs.AbilityDefinitionCount,
                         secondaryIdentity.Value,
                         "resolved ability",
                         out var resolvedAbilityRecordAddress,
                         out var abilityRecordStatus))
            {
                if (TryReadPointerStringField(
                        memory,
                        resolvedAbilityRecordAddress,
                        AbilityDefinitionNameOffset,
                        MaximumAbilityNameLength,
                        "resolved ability name",
                        out var abilityName,
                        out var abilityNameStatus))
                {
                    resolvedName = abilityName;
                    resolvedNameIsExact = true;
                    status.Add(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"ability {secondaryIdentity.Value}='{abilityName}'"));
                }
                else
                {
                    status.Add(abilityNameStatus);
                }

                tintRed = TryReadFiniteSingleField(
                    memory,
                    resolvedAbilityRecordAddress,
                    AbilityDefinitionTintRedOffset);
                tintGreen = TryReadFiniteSingleField(
                    memory,
                    resolvedAbilityRecordAddress,
                    AbilityDefinitionTintGreenOffset);
                tintBlue = TryReadFiniteSingleField(
                    memory,
                    resolvedAbilityRecordAddress,
                    AbilityDefinitionTintBlueOffset);
            }
            else
            {
                status.Add(abilityRecordStatus);
            }
        }
        else
        {
            status.Add(
                "resolved ability ID is unavailable; using the family name");
        }

        return new ShortcutIdentityResolution
        {
            Kind = ClientShortcutKind.Skill,
            FamilyName = familyName,
            ResolvedName = resolvedName,
            ResolvedNameIsExact = resolvedNameIsExact,
            IconResourceName = iconResourceName,
            TintRed = tintRed,
            TintGreen = tintGreen,
            TintBlue = tintBlue,
            Status = status.Count == 0
                ? "Skill identity was recognized but catalog metadata was unavailable"
                : string.Join("; ", status),
        };
    }

    private static ShortcutIdentityResolution ResolveInventoryIdentity(
        int primaryIdentity,
        ClientLocalPlayerObservation localPlayer)
    {
        if (primaryIdentity < 0)
        {
            return ShortcutIdentityResolution.Unavailable(
                "Inventory slot index is unavailable");
        }

        var equippedItem = localPlayer.Inventory.EquippedSlots
            .FirstOrDefault(slot =>
                slot.Slot == primaryIdentity &&
                slot.IsOccupied);
        var cargoItem = localPlayer.Inventory.CargoSlots
            .FirstOrDefault(slot =>
                slot.Slot == primaryIdentity &&
                slot.IsOccupied);

        var equippedTemplateId = equippedItem?.ItemTemplateId;
        var cargoTemplateId = cargoItem?.ItemTemplateId;
        var equippedName = ResolveItemName(equippedTemplateId);
        var cargoName = ResolveItemName(cargoTemplateId);

        if (equippedItem != null &&
            cargoItem == null)
        {
            return new ShortcutIdentityResolution
            {
                Kind = ClientShortcutKind.Equipment,
                InventoryCollection =
                    ClientInventoryCollectionKind.Equipped,
                ItemTemplateId = equippedTemplateId,
                ResolvedName = equippedName,
                EquippedItemTemplateIdCandidate = equippedTemplateId,
                EquippedItemNameCandidate = equippedName,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"equipped slot {primaryIdentity} -> template {equippedTemplateId} ('{equippedName}')"),
            };
        }

        if (cargoItem != null &&
            equippedItem == null)
        {
            return new ShortcutIdentityResolution
            {
                Kind = ClientShortcutKind.Cargo,
                InventoryCollection =
                    ClientInventoryCollectionKind.Cargo,
                ItemTemplateId = cargoTemplateId,
                ResolvedName = cargoName,
                CargoItemTemplateIdCandidate = cargoTemplateId,
                CargoItemNameCandidate = cargoName,
                Status = string.Create(
                    CultureInfo.InvariantCulture,
                    $"cargo slot {primaryIdentity} -> template {cargoTemplateId} ('{cargoName}')"),
            };
        }

        if (equippedItem != null &&
            cargoItem != null)
        {
            return new ShortcutIdentityResolution
            {
                Kind = ClientShortcutKind.Equipment,
                EquippedItemTemplateIdCandidate = equippedTemplateId,
                EquippedItemNameCandidate = equippedName,
                CargoItemTemplateIdCandidate = cargoTemplateId,
                CargoItemNameCandidate = cargoName,
                Status = string.Concat(
                    "inventory slot ",
                    primaryIdentity.ToString(CultureInfo.InvariantCulture),
                    " is occupied in both equipment and cargo; ",
                    "deferred collection choice to shortcut metadata"),
            };
        }

        return new ShortcutIdentityResolution
        {
            Kind = ClientShortcutKind.Equipment,
            Status = string.Create(
                CultureInfo.InvariantCulture,
                $"No occupied equipment or cargo item was observed at slot {primaryIdentity}"),
        };
    }

    private static string ResolveItemName(int? itemTemplateId)
    {
        if (itemTemplateId is not > 0)
        {
            return "";
        }

        return ClientItemTemplateNameResolver.GetKnownName(
                   itemTemplateId) ??
               string.Create(
                   CultureInfo.InvariantCulture,
                   $"Item {itemTemplateId.Value}");
    }

    private static bool TryReadPointerVectorBounds(
        ProcessMemoryReader memory,
        uint moduleBaseAddress,
        uint beginPointerRva,
        uint endPointerRva,
        int maximumCount,
        string description,
        out uint begin,
        out uint end,
        out int count,
        out string status)
    {
        begin = 0;
        end = 0;
        count = 0;
        status = "";

        if (!TryAddAddress(
                moduleBaseAddress,
                beginPointerRva,
                out var beginPointerAddress) ||
            !TryAddAddress(
                moduleBaseAddress,
                endPointerRva,
                out var endPointerAddress) ||
            !memory.TryReadUInt32(
                beginPointerAddress,
                out begin) ||
            !memory.TryReadUInt32(
                endPointerAddress,
                out end))
        {
            status = $"Could not read the {description} vector bounds";
            return false;
        }

        if (begin == 0 ||
            end <= begin ||
            (end - begin) % sizeof(uint) != 0)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"The {description} vector bounds were invalid: 0x{begin:X8}..0x{end:X8}");
            return false;
        }

        var unsignedCount =
            (end - begin) /
            sizeof(uint);

        if (unsignedCount == 0 ||
            unsignedCount > (uint)maximumCount)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"The {description} exposed unexpected entry count {unsignedCount}");
            return false;
        }

        count = checked((int)unsignedCount);
        status = string.Create(
            CultureInfo.InvariantCulture,
            $"Resolved {count} entries at 0x{begin:X8}..0x{end:X8}");
        return true;
    }

    private static bool TryReadCatalogRecord(
        ProcessMemoryReader memory,
        uint vectorBeginAddress,
        int vectorCount,
        int index,
        string description,
        out uint recordAddress,
        out string status)
    {
        recordAddress = 0;
        status = "";

        if (index < 0 ||
            index >= vectorCount)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"{description} index {index} is outside the {vectorCount}-entry native catalog");
            return false;
        }

        if (!TryAddAddress(
                vectorBeginAddress,
                checked((uint)(index * sizeof(uint))),
                out var entryAddress) ||
            !memory.TryReadUInt32(
                entryAddress,
                out recordAddress) ||
            recordAddress < MinimumProcessAddress)
        {
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {description} record {index} from 0x{entryAddress:X8}");
            return false;
        }

        return true;
    }

    private static bool TryReadPointerStringField(
        ProcessMemoryReader memory,
        uint recordAddress,
        uint fieldOffset,
        int maximumLength,
        string description,
        out string value,
        out string status)
    {
        value = "";
        status = "";

        if (!TryAddAddress(
                recordAddress,
                fieldOffset,
                out var fieldAddress) ||
            !memory.TryReadUInt32(
                fieldAddress,
                out var stringAddress) ||
            stringAddress < MinimumProcessAddress ||
            !memory.TryReadNullTerminatedLatin1String(
                stringAddress,
                maximumLength,
                out value) ||
            string.IsNullOrWhiteSpace(value) ||
            value.Any(character =>
                character < 0x20 ||
                character == 0x7f))
        {
            value = "";
            status = string.Create(
                CultureInfo.InvariantCulture,
                $"Could not read {description} through record 0x{recordAddress:X8} +0x{fieldOffset:X}");
            return false;
        }

        return true;
    }

    private static float? TryReadFiniteSingleField(
        ProcessMemoryReader memory,
        uint recordAddress,
        uint fieldOffset)
    {
        if (!TryAddAddress(
                recordAddress,
                fieldOffset,
                out var fieldAddress) ||
            !memory.TryReadUInt32(
                fieldAddress,
                out var rawValue))
        {
            return null;
        }

        var value = BitConverter.Int32BitsToSingle(
            unchecked((int)rawValue));

        return float.IsFinite(value)
            ? value
            : null;
    }

    private static bool TryReadPointer(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string name,
        out uint value,
        out string error)
    {
        value = 0;
        error = "";

        if (!TryAddAddress(
                baseAddress,
                offset,
                out var address))
        {
            error = $"{name} address overflow";
            return false;
        }

        if (!memory.TryReadUInt32(address, out value))
        {
            error = $"Could not read {name} at 0x{address:X8}";
            return false;
        }

        return true;
    }

    private static bool TryReadInt32(
        ProcessMemoryReader memory,
        uint baseAddress,
        uint offset,
        string name,
        out int value,
        out string error)
    {
        value = 0;
        error = "";

        if (!TryReadPointer(
                memory,
                baseAddress,
                offset,
                name,
                out var rawValue,
                out error))
        {
            return false;
        }

        value = unchecked((int)rawValue);
        return true;
    }

    private static bool TryValidateRelocatedAddress(
        uint moduleBaseAddress,
        uint expectedRva,
        uint actualAddress,
        string name,
        out string error)
    {
        error = "";

        if (!TryAddAddress(
                moduleBaseAddress,
                expectedRva,
                out var expectedAddress))
        {
            error = $"{name} expected address overflow";
            return false;
        }

        if (actualAddress == expectedAddress)
        {
            return true;
        }

        error = $"Unexpected {name} 0x{actualAddress:X8}; expected 0x{expectedAddress:X8}";
        return false;
    }

    private static uint GetImageRva(
        uint moduleBaseAddress,
        uint address)
    {
        if (address < moduleBaseAddress)
        {
            return 0;
        }

        var rva = address - moduleBaseAddress;

        return rva < MaximumExpectedImageSpan
            ? rva
            : 0;
    }

    private static bool TryAddAddress(
        uint address,
        uint offset,
        out uint result)
    {
        var expanded = (ulong)address + offset;

        if (expanded > uint.MaxValue)
        {
            result = 0;
            return false;
        }

        result = unchecked((uint)expanded);
        return true;
    }

    private sealed record NativeShortcutCatalogs
    {
        public bool SkillCatalogIsAvailable { get; init; }

        public string SkillCatalogStatus { get; init; } = "";

        public uint SkillVectorBeginAddress { get; init; }

        public uint SkillVectorEndAddress { get; init; }

        public int SkillDefinitionCount { get; init; }

        public bool AbilityCatalogIsAvailable { get; init; }

        public string AbilityCatalogStatus { get; init; } = "";

        public uint AbilityVectorBeginAddress { get; init; }

        public uint AbilityVectorEndAddress { get; init; }

        public int AbilityDefinitionCount { get; init; }
    }

    private sealed record ShortcutIdentityResolution
    {
        public ClientShortcutKind Kind { get; init; }

        public string FamilyName { get; init; } = "";

        public string ResolvedName { get; init; } = "";

        public bool ResolvedNameIsExact { get; init; }

        public string IconResourceName { get; init; } = "";

        public float? TintRed { get; init; }

        public float? TintGreen { get; init; }

        public float? TintBlue { get; init; }

        public ClientInventoryCollectionKind? InventoryCollection { get; init; }

        public int? ItemTemplateId { get; init; }

        public int? EquippedItemTemplateIdCandidate { get; init; }

        public string EquippedItemNameCandidate { get; init; } = "";

        public int? CargoItemTemplateIdCandidate { get; init; }

        public string CargoItemNameCandidate { get; init; } = "";

        public string Status { get; init; } = "";

        public static ShortcutIdentityResolution Unavailable(string status)
        {
            return new ShortcutIdentityResolution
            {
                Status = status,
            };
        }
    }
}
