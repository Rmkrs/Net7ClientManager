// ReSharper disable GrammarMistakeInComment
namespace Net7ClientManager.Observations.Observers;

using System.Globalization;
using Net7ClientManager.Observations.Models;

internal static class ClientStarbaseInteractionCatalog
{
    /*
     * SClient +0x135C owns StarbaseView.
     *
     * StarbaseView +0x184..+0x1A4 is a contiguous nine-pointer
     * interface strip. The first eight entries are opened through
     * StarbaseView.CurrentInterfaceCommand (+0x5C) and retained at
     * StarbaseView.CurrentInterface (+0x64). The ninth entry is the
     * persistent player-trade interface and is not part of that command
     * switch.
     *
     * Every interface uses +0x7C as its visible/active byte. TalkTree
     * additionally owns VendorTradeController at +0x160; a non-zero
     * child identifies VendorTrade even while the outer TalkTree flag is
     * transitioning.
     */
    public const uint PanelActiveFlagOffset = 0x7c;

    public const uint TalkTreeVendorTradeControllerOffset = 0x160;

    public static IReadOnlyList<ClientStarbasePanelDefinition> Panels { get; } =
    [
        new(
            Slot: 0,
            Kind: ClientStarbasePanelKind.Refining,
            DisplayName: "Refining",
            ViewFieldOffset: 0x184,
            CurrentInterfaceCommand: 5,
            FacilityType: 0,
            IsPersistent: false),
        new(
            Slot: 1,
            Kind: ClientStarbasePanelKind.Analyze,
            DisplayName: "Analyze",
            ViewFieldOffset: 0x188,
            CurrentInterfaceCommand: 6,
            FacilityType: 1,
            IsPersistent: false),
        new(
            Slot: 2,
            Kind: ClientStarbasePanelKind.Manufacturing,
            DisplayName: "Manufacturing",
            ViewFieldOffset: 0x18c,
            CurrentInterfaceCommand: 7,
            FacilityType: 2,
            IsPersistent: false),
        new(
            Slot: 3,
            Kind: ClientStarbasePanelKind.TalkTree,
            DisplayName: "TalkTree / Vendor Trade",
            ViewFieldOffset: 0x190,
            CurrentInterfaceCommand: 0x0b,
            FacilityType: null,
            IsPersistent: false),
        new(
            Slot: 4,
            Kind: ClientStarbasePanelKind.JobsTerminal,
            DisplayName: "Jobs Terminal",
            ViewFieldOffset: 0x194,
            CurrentInterfaceCommand: 8,
            FacilityType: 3,
            IsPersistent: false),
        new(
            Slot: 5,
            Kind: ClientStarbasePanelKind.IntergalacticNet,
            DisplayName: "Intergalactic Net",
            ViewFieldOffset: 0x198,
            CurrentInterfaceCommand: 9,
            FacilityType: 4,
            IsPersistent: false),
        new(
            Slot: 6,
            Kind: ClientStarbasePanelKind.CustomizeAvatar,
            DisplayName: "Customize Avatar",
            ViewFieldOffset: 0x19c,
            CurrentInterfaceCommand: 0x0e,
            FacilityType: 6,
            IsPersistent: false),
        new(
            Slot: 7,
            Kind: ClientStarbasePanelKind.CustomizeShip,
            DisplayName: "Customize Ship",
            ViewFieldOffset: 0x1a0,
            CurrentInterfaceCommand: 0x0d,
            FacilityType: 5,
            IsPersistent: false),
        new(
            Slot: 8,
            Kind: ClientStarbasePanelKind.PlayerTrade,
            DisplayName: "Player Trade",
            ViewFieldOffset: 0x1a4,
            CurrentInterfaceCommand: null,
            FacilityType: null,
            IsPersistent: true),
    ];

    public static ClientStarbasePanelDefinition? GetPanelByCurrentInterfaceCommand(
        int command)
    {
        return Panels.FirstOrDefault(
            panel =>
                panel.CurrentInterfaceCommand == command);
    }

