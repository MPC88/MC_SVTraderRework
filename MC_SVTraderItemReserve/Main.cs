using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Purchasing;
using static System.Collections.Specialized.BitVector32;

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
        internal static int curTraderID = -1;
        internal static List<BuyReservation> buyReservations = new List<BuyReservation>();
        internal static Dictionary<int, TSector> sellTargets = new Dictionary<int, TSector>();

        // Debug
        public static ConfigEntry<bool> cfgDebug;
        internal static BepInEx.Logging.ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            Main.cfgDebug = Config.Bind<bool>(
                "Debug",
                "Debug",
                false,
                "Log debug messages");
        }
                
        [HarmonyPatch(typeof(GameDataInfo), nameof(GameDataInfo.GetRandomItemOfferOnNearbySectors))]
        [HarmonyPrefix]
        private static bool GetBestItemOfferNearby(GameDataInfo __instance, ref Item __result, int commerceLevel, float maxRange, TSector fromSector, int maxSectorLevel, System.Random rand)
        {
            if (rand == null)
                rand = new System.Random();

            Tuple<ItemMarketPrice, int> finalResult = null;
            int maxLvl = commerceLevel + 2;
            int minLvl = Mathf.RoundToInt(maxLvl / 2);
            while (finalResult == null && maxLvl <= maxSectorLevel)
            {
                List<TSector> list = __instance.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(fromSector.posV2) <= maxRange && s.MarketPriceControl().HasAnyItemOffer(minLvl - 2, maxLvl + 2));
                List<Tuple<ItemMarketPrice, int>> bestItemOffers = new List<Tuple<ItemMarketPrice, int>>();

                if (list.Count > 0)
                {
                    foreach(TSector s in list)
                    {
                        Tuple<ItemMarketPrice, int> bestSectorItem = GetBestItemOfferInSector(s.MarketPriceControl(), minLvl - 2, maxLvl + 2);
                        if (bestSectorItem != null)
                            bestItemOffers.Add(bestSectorItem);
                    }

                    if (bestItemOffers.Count > 0)
                    {
                        float finalUnitPrice = 0;
                        foreach (Tuple<ItemMarketPrice, int> itemOffer in bestItemOffers)
                        {
                            if (finalResult == null)
                            {
                                finalResult = itemOffer;
                                finalUnitPrice = UtilityMethods.GetSellingPrice(finalResult.Item1, finalResult.Item1.mpc.sector);
                            }
                            else
                            {
                                float iUnitPrice = UtilityMethods.GetSellingPrice(itemOffer.Item1, itemOffer.Item1.mpc.sector);
                                if ((iUnitPrice / itemOffer.Item1.AsItem.basePrice) < (finalUnitPrice / finalResult.Item1.AsItem.basePrice))
                                {
                                    finalResult = itemOffer;
                                    finalUnitPrice = iUnitPrice;
                                }
                            }
                        }                        
                    }
                }

                if (finalResult == null)
                {
                    minLvl = minLvl - 2 > 0 ? minLvl - 2 : 1;
                    maxLvl += 2;
                }
            }

            if (finalResult == null)
            {
                __result = null;
                return false;
            }

            buyReservations.Add(new BuyReservation(Main.curTraderID, finalResult.Item1.mpc.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
            if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + GameData.data.characterSystem.dynChars.Find(dc => dc.id == Main.curTraderID).name + " (" + Main.curTraderID + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + finalResult.Item1.mpc.sector.coords + ".  Reserved list count: " + buyReservations.Count);
            __result = finalResult.Item1.AsItem;
            return false;
        }

        private static Tuple<ItemMarketPrice, int> GetBestItemOfferInSector(MarketPriceControl __instance, int minItemLvl, int maxItemLvl)
        {
            DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == Main.curTraderID);

            List<ItemMarketPrice> newList = UtilityMethods.FilterBuyingMarketItemList(
                __instance.prices.FindAll((ItemMarketPrice p) => p.IsSelling && p.AsItem.itemLevel >= minItemLvl && p.AsItem.itemLevel <= maxItemLvl),
                __instance.sector,
                dynChar);

            if (newList.Count <= 0)
                return null;

            return UtilityMethods.GetFinalBuyingItem(__instance, newList, dynChar);
        }

        [HarmonyPatch(typeof(MarketPriceControl), nameof(MarketPriceControl.GetRandomItemToBuy))]
        [HarmonyPrefix]
        private static bool MPCGetRandomItemToBuy_Pre(MarketPriceControl __instance, int commerceLevel, int randomness, ref Item __result)
        {
            DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == Main.curTraderID);

            List<ItemMarketPrice> newList = UtilityMethods.FilterBuyingMarketItemList(
                (List<ItemMarketPrice>)AccessTools.Method(typeof(MarketPriceControl), "GetSellingPriceList").Invoke(__instance, new object[] { commerceLevel, true, 2, true }), 
                __instance.sector, 
                dynChar);

            if (newList.Count == 0)
            {
                __result = null;
                return false;
            }

            if (randomness > newList.Count)
                randomness = newList.Count;

            Tuple<ItemMarketPrice, int> finalResult = UtilityMethods.GetFinalBuyingItem(__instance, newList, dynChar);
            if (finalResult == null)
            {
                __result = null;
                return false;
            }

            buyReservations.Add(new BuyReservation(Main.curTraderID, __instance.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
            if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + GameData.data.characterSystem.dynChars.Find(dc => dc.id == Main.curTraderID).name + " (" + Main.curTraderID + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + __instance.sector.coords + ".  Reserved list count: " + buyReservations.Count);
            __result = finalResult.Item1.AsItem;
            return false;
        }

        [HarmonyPatch(typeof(Station), nameof(Station.SellItemToNPC))]
        [HarmonyPostfix]
        private static void StationSellToNPC_Post(DynamicCharacter NPCChar)
        {
            lock (GameData.threadSaveLock)
            {
                DynamicCharacterPatches.ClearReservations(NPCChar, "Made Purchase");
            }
        }

        [HarmonyPatch(typeof(Station), nameof(Station.BuyItemFromNPC))]
        [HarmonyPostfix]
        private static void StationBuyItemFromNPC_Post(DynamicCharacter NPCChar)
        {
            lock (GameData.threadSaveLock)
            {
                if (Main.sellTargets.TryGetValue(NPCChar.id, out TSector targetSector))
                {
                    if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + NPCChar.name + " (" + NPCChar.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - Made sale.");
                    Main.sellTargets.Remove(NPCChar.id);
                }
            }
        }
    }
}
