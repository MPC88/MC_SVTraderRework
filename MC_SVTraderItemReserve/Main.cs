using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MC_SVTraderItemReserve
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        // BepInEx
        public const string pluginGuid = "mc.starvalor.traderitemreserve";
        public const string pluginName = "SV Trader Item Reserve";
        public const string pluginVersion = "0.0.1";

        // Mod
        public static ConfigEntry<int> cfgInsufficientStockLimit;
        public static ConfigEntry<float> cfgSellQntTarget;
        public static ConfigEntry<int> cfgRandomWarpTries;
        public static ConfigEntry<int> cfgMaxSuppliersPerSector;
        public static ConfigEntry<float> cfgMinSectorLevelSupplying;
        public static ConfigEntry<float> cfgMinSectorLevelBuying;
        public static ConfigEntry<int> cfgMinSupplyQuantity;
        internal static object listLock = new object();
        internal static List<BuyReservation> buyReservations = new List<BuyReservation>();
        internal static Dictionary<int, TSector> sellTargets = new Dictionary<int, TSector>();

        // Debug
        public static ConfigEntry<bool> cfgDebug;
        public static ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));
            Harmony.CreateAndPatchAll(typeof(AITraderControlPatches));
            Harmony.CreateAndPatchAll(typeof(DynamicCharacterPatches));

            cfgSellQntTarget = Config.Bind<float>(
                "Selling",
                "Sell quantity target %",
                75,
                "The % of trader's current cargo that must be available to sell when looking for a sell location.");

            cfgRandomWarpTries = Config.Bind<int>(
                "Selling",
                "Random warp tries",
                4,
                "When searching for sell location, the number of random warps a trader makes before setting their target sell unit price to 0.");

            cfgMaxSuppliersPerSector = Config.Bind<int>(
                "Selling",
                "Max suppliers per sector",
                10,
                "Maximum number of traders who will supply any station in a given sector.");

            cfgInsufficientStockLimit = Config.Bind<int>(
                "Buying",
                "Insufficient stock limit",
                5,
                "The stock level where a trader will attempt to supply a producer.");

            cfgMinSectorLevelSupplying = Config.Bind<float>(
                "Buying",
                "Minimum supply sector level",
                1,
                "The minimum sector level in which a trader will search for stations in need to supply.  When set to 1, the sector level is 1.  When set to any other value, it is that fraction of the trader's level rounded down e.g. 2 would mean minimum sector level is half the trader's level, so a level 41 trader will sarch in sectors between 20 and 41 or a level 10 trader between 5 and 10.  Higher final minimum sector level (after division) can improve performance.");
            
            cfgMinSectorLevelBuying = Config.Bind<float>(
                "Buying",
                "Minimum buying sector level",
                2,
                "The minimum sector level in which a trader will search for goods to buy.  When set to 1, the sector level is 1.  When set to any other value, it is that fraction of the trader's level rounded down e.g. 2 would mean minimum sector level is half the trader's level, so a level 41 trader will search in sectors between 20 and 41 or a level 10 trader between 5 and 10.  Higher minimum sector level (after division) can improve performance.");

            cfgMinSupplyQuantity = Config.Bind<int>(
                "Buying",
                "Minimum supply quantity to buy",
                50,
                "The minimum quantity that must be available for purchase when looking for a location to buy an item to supply a station.  The default is 10x the threshold for considering a station to be 'out of stock' (5)");

            cfgDebug = Config.Bind<bool>(
                "Debug",
                "Debug",
                false,
                "Log debug messages");
        }

        public void Start()
        {
            if (cfgMinSectorLevelBuying.Value < 1) cfgMinSectorLevelBuying.Value = 1f;
            if (cfgMinSectorLevelSupplying.Value < 1) cfgMinSectorLevelSupplying.Value = 1f;
        }

        public void Update()
        {
            if (!cfgDebug.Value)
                return;

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                lock (Main.listLock)
                {
                    log.LogWarning("-------------------------------------Sell Targets-------------------------------------");
                    foreach (KeyValuePair<int, TSector> kvp in sellTargets)
                    {
                        DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == kvp.Key);
                        log.LogWarning("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " target sector: " + kvp.Value.coords);
                    }
                    log.LogWarning("--------------------------------------------------------------------------------------");
                }
            }

            if (Input.GetKeyDown(KeyCode.PageDown))
            {
                lock (Main.listLock)
                {
                    log.LogWarning("-------------------------------------Buy Reservations-------------------------------------");
                    foreach (BuyReservation re in buyReservations)
                    {
                        DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == re.traderID);
                        TSector sector = GameData.data.sectors.Find(s => s.Index == re.sectorIndex);
                        Item item = ItemDB.GetItem(re.itemID);
                        log.LogWarning("Trader: " + dynChar.name + " (" + dynChar.id + ") Target sector:" + sector.coords + " Item: " + item.itemName + " (" + item.id + ") Qnt: " + re.qnt);
                    }
                    log.LogWarning("------------------------------------------------------------------------------------------");
                }
            }
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.QuitToMainMenu))]
        [HarmonyPostfix]
        private static void MenuControlLoadOrQuit_Post()
        {
            lock(Main.listLock)
            {
                buyReservations.Clear();
                sellTargets.Clear();
            }
        }

        internal static Item GetRandomItemToBuy(DynamicCharacter dynChar, MarketPriceControl __instance, int commerceLevel, int randomness)
        {
            List<ItemMarketPrice> newList = UtilityMethods.FilterBuyingMarketItemList(
                (List<ItemMarketPrice>)AccessTools.Method(typeof(MarketPriceControl), "GetSellingPriceList").Invoke(__instance, new object[] { commerceLevel, true, 2, true }), 
                __instance.sector, 
                dynChar);

            if (newList.Count == 0)
                return null;

            if (randomness > newList.Count)
                randomness = newList.Count;

            Tuple<ItemMarketPrice, int> finalResult = UtilityMethods.GetFinalBuyingItem(__instance, newList, dynChar);
            if (finalResult == null)
                return null;

            lock (Main.listLock)
            {
                buyReservations.Add(new BuyReservation(dynChar.id, __instance.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
                if (cfgDebug.Value) log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + __instance.sector.coords + ".  Reserved list count: " + buyReservations.Count);
            }
            return finalResult.Item1.AsItem;
        }

        [HarmonyPatch(typeof(Station), nameof(Station.SellItemToNPC))]
        [HarmonyPostfix]
        private static void StationSellToNPC_Post(DynamicCharacter NPCChar)
        {
            ClearBuyReservations(NPCChar, "Bought Items");
        }

        [HarmonyPatch(typeof(Station), nameof(Station.BuyItemFromNPC))]
        [HarmonyPostfix]
        private static void StationBuyItemFromNPC_Post(DynamicCharacter NPCChar)
        {
            ClearSellTargets(NPCChar, "Sold Items");
        }

        internal static void ClearReservations(DynamicCharacter dynChar, string cause)
        {
            ClearBuyReservations(dynChar, cause);
            ClearSellTargets(dynChar, cause);
        }

        internal static void ClearBuyReservations(DynamicCharacter dynChar, string cause)
        {
            if (Main.cfgDebug.Value)
            {
                List<BuyReservation> remove = new List<BuyReservation>();
                lock (Main.listLock)
                {
                    foreach (BuyReservation re in Main.buyReservations)
                    {
                        if (re.traderID == dynChar.id)
                        {
                            Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + ", Item: " + ItemDB.GetItem(re.itemID).itemName + " (" + re.itemID + ")" + ", qnt: " + re.qnt + ", sector: " + GameData.data.sectors[re.sectorIndex].coords + " - Removing Reservation - " + cause + " - Reserved list count: " + buyReservations.Count);
                            remove.Add(re);
                        }
                    }
                    Main.buyReservations = Main.buyReservations.Except(remove).ToList();
                }
            }
            else
            {
                lock (Main.listLock)
                {
                    Main.buyReservations.RemoveAll(re => re.traderID == dynChar.id);
                }
            }
        }

        internal static void ClearSellTargets(DynamicCharacter dynChar, string cause)
        {
            lock (Main.listLock)
            {
                if (sellTargets.TryGetValue(dynChar.id, out TSector targetSector))
                {
                    if (cfgDebug.Value) log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + "), target sector: " + targetSector.coords + " - Removing Sell Target - " + cause + " - Selling list count: " + sellTargets.Count);
                    sellTargets.Remove(dynChar.id);
                }
            }
        }
    }
}
