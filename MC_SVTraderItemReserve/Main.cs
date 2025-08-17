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
        private static int curTraderID = -1;
        private static List<ReserveEntry> reservedList = new List<ReserveEntry>();

        // Debug
        public static ConfigEntry<bool> cfgDebug;
        public static ConfigEntry<bool> cfgDebugLockState;
        internal static BepInEx.Logging.ManualLogSource log = BepInEx.Logging.Logger.CreateLogSource(pluginName);

        public void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            cfgDebug = Config.Bind<bool>(
                "Debug",
                "Debug",
                false,
                "Log debug messages");

            cfgDebugLockState = Config.Bind<bool>(
                "Debug",
                "Debug lock state",
                false,
                "Log lock state messages.");
        }

        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.DetermineItemToBuy))]
        [HarmonyPrefix]
        private static bool DynCharDetermineItemToBuy_Pre(DynamicCharacter __instance)
        {
            curTraderID = __instance.id;
            if (cfgDebugLockState.Value) log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ")" + " locking searches.");

            ClearReservations(__instance, "LookingForNewItem");

            return true;
        }

        [HarmonyPatch(typeof(AITraderControl), "SetNewDestination")]
        [HarmonyPrefix]
        private static bool AITCSetNewDest_Pre(AITraderControl __instance, int ___stayAtDestinationTurns)
        {
            // Don't run if not just arrived.
            if (__instance.currStatus != 0)
                return true;

            // Modified original method
            if (___stayAtDestinationTurns > 0)
            {
                ___stayAtDestinationTurns--;
                return false;
            }
            TSector currentSector = GameData.data.GetCurrentSector();
            if (__instance.currStatus == 0)
            {
                __instance.goingToStation = null;
                Station station = null;

                if (__instance.dynChar.HasItemInCargo)
                {
                    ItemStockData stockDataByIndex = __instance.dynChar.GetItemStock.GetStockDataByIndex(0);
                    station = currentSector.GetStationBuyingItem(stockDataByIndex.AsItem, -1, out var _);
                }
                else
                {
                    if (__instance.dynChar.wantsToBuyItem == null)
                    {
                        lock (GameData.threadSaveLock)
                        {
                            curTraderID = __instance.dynChar.id;
                            ClearReservations(__instance.dynChar, "LookingForNewItem");

                            __instance.dynChar.wantsToBuyItem = currentSector.MarketPriceControl().GetRandomItemToBuy(__instance.dynChar.CommerceLevel, 5);
                        }
                    }
                    if (__instance.dynChar.wantsToBuyItem != null)
                    {
                        lock (GameData.threadSaveLock)
                        { 
                            ReserveEntry reserveEntry = reservedList.Find(re => re.traderID == __instance.dynChar.id);

                            if (reserveEntry != null)
                            {
                                if (currentSector.Index != reserveEntry.sectorIndex)
                                {
                                    TSector targetSector = GameData.data.sectors[reserveEntry.sectorIndex];
                                    if (targetSector.IsBeingAttacked)
                                    {
                                        __instance.dynChar.ClearWishlist();
                                        return false;
                                    }

                                    TSector nextSector = null;
                                    if (targetSector.DistanceToPositionInGalaxy(currentSector.posV2) <= __instance.dynChar.MaxWarpDistance)
                                        nextSector = targetSector;
                                    else
                                    {
                                        float warpAdjust = 0.05f;
                                        do
                                        {
                                            nextSector = GetClosestSectorToTargetInWarpRange(currentSector, targetSector, Mathf.RoundToInt(__instance.dynChar.MaxWarpDistance * (1 + warpAdjust)));
                                            warpAdjust += 0.05f;
                                        }
                                        while (nextSector == null);

                                        if (cfgDebug.Value) log.LogInfo("Trader: " + __instance.dynChar.name + " (" + __instance.dynChar.id + ")" + " jumping to: " + nextSector.coords + " warp cheat: +" + warpAdjust + "%");
                                    }

                                    if (nextSector != null)
                                    {
                                        __instance.WarpDisappear(true);
                                        __instance.dynChar.GoToSector(nextSector);
                                        return false;
                                    }
                                }
                            }
                        }

                        station = currentSector.GetStationSellingItem(__instance.dynChar.wantsToBuyItem, -1, 10, out var _);
                        if(station == null)
                        {
                            __instance.WarpDisappear(true);
                            __instance.dynChar.ClearWishlist();
                            return false;
                        }
                    }
                }
                if (station != null)
                {
                    __instance.destination = station.posV3;
                    __instance.goingToStation = station;
                    __instance.currStatus = 1;
                    return false;
                }
                AIStationControl randomJumpgateStation = GameManager.instance.GetRandomJumpgateStation(allowLocalJump: false);
                if (randomJumpgateStation != null)
                {
                    __instance.destination = randomJumpgateStation.station.posV3;
                    __instance.goingToStation = randomJumpgateStation.station;
                    __instance.currStatus = 2;
                }
                else
                {
                    __instance.currStatus = 3;
                    __instance.goingToStation = null;
                    __instance.destination = Vector3.zero;
                }
                return false;
            }
            if (__instance.currStatus == 1 && __instance.goingToStation == null)
            {
                __instance.WarpDisappear(nearPlayer: true);
            }
            if (__instance.currStatus == 2)
            {
                if (UnityEngine.Random.Range(1, 11) < 6)
                {
                    AIStationControl randomJumpgateStation2 = GameManager.instance.GetRandomJumpgateStation(allowLocalJump: false);
                    if (randomJumpgateStation2 != null)
                    {
                        __instance.destination = randomJumpgateStation2.station.posV3;
                        __instance.goingToStation = randomJumpgateStation2.station;
                        return false;
                    }
                }
                __instance.currStatus = 3;
                __instance.goingToStation = null;
                __instance.WarpDisappear(nearPlayer: true);
            }
            else if (__instance.currStatus == 3)
            {
                __instance.WarpDisappear(nearPlayer: true);
            }

            // Block original
            return false;
        }

        private static void ClearReservations(DynamicCharacter dynChar, string cause)
        {
            if (cfgDebug.Value)
            {
                List<ReserveEntry> remove = new List<ReserveEntry>();
                foreach (ReserveEntry re in reservedList)
                {
                    if (re.traderID == dynChar.id)
                    {
                        log.LogInfo("Removing reservation - Trader: " + dynChar.name + " (" + dynChar.id + ")" + ", Item: " + ItemDB.GetItem(re.itemID).itemName + " (" + re.itemID + ")" + ", qnt: " + re.qnt + ", sector: " + GameData.data.sectors[re.sectorIndex].coords + " Cause: " + cause);
                        remove.Add(re);
                    }
                }
                reservedList = reservedList.Except(remove).ToList();                
            }
            else
            {
                reservedList.RemoveAll(re => re.traderID == dynChar.id);
            }
        }

        [HarmonyPatch(typeof(GameDataInfo), nameof(GameDataInfo.GetRandomItemOfferOnNearbySectors))]
        [HarmonyPrefix]
        private static bool GDIGetRandomItemOffer_Pre(GameDataInfo __instance, ref Item __result, int commerceLevel, float maxRange, TSector fromSector, int maxSectorLevel, System.Random rand)
        {
            if (rand == null)
                rand = new System.Random();

            Tuple<ItemMarketPrice, int> finalResult = null;
            int maxLvl = commerceLevel + 2;
            int minLvl = Mathf.RoundToInt(maxLvl / 2);
            while (finalResult == null && maxLvl <= maxSectorLevel)
            {
                List<TSector> list = __instance.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(fromSector.posV2) <= maxRange && s.MarketPriceControl().HasAnyItemOffer(minLvl - 2, maxLvl + 2));
                List<Tuple<ItemMarketPrice, int>> bestItems = new List<Tuple<ItemMarketPrice, int>>();

                if (list.Count > 0)
                {
                    foreach(TSector s in list)
                    {
                        Tuple<ItemMarketPrice, int> bestSectorItem = GetBestItemOffer(s.MarketPriceControl(), minLvl - 2, maxLvl + 2);
                        if (bestSectorItem != null)
                            bestItems.Add(bestSectorItem);
                    }

                    if (bestItems.Count > 0)
                    {
                        float finalUnitPrice = 0;
                        foreach (Tuple<ItemMarketPrice, int> i in bestItems)
                        {
                            if (finalResult == null)
                            {
                                finalResult = i;
                                finalUnitPrice = GetPrice(finalResult.Item1, finalResult.Item1.mpc.sector);
                            }
                            else
                            {
                                float iUnitPrice = GetPrice(i.Item1, i.Item1.mpc.sector);
                                if ((iUnitPrice / i.Item1.AsItem.basePrice) < (finalUnitPrice / finalResult.Item1.AsItem.basePrice))
                                {
                                    finalResult = i;
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

            reservedList.Add(new ReserveEntry(curTraderID, finalResult.Item1.mpc.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
            if (cfgDebug.Value) log.LogInfo("Trader: " + GameData.data.characterSystem.dynChars.Find(dc => dc.id == curTraderID).name + " (" + curTraderID + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + finalResult.Item1.mpc.sector.coords + ".  Reserved list count: " + reservedList.Count);
            __result = finalResult.Item1.AsItem;
            return false;
        }

        private static Tuple<ItemMarketPrice, int> GetBestItemOffer(MarketPriceControl __instance, int minItemLvl, int maxItemLvl)
        {
            DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == curTraderID);

            List<ItemMarketPrice> newList = FilterMarketItemList(
                __instance.prices.FindAll((ItemMarketPrice p) => p.IsSelling && p.AsItem.itemLevel >= minItemLvl && p.AsItem.itemLevel <= maxItemLvl),
                __instance.sector,
                dynChar);

            if (newList.Count <= 0)
                return null;

            return GetFinalItem(__instance, newList, dynChar);
        }

        [HarmonyPatch(typeof(MarketPriceControl), nameof(MarketPriceControl.GetRandomItemToBuy))]
        [HarmonyPostfix]
        private static void MPCGetRandomItemToBuy_Post(MarketPriceControl __instance, int commerceLevel, int randomness, ref Item __result)
        {
            if (__result == null)
                return;

            DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == curTraderID);

            List<ItemMarketPrice> newList = FilterMarketItemList(
                (List<ItemMarketPrice>)AccessTools.Method(typeof(MarketPriceControl), "GetSellingPriceList").Invoke(__instance, new object[] { commerceLevel, true, 2, true }), 
                __instance.sector, 
                dynChar);

            if (newList.Count == 0)
            {
                __result = null;
                return;
            }

            if (randomness > newList.Count)
                randomness = newList.Count;

            Tuple<ItemMarketPrice, int> finalResult = GetFinalItem(__instance, newList, dynChar);
            reservedList.Add(new ReserveEntry(curTraderID, __instance.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
            if (cfgDebug.Value) log.LogInfo("Trader: " + GameData.data.characterSystem.dynChars.Find(dc => dc.id == curTraderID).name + " (" + curTraderID + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + __instance.sector.coords + ".  Reserved list count: " + reservedList.Count);
            __result = finalResult.Item1.AsItem;
        }

        private static List<ItemMarketPrice> FilterMarketItemList(List<ItemMarketPrice> list, TSector sector, DynamicCharacter dynChar)
        {
            List<ItemMarketPrice> newList = new List<ItemMarketPrice>();
            foreach (ItemMarketPrice imp in list)
            {
                ReserveEntry re = ReserveEntry.EntryExists(reservedList, sector.Index, imp.itemID);
                if (re == null)
                {
                    Station station = sector.GetStationSellingItem(imp.AsItem, -1, 10, out float _);
                    if(station != null)
                        newList.Add(imp);
                }
                else
                {
                    Station station = sector.GetStationSellingItem(imp.AsItem, -1, 10, out float unitPrice);
                    if (station != null)
                    {
                        SM_Market market = station.MarketModule;
                        if (market != null)
                        {
                            int availableQnt = market.GetMarketItem(3, imp.itemID, imp.AsItem.rarity, null).Stock - ReserveEntry.GetTotalReservedQuantity(reservedList, imp.itemID);

                            unitPrice = GetPrice(imp, sector);
                            int desiredQnt = Mathf.Clamp((int)(dynChar.credits / unitPrice), 0, (int)(dynChar.CargoSpace / imp.AsItem.weight));

                            if (desiredQnt <= availableQnt)
                                newList.Add(imp);
                            else if (cfgDebug.Value)
                                log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to insufficent stock: " + availableQnt + " (desired: " + desiredQnt + ")");
                        }
                        else if (cfgDebug.Value)
                            log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to NULL MARKET MODULE");
                    }
                    else if (cfgDebug.Value)
                        log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to NULL STATION");
                }
            }

            return newList;
        }

        private static Tuple<ItemMarketPrice, int> GetFinalItem(MarketPriceControl mpc, List<ItemMarketPrice> list, DynamicCharacter dynChar)
        {
            ItemMarketPrice finalIMP = list[mpc.Rand.Next(0, list.Count)];
            float finalUnitPrice = GetPrice(finalIMP, mpc.sector);

            foreach (ItemMarketPrice item in list)
            {
                float itemUnitPrice = GetPrice(item, mpc.sector);
                if ((itemUnitPrice / item.AsItem.basePrice) < (finalUnitPrice / finalIMP.AsItem.basePrice))
                {
                    finalIMP = item;
                    finalUnitPrice = itemUnitPrice;
                }
            }

            int maxQnt = Mathf.Clamp((int)(dynChar.credits / finalUnitPrice), 0, (int)(dynChar.CargoSpace / finalIMP.AsItem.weight));

            return new Tuple<ItemMarketPrice, int>(finalIMP, maxQnt);
        }

        private static float GetPrice(ItemMarketPrice item, TSector sector)
        {
            return item.sellingPrice == -1 ? (item.tradePrice == -1 ? GameData.data.GalacticMarket().GetItemPriceOnSector(item.AsItem.id, sector) : item.tradePrice) : item.sellingPrice;
        }

        [HarmonyPatch(typeof(Station), nameof(Station.SellItemToNPC))]
        [HarmonyPostfix]
        private static void StationSellToNPC_Post(DynamicCharacter NPCChar)
        {
            lock (GameData.threadSaveLock)
            {
                ClearReservations(NPCChar, "BoughtItems");
            }
        }

        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.ClearWishlist))]
        [HarmonyPostfix]
        private static void DynamicCharacterClearWishlist_Post(DynamicCharacter __instance)
        {
            lock (GameData.threadSaveLock)
            {
                ClearReservations(__instance, "WishlistClear");
            }
        }

        [HarmonyPatch(typeof(CharacterSystem), nameof(CharacterSystem.UpdateCharacters))]
        [HarmonyPrefix]
        private static void CharacterSystemUpdate_Pre(CharacterSystem __instance, bool forced)
        {
            if (forced || GameData.timePlayed - __instance.timeCountOnLastUpdate >= 30f)
                if(cfgDebug.Value) log.LogInfo("=============== UPDATING TRADERS ===============");
        }

        private static TSector GetClosestSectorToTargetInWarpRange(TSector curSector, TSector targetSector, int maxRange)
        {
            List<TSector> sectors = GameData.data.sectors.FindAll(s => !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(curSector.posV2) <= maxRange);

            TSector nextSector = null;

            foreach (TSector sector in sectors)
                if (nextSector == null)
                    nextSector = sector;
                else if (sector.DistanceToPositionInGalaxy(targetSector.posV2) < nextSector.DistanceToPositionInGalaxy(targetSector.posV2))
                    nextSector = sector;

            return nextSector;
        }

        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.UpdateCharacter))]
        [HarmonyPrefix]
        private static bool DynamicCharacterUpdate_Pre(DynamicCharacter __instance)
        {
            if (__instance.Sector.IsBeingAttacked)
            {
                __instance.GoToRandomSector(__instance.level + 5, 0);
                return false;
            }
            if (__instance.Sector.IsCurrentSector)
            {
                if (!GameManager.instance.CurrentSectorHasAnyStationWithMarket())
                {
                    __instance.GoToRandomSector(__instance.level + 5, 0);
                }
                return false;
            }
            System.Random rand = CharacterSystem.Rand;
            if (__instance.HasItemInCargo)
            {
                ItemStockData stockDataByIndex = __instance.GetItemStock.GetStockDataByIndex(0);
                float higherThanValue = ((__instance.failedAttempts < 3) ? __instance.lastUnitPricePaid : 0f);
                TSector nearbySectorWithHighestBuyingPrice = GameData.data.GetNearbySectorWithHighestBuyingPrice(stockDataByIndex.AsItem, __instance.level, __instance.MaxWarpDistance, __instance.Sector, __instance.level + 5, higherThanValue);
                if (nearbySectorWithHighestBuyingPrice == null)
                {
                    __instance.failedAttempts++;
                    __instance.GoToRandomSector(__instance.level + 5, 10);
                    return false;
                }
                __instance.failedAttempts = 0;
                if (nearbySectorWithHighestBuyingPrice != __instance.Sector)
                {
                    __instance.GoToSector(nearbySectorWithHighestBuyingPrice);
                    return false;
                }

                float unitPrice;
                Station stationBuyingItem = __instance.Sector.GetStationBuyingItem(stockDataByIndex.AsItem, -1, out unitPrice);
                if (stationBuyingItem != null)
                {
                    __instance.SellItemToStation(stationBuyingItem, stockDataByIndex.AsItem, stockDataByIndex.stock);
                }
                else
                {
                    __instance.GoToRandomSector(__instance.level + 10, 10);
                }
                return false;
            }
            if (__instance.wantsToBuyItem == null)
            {
                __instance.DetermineItemToBuy(rand);

                if (__instance.wantsToBuyItem == null)
                {
                    __instance.GoToRandomSectorWithUnlimitedRange();
                    return false;
                }                
            }
            if (__instance.wantsToBuyItem != null)
            {
                ReserveEntry reserveEntry = reservedList.Find(re => re.traderID == __instance.id);

                if (reserveEntry != null)
                {                    
                    if (__instance.Sector.Index != reserveEntry.sectorIndex)
                    {
                        TSector targetSector = GameData.data.sectors[reserveEntry.sectorIndex];
                        if (targetSector.IsBeingAttacked)
                        {
                            __instance.ClearWishlist();
                            return false;
                        }

                        TSector nextSector = null;
                        if (targetSector.DistanceToPositionInGalaxy(__instance.Sector.posV2) <= __instance.MaxWarpDistance)
                            nextSector = targetSector;
                        else
                        {
                            float warpAdjust = 0.05f;
                            do
                            {
                                nextSector = GetClosestSectorToTargetInWarpRange(__instance.Sector, targetSector, Mathf.RoundToInt(__instance.MaxWarpDistance * (1 + warpAdjust)));
                                warpAdjust += 0.05f;
                            }
                            while (nextSector == null);

                            if (cfgDebug.Value) log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ")" + " jumping to: " + nextSector.coords + " warp cheat: +" + warpAdjust + "%");
                        }

                        if (nextSector != null)
                        {
                            __instance.GoToSector(nextSector);
                            return false;
                        }
                    }
                }

                float unitPrice2;
                Station stationSellingItem = __instance.Sector.GetStationSellingItem(__instance.wantsToBuyItem, -1, 1, out unitPrice2);
                if (stationSellingItem != null)
                {
                    int maxQnt = Mathf.Clamp((int)(__instance.credits / unitPrice2), 0, (int)((float)__instance.CargoSpace / __instance.wantsToBuyItem.weight));
                    if (__instance.BuyItemFromStation(stationSellingItem, __instance.wantsToBuyItem, maxQnt))
                        __instance.ClearWishlist();
                    
                }
                else
                    __instance.ClearWishlist();

                return false;
            }
            __instance.GoToRandomSector(__instance.level + 5, 10);

            return false;
        }

        private class ReserveEntry
        {
            internal int traderID;
            internal int sectorIndex;
            internal int itemID;
            internal int qnt;

            internal ReserveEntry(int traderID, int sectorIndex, int itemID, int qty)
            {
                this.traderID = traderID;
                this.sectorIndex = sectorIndex;
                this.itemID = itemID;
                this.qnt = qty;
            }

            internal static ReserveEntry EntryExists(List<ReserveEntry> list, int sectorIndex, int itemID)
            {
                foreach (ReserveEntry entry in list)
                    if (entry.sectorIndex == sectorIndex && entry.itemID == itemID)
                        return entry;

                return null;
            }

            internal static int GetTotalReservedQuantity(List<ReserveEntry> list, int itemID)
            {
                int qnt = 0;

                foreach(ReserveEntry re in list)
                    if (re.itemID == itemID)
                        qnt += re.qnt;

                return qnt;
            }
        }
    }
}
