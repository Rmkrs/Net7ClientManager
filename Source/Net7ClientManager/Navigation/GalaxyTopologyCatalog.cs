// ReSharper disable StringLiteralTypo
namespace Net7ClientManager.Navigation;

public static class GalaxyTopologyCatalog
{
    public static GalaxyTopology Instance { get; } =
        new(CreateSectors());

    private static IReadOnlyCollection<GalaxySectorDefinition> CreateSectors()
    {
        return
        [
            new GalaxySectorDefinition
            {
                Key = "aganju",
                Name = "Aganju",
                SystemName = "61 Cygni",
                Connections = ["inverness"],
            },
            new GalaxySectorDefinition
            {
                Key = "zweihander-planet",
                Name = "Jagerstadt",
                SystemName = "Alpha Centauri",
                Aliases =
                [
                    "Zweihander Planet",
                    "Planet Zweihander",
                ],
                Connections = ["zweihander"],
            },
            new GalaxySectorDefinition
            {
                Key = "witberg",
                Name = "Witberg",
                SystemName = "Alpha Centauri",
                Connections = ["zweihander", "freya"],
            },
            new GalaxySectorDefinition
            {
                Key = "zweihander",
                Name = "Zweihander",
                SystemName = "Alpha Centauri",
                Connections = ["witberg", "zweihander-planet", "luna"],
            },
            new GalaxySectorDefinition
            {
                Key = "altair-iii",
                Name = "Altair III",
                SystemName = "Altair",
                Aliases = ["Altair (III)"],
                Connections = ["mars-gamma", "endriago", "nostrand-vor"],
            },
            new GalaxySectorDefinition
            {
                Key = "antares-frontier",
                Name = "Antares Frontier",
                SystemName = "Antares",
                Connections = ["vishaos-cove"],
            },
            new GalaxySectorDefinition
            {
                Key = "aragoth-prime",
                Name = "Aragoth Prime",
                SystemName = "Aragoth",
                Connections = ["muspelheim", "valkyrie-twins", "varens-girdle"],
            },
            new GalaxySectorDefinition
            {
                Key = "fenris",
                Name = "Fenris",
                SystemName = "Aragoth",
                Connections = ["varens-girdle", "valkyrie-twins"],
            },
            new GalaxySectorDefinition
            {
                Key = "freya",
                Name = "Freya",
                SystemName = "Aragoth",
                Connections = ["jotunheim", "ragnarok", "nifleheim-cloud", "witberg", "akerons-gate", "adriel-prime"],
            },
            new GalaxySectorDefinition
            {
                Key = "jotunheim",
                Name = "Jotunheim",
                SystemName = "Aragoth",
                Connections = ["ragnarok", "odin-rex", "freya"],
            },
            new GalaxySectorDefinition
            {
                Key = "muspelheim",
                Name = "Muspelheim",
                SystemName = "Aragoth",
                Connections = ["aragoth-prime", "odins-belt"],
            },
            new GalaxySectorDefinition
            {
                Key = "nifleheim-cloud",
                Name = "Nifleheim Cloud",
                SystemName = "Aragoth",
                Connections = ["freya"],
            },
            new GalaxySectorDefinition
            {
                Key = "odins-belt",
                Name = "Odin's Belt",
                SystemName = "Aragoth",
                Connections = ["odin-rex", "lagarto", "muspelheim"],
            },
            new GalaxySectorDefinition
            {
                Key = "odin-rex",
                Name = "Odin Rex",
                SystemName = "Aragoth",
                Connections = ["odins-belt", "jotunheim"],
            },
            new GalaxySectorDefinition
            {
                Key = "ragnarok",
                Name = "Ragnarok",
                SystemName = "Aragoth",
                Connections = ["jotunheim", "freya"],
            },
            new GalaxySectorDefinition
            {
                Key = "varens-girdle",
                Name = "Varen's Girdle",
                SystemName = "Aragoth",
                Connections = ["aragoth-prime", "fenris"],
            },
            new GalaxySectorDefinition
            {
                Key = "valkyrie-twins",
                Name = "Valkyrie Twins",
                SystemName = "Aragoth",
                Connections = ["aragoth-prime", "fenris"],
            },
            new GalaxySectorDefinition
            {
                Key = "carpenter",
                Name = "Carpenter",
                SystemName = "Beta Hydri",
                Connections = ["glenn", "slayton", "new-edinburgh", "shepard"],
            },
            new GalaxySectorDefinition
            {
                Key = "cooper",
                Name = "Cooper",
                SystemName = "Beta Hydri",
                Connections = ["grissom"],
            },
            new GalaxySectorDefinition
            {
                Key = "glenn",
                Name = "Glenn",
                SystemName = "Beta Hydri",
                Connections = ["slayton", "carpenter", "swooping-eagle", "saturn"],
            },
            new GalaxySectorDefinition
            {
                Key = "grissom",
                Name = "Grissom",
                SystemName = "Beta Hydri",
                Connections = ["grissom-planet", "shepard", "cooper"],
            },
            new GalaxySectorDefinition
            {
                Key = "grissom-planet",
                Name = "Grissom Meteorological Site",
                SystemName = "Beta Hydri",
                Aliases =
                [
                    "Grissom Planet",
                    "Planet Grissom",
                ],
                Connections = ["grissom"],
            },
            new GalaxySectorDefinition
            {
                Key = "glorys-orbit",
                Name = "Glory's Orbit",
                SystemName = "Beta Hydri",
                Aliases = ["Schirra & Glory's Orbit"],
                Connections = ["slayton"],
            },
            new GalaxySectorDefinition
            {
                Key = "shepard",
                Name = "Shepard",
                SystemName = "Beta Hydri",
                Connections = ["carpenter", "grissom"],
            },
            new GalaxySectorDefinition
            {
                Key = "slayton",
                Name = "Slayton",
                SystemName = "Beta Hydri",
                Connections = ["glorys-orbit", "carpenter", "glenn"],
            },
            new GalaxySectorDefinition
            {
                Key = "dahin",
                Name = "Dahin",
                SystemName = "Capella",
                Connections = ["kitaras-veil", "dahin-planet", "kailaasa"],
            },
            new GalaxySectorDefinition
            {
                Key = "kailaasa",
                Name = "Kailaasa",
                SystemName = "Capella",
                Connections = ["dahin", "yokan", "io"],
            },
            new GalaxySectorDefinition
            {
                Key = "kitaras-veil",
                Name = "Kitara's Veil",
                SystemName = "Capella",
                Connections = ["vishaos-cove", "dahin"],
            },
            new GalaxySectorDefinition
            {
                Key = "dahin-planet",
                Name = "Dahin Mining Interest",
                SystemName = "Capella",
                Aliases =
                [
                    "Dahin Planet",
                    "Planet Dahin",
                ],
                Connections = ["dahin"],
            },
            new GalaxySectorDefinition
            {
                Key = "vishaos-cove",
                Name = "Vishao's Cove",
                SystemName = "Capella",
                Connections = ["antares-frontier", "kitaras-veil"],
            },
            new GalaxySectorDefinition
            {
                Key = "yokan",
                Name = "Yokan",
                SystemName = "Capella",
                Connections = ["kailaasa", "swooping-eagle", "ishuan"],
            },
            new GalaxySectorDefinition
            {
                Key = "ishuan",
                Name = "Ishuan",
                SystemName = "Castor",
                Connections = ["yokan", "menorb", "ganymede"],
            },
            new GalaxySectorDefinition
            {
                Key = "menorb",
                Name = "Menorb",
                SystemName = "Castor",
                Connections = ["ishuan"],
            },
            new GalaxySectorDefinition
            {
                Key = "roc",
                Name = "R4c",
                SystemName = "Deneb",
                Aliases = ["Roc"],
                Connections = ["lagarto"],
            },
            new GalaxySectorDefinition
            {
                Key = "endriago",
                Name = "Endriago",
                SystemName = "Gallina",
                Connections = ["lagarto", "endriago-planet", "primus", "altair-iii"],
            },
            new GalaxySectorDefinition
            {
                Key = "lagarto",
                Name = "Lagarto",
                SystemName = "Gallina",
                Connections = ["roc", "odins-belt", "mars-beta", "lagarto-moon-risco", "endriago"],
            },
            new GalaxySectorDefinition
            {
                Key = "endriago-planet",
                Name = "Porvenir Mons Area",
                SystemName = "Gallina",
                Aliases =
                [
                    "Endriago Planet",
                    "Planet Endriago",
                ],
                Connections = ["endriago", "lagarto-moon-risco"],
            },
            new GalaxySectorDefinition
            {
                Key = "lagarto-moon-risco",
                Name = "Risco Moon",
                SystemName = "Gallina",
                Aliases =
                [
                    "Lagarto Moon Risco",
                    "Risco",
                ],
                Connections = ["lagarto", "endriago-planet"],
            },
            new GalaxySectorDefinition
            {
                Key = "nostrand-vor",
                Name = "Nostrand Vor",
                SystemName = "Altair",
                Aliases = ["Nastrand Vor"],
                Connections = ["altair-iii", "nostrand-vor-planet"],
            },
            new GalaxySectorDefinition
            {
                Key = "nostrand-vor-planet",
                Name = "Nostrand Vor Planet",
                SystemName = "Altair",
                Aliases =
                [
                    "Planet Nostrand Vor",
                    "Nastrand Vor Planet",
                    "Planet Nastrand Vor",
                ],
                Connections = ["nostrand-vor"],
            },
            new GalaxySectorDefinition
            {
                Key = "swooping-eagle-planet",
                Name = "Yasuragi Area",
                SystemName = "Sirius",
                Aliases =
                [
                    "Swooping Eagle Planet",
                    "Planet Swooping Eagle",
                ],
                Connections = ["swooping-eagle"],
            },
            new GalaxySectorDefinition
            {
                Key = "swooping-eagle",
                Name = "Swooping Eagle",
                SystemName = "Sirius",
                Connections = ["swooping-eagle-planet", "xipe-totec", "yokan", "europa", "glenn"],
            },
            new GalaxySectorDefinition
            {
                Key = "xipe-totec",
                Name = "Xipe Totec",
                SystemName = "Sirius",
                Connections = ["swooping-eagle"],
            },
            new GalaxySectorDefinition
            {
                Key = "adriel-prime",
                Name = "Adriel Prime",
                SystemName = "Proxima Centauri",
                Connections = ["margesi", "freya"],
            },
            new GalaxySectorDefinition
            {
                Key = "margesi",
                Name = "Margesi",
                SystemName = "Proxima Centauri",
                Connections = ["equatorial-earth", "adriel-prime"],
            },
            new GalaxySectorDefinition
            {
                Key = "akerons-gate",
                Name = "Akeron's Gate",
                SystemName = "Sol",
                Connections = ["saturn", "freya", "pluto-and-charon"],
            },
            new GalaxySectorDefinition
            {
                Key = "asteroid-belt-alpha",
                Name = "Asteroid Belt Alpha",
                SystemName = "Sol",
                Connections = ["asteroid-belt-beta", "saturn", "earth"],
            },
            new GalaxySectorDefinition
            {
                Key = "asteroid-belt-beta",
                Name = "Asteroid Belt Beta",
                SystemName = "Sol",
                Connections = ["asteroid-belt-alpha", "saturn", "asteroid-belt-gamma", "venus"],
            },
            new GalaxySectorDefinition
            {
                Key = "asteroid-belt-gamma",
                Name = "Asteroid Belt Gamma",
                SystemName = "Sol",
                Connections = ["saturn", "asteroid-belt-beta", "mars"],
            },
            new GalaxySectorDefinition
            {
                Key = "ceres",
                Name = "Ceres",
                SystemName = "Sol",
                RequiredFaction = "Chavez",
                Connections = ["venus"],
            },
            new GalaxySectorDefinition
            {
                Key = "earth",
                Name = "Earth",
                SystemName = "Sol",
                Connections = ["high-earth", "luna", "asteroid-belt-alpha", "equatorial-earth"],
            },
            new GalaxySectorDefinition
            {
                Key = "equatorial-earth",
                Name = "Equatorial Earth",
                SystemName = "Sol",
                RequiredProfession = "Terran Scout",
                Connections = ["earth", "margesi"],
            },
            new GalaxySectorDefinition
            {
                Key = "europa",
                Name = "Europa",
                SystemName = "Sol",
                RequiredProfession = "Jenquai Defender",
                Connections = ["jupiter", "swooping-eagle"],
            },
            new GalaxySectorDefinition
            {
                Key = "ganymede",
                Name = "Ganymede",
                SystemName = "Sol",
                RequiredProfession = "Jenquai Seeker",
                Connections = ["ishuan", "jupiter"],
            },
            new GalaxySectorDefinition
            {
                Key = "high-earth",
                Name = "High Earth",
                SystemName = "Sol",
                RequiredProfession = "Terran Trader",
                Connections = ["earth", "new-edinburgh"],
            },
            new GalaxySectorDefinition
            {
                Key = "io",
                Name = "Io",
                SystemName = "Sol",
                RequiredProfession = "Jenquai Explorer",
                Connections = ["jupiter", "kailaasa"],
            },
            new GalaxySectorDefinition
            {
                Key = "jupiter",
                Name = "Jupiter",
                SystemName = "Sol",
                Connections = ["saturn", "europa", "io", "ganymede"],
            },
            new GalaxySectorDefinition
            {
                Key = "luna",
                Name = "Luna",
                SystemName = "Sol",
                RequiredProfession = "Terran Enforcer",
                Connections = ["earth", "zweihander"],
            },
            new GalaxySectorDefinition
            {
                Key = "mars-alpha",
                Name = "Mars Alpha",
                SystemName = "Sol",
                RequiredProfession = "Progen Warrior",
                Connections = ["mars", "tarsis"],
            },
            new GalaxySectorDefinition
            {
                Key = "mars-beta",
                Name = "Mars Beta",
                SystemName = "Sol",
                RequiredProfession = "Progen Sentinel",
                Connections = ["mars", "lagarto"],
            },
            new GalaxySectorDefinition
            {
                Key = "mars-gamma",
                Name = "Mars Gamma",
                SystemName = "Sol",
                RequiredProfession = "Progen Privateer",
                Connections = ["mars", "altair-iii"],
            },
            new GalaxySectorDefinition
            {
                Key = "mars",
                Name = "Mars",
                SystemName = "Sol",
                Connections = ["asteroid-belt-gamma", "mars-beta", "mars-alpha", "mars-gamma"],
            },
            new GalaxySectorDefinition
            {
                Key = "mercury",
                Name = "Mercury",
                SystemName = "Sol",
                Connections = ["venus", "pluto-and-charon"],
            },
            new GalaxySectorDefinition
            {
                Key = "neptune",
                Name = "Neptune",
                SystemName = "Sol",
                Connections = ["uranus"],
            },
            new GalaxySectorDefinition
            {
                Key = "pluto-and-charon",
                Name = "Pluto and Charon",
                SystemName = "Sol",
                Aliases = ["Pluto"],
                Connections = ["akerons-gate", "uranus", "mercury"],
            },
            new GalaxySectorDefinition
            {
                Key = "saturn",
                Name = "Saturn",
                SystemName = "Sol",
                Connections = ["akerons-gate", "asteroid-belt-alpha", "asteroid-belt-beta", "asteroid-belt-gamma", "jupiter", "glenn", "uranus"],
            },
            new GalaxySectorDefinition
            {
                Key = "uranus",
                Name = "Uranus",
                SystemName = "Sol",
                Connections = ["neptune", "saturn", "pluto-and-charon"],
            },
            new GalaxySectorDefinition
            {
                Key = "venus",
                Name = "Venus",
                SystemName = "Sol",
                Connections = ["asteroid-belt-beta", "ceres", "mercury"],
            },
            new GalaxySectorDefinition
            {
                Key = "arduinne",
                Name = "Arduinne",
                SystemName = "Tau Ceti",
                Connections = ["inverness", "arduinne-gas-cloud"],
            },
            new GalaxySectorDefinition
            {
                Key = "arduinne-gas-cloud",
                Name = "Arduinne Gas Cloud",
                SystemName = "Tau Ceti",
                Aliases = ["Arduinne Planet", "Planet Arduinne"],
                Connections = ["arduinne"],
            },
            new GalaxySectorDefinition
            {
                Key = "inverness",
                Name = "Inverness",
                SystemName = "Tau Ceti",
                Connections = ["inverness-planet", "new-edinburgh", "arduinne", "aganju"],
            },
            new GalaxySectorDefinition
            {
                Key = "new-edinburgh",
                Name = "New Edinburgh",
                SystemName = "Tau Ceti",
                Connections = ["carpenter", "high-earth", "inverness"],
            },
            new GalaxySectorDefinition
            {
                Key = "inverness-planet",
                Name = "Planet Inverness",
                SystemName = "Tau Ceti",
                Aliases = ["Inverness Planet"],
                Connections = ["inverness"],
            },
            new GalaxySectorDefinition
            {
                Key = "primus-planet",
                Name = "Pr12t4r35m M4ns 1r21 (Pl1n2t Pr3m5s)",
                SystemName = "Vega",
                Aliases =
                [
                    "Primus Planet",
                    "Planet Primus",
                    "Praetorium Mons area (Planet Primus)",
                ],
                Connections = ["primus"],
            },
            new GalaxySectorDefinition
            {
                Key = "primus",
                Name = "Primus",
                SystemName = "Vega",
                Connections = ["endriago", "tarsis", "primus-planet"],
            },
            new GalaxySectorDefinition
            {
                Key = "tarsis",
                Name = "Tarsis",
                SystemName = "Vega",
                Connections = ["primus", "mars-alpha"],
            },
        ];
    }
}
