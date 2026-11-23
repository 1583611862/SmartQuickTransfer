using System;
using System.Collections.Generic;
using SmartQuickTransfer.Transfer;

namespace SmartQuickTransfer.Core
{
    /// <summary>
    /// Game-mechanic knowledge: how well an item fits a destination machine, scored 0-100.
    ///
    /// Every rule here is grounded in the actual Assembly-CSharp.dll of the installed game
    /// (verified by decompilation) and cross-checked against the Probably Stolen wiki (game data
    /// v1.0.0.12). Native identifiers and tags are used exclusively - never display names.
    /// A relationship that cannot be confirmed stays Neutral (50). Native slot compatibility is
    /// NOT decided here: incompatible items are already filtered out by MachineSlotResolver using
    /// the game's own mayInventoryAddItemFunc, so this class only ranks compatible candidates.
    ///
    /// Score bands: Strong=100 (dedicated function), Related=75 (related but not primary),
    /// Neutral=50 (game allows it, no known functional relationship).
    /// </summary>
    internal static class ItemDestinationAffinityRules
    {
        internal const float Strong = 100f;
        internal const float Related = 75f;
        internal const float Neutral = 50f;

        // Verified machine identifiers (FurnitureItemDirectory.cs / HusbandryDirectory.cs).
        private const string MRecharger = "recharger";
        private const string MFurnace = "furnace";
        private const string MFridge = "fridge";
        private const string MHydroponic = "hydroponic";
        private const string MDnaManipulator = "plant_dna_manipulator";
        private const string MPrinter = "printer";
        private const string MBlender = "blender";
        private const string MHeatingPlate = "heating_plate";
        private const string MEvaporator = "evaporator";
        private const string MChemFinisher = "chem_finisher";
        private const string MRas = "ras";
        private const string MWaterPurifier = "water_purifier";

        /// <summary>
        /// Returns the affinity of an item for a candidate destination. The destination machine id
        /// is taken from the candidate's TargetKey ("machine:&lt;id&gt;"); storage/container targets
        /// have no dedicated functional relationship and score Neutral unless a learned preference
        /// later overrides them.
        /// </summary>
        internal static float Score(GameItem item, string targetKey)
        {
            if (item == null || string.IsNullOrEmpty(targetKey))
            {
                return Neutral;
            }
            if (!targetKey.StartsWith(StableIdentityResolver.PrefixMachine, StringComparison.Ordinal))
            {
                return Neutral;
            }

            string machineId = targetKey.Substring(StableIdentityResolver.PrefixMachine.Length);
            switch (machineId)
            {
                case MRecharger:
                    return ScoreRecharger(item);
                case MFurnace:
                    return ScoreFurnace(item);
                case MFridge:
                    return ScoreFridge(item);
                case MHydroponic:
                    return ScoreHydroponic(item);
                case MDnaManipulator:
                    return ScoreDnaManipulator(item);
                case MPrinter:
                    return ScorePrinter(item);
                case MBlender:
                case MHeatingPlate:
                case MEvaporator:
                case MChemFinisher:
                    return ScoreChemistry(item, machineId);
                case MRas:
                    return ScoreRas(item);
                case MWaterPurifier:
                    return ScoreWaterPurifier(item);
                default:
                    return Neutral;
            }
        }

        // MachineRecharger.cs:18 -> !disposable && IsTag("power_source_item").
        private static float ScoreRecharger(GameItem item)
        {
            if (IsPowerSource(item) && CanRecharge(item))
            {
                return Strong;
            }
            if (IsPowerSource(item))
            {
                return Related;
            }
            return Neutral;
        }

        // MachineFurnace.cs:28 -> scrap_metal | common_ore | flux_agent | advanced_flux_agent.
        private static float ScoreFurnace(GameItem item)
        {
            string id = item.identifier;
            if (id == "scrap_metal" || id == "common_ore")
            {
                return Strong;
            }
            if (id == "flux_agent" || id == "advanced_flux_agent")
            {
                return Related; // flux influences purity but is not itself smelted into an ingot
            }
            if (id == "junk")
            {
                return Related; // only with the junk-processing module (wiki); native slot allows it
            }
            return Neutral;
        }