    public static ClientStarbasePanelDefinition? GetPanelByFacilityType(
        int facilityType)
    {
        return Panels.FirstOrDefault(
            panel =>
                panel.FacilityType == facilityType);
    }

    public static ClientStarbaseInteractionKind GetPanelInteractionKind(
        ClientStarbasePanelKind panelKind,
        uint childAddress)
    {
        return panelKind switch
        {
            ClientStarbasePanelKind.Refining =>
                ClientStarbaseInteractionKind.Refining,
            ClientStarbasePanelKind.Analyze =>
                ClientStarbaseInteractionKind.Analyze,
            ClientStarbasePanelKind.Manufacturing =>
                ClientStarbaseInteractionKind.Manufacturing,
            ClientStarbasePanelKind.TalkTree =>
                childAddress != 0
                    ? ClientStarbaseInteractionKind.VendorTrade
                    : ClientStarbaseInteractionKind.TalkTree,
            ClientStarbasePanelKind.JobsTerminal =>
                ClientStarbaseInteractionKind.JobsTerminal,
            ClientStarbasePanelKind.IntergalacticNet =>
                ClientStarbaseInteractionKind.IntergalacticNet,
            ClientStarbasePanelKind.CustomizeAvatar =>
                ClientStarbaseInteractionKind.CustomizeAvatar,
            ClientStarbasePanelKind.CustomizeShip =>
                ClientStarbaseInteractionKind.CustomizeShip,
            ClientStarbasePanelKind.PlayerTrade =>
                ClientStarbaseInteractionKind.PlayerTrade,
            _ =>
                ClientStarbaseInteractionKind.None,
        };
    }

    public static ClientStarbaseInteractionKind GetCurrentInterfaceKind(
        int command,
        uint talkTreeVendorTradeControllerAddress)
    {
        var panel =
            GetPanelByCurrentInterfaceCommand(
                command);

        return panel == null
            ? ClientStarbaseInteractionKind.None
            : GetPanelInteractionKind(
                panel.Kind,
                panel.Kind == ClientStarbasePanelKind.TalkTree
                    ? talkTreeVendorTradeControllerAddress
                    : 0);
    }


    public static string GetInteractionDisplayName(
        ClientStarbaseInteractionKind kind)
    {
        return kind switch
        {
            ClientStarbaseInteractionKind.None => "None",
            ClientStarbaseInteractionKind.Refining => "Refining",
            ClientStarbaseInteractionKind.Analyze => "Analyze",
            ClientStarbaseInteractionKind.Manufacturing => "Manufacturing",
            ClientStarbaseInteractionKind.TalkTree => "TalkTree",
            ClientStarbaseInteractionKind.VendorTrade => "Vendor Trade",
            ClientStarbaseInteractionKind.JobsTerminal => "Jobs Terminal",
            ClientStarbaseInteractionKind.IntergalacticNet => "Intergalactic Net",
            ClientStarbaseInteractionKind.CustomizeAvatar => "Customize Avatar",
            ClientStarbaseInteractionKind.CustomizeShip => "Customize Ship",
            ClientStarbaseInteractionKind.PlayerTrade => "Player Trade",
            ClientStarbaseInteractionKind.PlayerInteractionMenu => "Player interaction menu",
            ClientStarbaseInteractionKind.Ambiguous => "Ambiguous interaction",
            _ => kind.ToString(),
        };
    }

    public static string GetFacilityTypeName(
        int facilityType)
    {
        return facilityType switch
        {
            0 => "Refining",
            1 => "Analyze",
            2 => "Manufacturing",
            3 => "Jobs Terminal",
            4 => "Intergalactic Net",
            5 => "Customize Ship",
            6 => "Customize Avatar",
            7 => "Training",
            9 => "Facility type 9",
            _ => string.Create(CultureInfo.InvariantCulture, $"Facility type {facilityType}"),
        };
    }

    public static ClientStarbaseInteractionKind GetFacilityInteractionKind(
        int facilityType)
    {
        var panel =
            GetPanelByFacilityType(
                facilityType);

        return panel == null
            ? ClientStarbaseInteractionKind.None
            : GetPanelInteractionKind(
                panel.Kind,
                0);
    }
}
