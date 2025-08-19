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
        internal const int randomWarpsBeforeSellingAtZero = 4;
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

            cfgDebug = Config.Bind<bool>(
                "Debug",
                "Debug",
                false,
                "Log debug messages");
        }

        public void Update()
        {
            if (!cfgDebug.Value)
                return;

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                lock(GameData.threadSaveLock)
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
                lock(GameData.threadSaveLock)
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

            buyReservations.Add(new BuyReservation(dynChar.id, __instance.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
            if (cfgDebug.Value) log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + __instance.sector.coords + ".  Reserved list count: " + buyReservations.Count);
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
            lock (GameData.threadSaveLock)
            {
                if (Main.cfgDebug.Value)
                {
                    List<BuyReservation> remove = new List<BuyReservation>();
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
                else
                {
                    Main.buyReservations.RemoveAll(re => re.traderID == dynChar.id);
                }
            }
        }

        internal static void ClearSellTargets(DynamicCharacter dynChar, string cause)
        {
            lock (GameData.threadSaveLock)
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