        // MachineFridge.cs + wiki: specific drinks get chilled/frozen; containers only cool chemistry.
        private static float ScoreFridge(GameItem item)
        {
            string id = item.identifier;
            // Chilled/frozen drink set verified in wiki and present in the assembly.
            if (id == "red_beer" || id == "processed_milk" || id == "processed_juice"
                || id == "energy_drink" || id == "soda_red" || id == "water_ration"
                || id == "water_ration_small" || id == "liquid_luck"
                || id == "bottled_water_premium" || id == "nudka" || id == "beis_icecream"
                || IsBeerCase(id))
            {
                return Strong;
            }
            // A liquid container can go in the fridge to cool chemistry, but gains no chilled tag.
            if (IsTag(item, "LIQUID_CONTAINER_TAG"))
            {
                return Related;
            }
            return Neutral;
        }

        // MachineHydroponic.cs:205/216 -> seed tag (not container) / liquid container / battery.
        private static float ScoreHydroponic(GameItem item)
        {
            if (IsTag(item, "SEED") && !IsTag(item, "CONTAINER_TAG"))
            {
                return Strong;
            }
            if (IsTag(item, "LIQUID_CONTAINER_TAG"))
            {
                return Related; // water source
            }
            if (IsPowerSource(item))
            {
                return Related; // powers the machine
            }
            if (item.identifier == "nutrient_tablet")
            {
                return Related; // wiki: hydroponic nutrient tablet
            }
            return Neutral;
        }

        // DNA editor: data disks and seeds. slot used is not distinguishable via our available API,
        // so we rank by item kind only and keep unknown disks neutral.
        private static float ScoreDnaManipulator(GameItem item)
        {
            string id = item.identifier;
            if (id == "dna_disk_black_empty" || id == "plant_dna_disk"
                || id.StartsWith("dna_disk_", StringComparison.Ordinal))
            {
                return Strong;
            }
            if (IsTag(item, "SEED") && !IsTag(item, "CONTAINER_TAG"))
            {
                return Related;
            }
            return Neutral;
        }

        // 3D printer: printer chip (recipe) and printer plastic (filament), both native identifiers.
        private static float ScorePrinter(GameItem item)
        {
            string id = item.identifier;
            if (id != null && id.StartsWith("printer_chip_", StringComparison.Ordinal))
            {
                return Strong;
            }
            if (id == "printer_plastic" || id == "metal_ingot")
            {
                return Related; // filament / refined ingot influences print quality (wiki)
            }
            return Neutral;
        }

        // Chemistry devices: containers and liquid/chemical inputs are related; exact recipes are
        // deliberately not hardcoded. Unconfirmed relationships stay neutral.
        private static float ScoreChemistry(GameItem item, string machineId)
        {
            string id = item.identifier;
            if (IsTag(item, "LIQUID_CONTAINER_TAG"))
            {
                return Related;
            }
            if (IsTag(item, "LIQUID_TAG") || IsTag(item, "CHEMICAL"))
            {
                return Related;
            }
            if (machineId == MChemFinisher && (id == "filter" || id == "filters"))
            {
                return Related;
            }
            return Neutral;
        }

        // RAS (recirculating aquaculture): fish go in the tank; feed is a related consumable.
        // industrial_fish_feed / gutterflow_pellet keep their vanilla double-click split behaviour
        // (handled in ItemDoubleClickPatch) - that is unrelated to affinity scoring here.
        private static float ScoreRas(GameItem item)
        {
            if (IsTag(item, "FISH_ITEM_TAG"))
            {
                return Strong;
            }
            if (item.identifier == "industrial_fish_feed" || item.identifier == "gutterflow_pellet"
                || IsTag(item, "AQUACULTURE"))
            {
                return Related;
            }
            return Neutral;
        }

        private static float ScoreWaterPurifier(GameItem item)
        {
            if (IsTag(item, "LIQUID_CONTAINER_TAG"))
            {
                return Related;
            }
            return Neutral;
        }

        private static bool IsPowerSource(GameItem item)
        {
            return IsTag(item, "power_source_item");
        }

        private static bool CanRecharge(GameItem item)
        {
            // PowerHelper marks disposable sources as non-rechargeable.
            return IsTag(item, "power_source_item_recharge");
        }

        private static bool IsBeerCase(string id)
        {
            return id != null && id.StartsWith("beer_case", StringComparison.Ordinal);
        }

        private static bool IsTag(GameItem item, string tag)
        {
            if (item == null || string.IsNullOrEmpty(tag))
            {
                return false;
            }
            try
            {
                return item.IsTag(tag);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
