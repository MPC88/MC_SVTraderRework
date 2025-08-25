using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MC_SVTraderRework
{
    internal class DynamicCharacterPatches
    {
        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.UpdateCharacter))]
        [HarmonyPrefix]
        private static bool DynamicCharacterUpdate_Pre(DynamicCharacter __instance)
        {
            // This is a kludge.  Ended up with a trader in sector -1 and I don't know why.
            // Could be a bug here somewhere, could be because I wasn't clearing buy/sell lists between quit/loads, could be an old issue I had and fixed without knowing.  Just don't know.
            if (__instance.currSectorID < 0 || __instance.currSectorID >= GameData.data.sectors.Count)
            {
                if (Main.cfgDebug.Value) Main.log.LogError("Trader: " + __instance.name + " (" + __instance.id + ") in invalid sector: " + __instance.currSectorID + ".  Setting to 0.");
                __instance.currSectorID = 0;
                Main.ClearBuyReservations(__instance, "Invalid current sector");
            }
            // end of kludge.
            
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
                int qntTarget = stockDataByIndex.stock;
                lock (Main.listLock)
                {
                    TSector targetSector = null;
                    if (Main.sellTargets.TryGetValue(__instance.id, out targetSector))
                        qntTarget = 1; // If trader has a sell target, it's supplying and will supply any qnt.

                    if (targetSector != null && targetSector != __instance.Sector)
                    {
                        if (targetSector.IsBeingAttacked)
                        {
                            if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - Target under attack.  Selling list count: " + Main.sellTargets.Count);
                            Main.sellTargets.Remove(__instance.id);
                            return false;
                        }

                        TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance, __instance.Sector, targetSector);
                        if (nextSector != null)
                        {
                            __instance.GoToSector(nextSector);
                            return false;
                        }
                    }
                    else if (!Main.sellTargets.TryGetValue(__instance.id, out _))
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ")");
                        float higherThanValue = ((__instance.failedAttempts < Main.cfgRandomWarpTries.Value) ? __instance.lastUnitPricePaid : 0f);
                        if (higherThanValue == 0)
                            if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") - Failed " + Main.cfgRandomWarpTries.Value + " attempts, setting target value to 0 and randomly warping.");

                        TSector nearbySectorWithHighestBuyingPrice = UtilityMethods.GetHighestNearbyBuyingPriceForItem(stockDataByIndex.AsItem, __instance.level, __instance.MaxWarpDistance, __instance.Sector, __instance.level + 5, higherThanValue, qntTarget).Item1;
                        
                        if (nearbySectorWithHighestBuyingPrice == null)
                        {
                            __instance.failedAttempts++;
                            __instance.GoToRandomSector(__instance.level + 5, 10);
                            return false;
                        }                        
                        else if (nearbySectorWithHighestBuyingPrice != __instance.Sector)
                        {
                            if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") - Found nearby sector, going to: " + nearbySectorWithHighestBuyingPrice.coords);
                            __instance.GoToSector(nearbySectorWithHighestBuyingPrice);
                        }
                    }

                    Station stationBuyingItem = UtilityMethods.GetStationBuyingItem(__instance.Sector, stockDataByIndex.AsItem, -1, qntTarget, out _);
                    
                    if (stationBuyingItem != null)
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") found station in sector.");
                        __instance.SellItemToStation(stationBuyingItem, stockDataByIndex.AsItem, stockDataByIndex.stock);
                        __instance.failedAttempts = 0;
                    }
                    else
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") failed to find station in setor, randomly warping.");
                        __instance.GoToRandomSector(__instance.level + 10, 10);
                    }
                }
                return false;
            }

            if (__instance.wantsToBuyItem == null)
            {
                DetermineItemToBuy(__instance, rand);

                if (__instance.wantsToBuyItem == null)
                {
                    if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ")" + " found nothing to buy.  Warping");
                    lock (Main.listLock)
                    {
                        if (Main.sellTargets.TryGetValue(__instance.id, out TSector targetSector))
                        {
                            if (Main.cfgDebug.Value) Main.log.LogInfo("Ttrader: " + __instance.name + " (" + __instance.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - At target, no buying station.  Selling list count: " + Main.sellTargets.Count);
                            Main.sellTargets.Remove(__instance.id);
                        }
                    }
                    __instance.GoToRandomSectorWithUnlimitedRange();
                    return false;
                }
            }
            if (__instance.wantsToBuyItem != null)
            {
                lock (Main.listLock)
                {
                    BuyReservation buyReservation = Main.buyReservations.Find(re => re.traderID == __instance.id);

                    if (buyReservation != null)
                    {
                        if (__instance.Sector.Index != buyReservation.sectorIndex)
                        {
                            TSector targetSector = GameData.data.sectors[buyReservation.sectorIndex];
                            if (targetSector.IsBeingAttacked)
                            {
                                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") Target sector under attack.");
                                Main.ClearBuyReservations(__instance, "Target under attack");
                                
                                if (Main.sellTargets.TryGetValue(__instance.id, out _))
                                    Main.ClearSellTargets(__instance, "Buy target under attack.");
                                return false;
                            }

                            TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance, __instance.Sector, targetSector);

                            if (nextSector != null)
                            {
                                __instance.GoToSector(nextSector);
                                return false;
                            }
                        }
                    }
                }

                Station stationSellingItem = __instance.Sector.GetStationSellingItem(__instance.wantsToBuyItem, -1, 1, out float unitPrice2);
                if (stationSellingItem != null)
                {
                    int maxQnt = Mathf.FloorToInt(Mathf.Clamp((int)(__instance.credits / unitPrice2), 0, (int)((float)__instance.CargoSpace / __instance.wantsToBuyItem.weight)));
                    if (__instance.BuyItemFromStation(stationSellingItem, __instance.wantsToBuyItem, maxQnt))
                    {
                        __instance.wantsToBuyItem = null;
                        Main.ClearBuyReservations(__instance, "Bought Items");
                    }
                }
                else
                {
                    __instance.wantsToBuyItem = null;
                    Main.ClearBuyReservations(__instance, "Null selling station in target sector (background).");
                    lock (Main.listLock)
                    {
                        if (Main.sellTargets.TryGetValue(__instance.id, out _))
                            Main.ClearSellTargets(__instance, "Null selling station in target sector (background).");
                    }
                }

                return false;
            }
            __instance.GoToRandomSector(__instance.level + 5, 10);

            return false;
        }

        private static void DetermineItemToBuy(DynamicCharacter dynChar, System.Random rand)
        {
            if (rand == null)
                rand = CharacterSystem.Rand;

            Main.ClearReservations(dynChar, "Looking For New Item");

            // Get nearby producer with insufficient stock
            dynChar.wantsToBuyItem = GetNearbyProducerWithInsufficientStock(dynChar, dynChar.CommerceLevel, dynChar.MaxWarpDistance, dynChar.Sector, dynChar.level + 5, rand);

            if (dynChar.wantsToBuyItem == null)
            {
                // Look for best purchase
                dynChar.wantsToBuyItem = GetBestItemOfferNearby(dynChar.id, dynChar.CommerceLevel, dynChar.MaxWarpDistance, dynChar.Sector, dynChar.level + 5, rand);
            }
            if (dynChar.wantsToBuyItem == null)
            {
                // Get random item
                dynChar.wantsToBuyItem = Main.GetRandomItemToBuy(dynChar, dynChar.Sector.MarketPriceControl(), dynChar.CommerceLevel, 5);
            }
        }

        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.ClearWishlist))]
        [HarmonyPostfix]
        private static void DynamicCharacterClearWishlist_Post(DynamicCharacter __instance)
        {
            Main.ClearReservations(__instance, "Wish list Clear");
        }

        internal static Item GetNearbyProducerWithInsufficientStock(DynamicCharacter dynChar, int commerceLevel, float maxRange, TSector fromSector, int maxSectorLevel, System.Random rand)
        {
            if (rand == null)
                rand = new System.Random();

            ItemMarketPrice finalItem = null;
            Dictionary<ItemMarketPrice, BuyReservation> zeroStockBuyLocs = new Dictionary<ItemMarketPrice, BuyReservation>();
            int maxLvl = commerceLevel + 2;
            int minLvl = Main.cfgMinSectorLevelSupplying.Value <= 1 ? 1 : Mathf.FloorToInt(maxLvl / Main.cfgMinSectorLevelSupplying.Value);
            if (minLvl < 1) minLvl = 1;
            while (finalItem == null && maxLvl <= maxSectorLevel)
            {
                zeroStockBuyLocs.Clear();
                List<TSector> sectorList = GameData.data.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(fromSector.posV2) <= maxRange && s.MarketPriceControl().HasAnyItemOffer(minLvl - 2, maxLvl + 2));
                List<ItemMarketPrice> zeroStockItems = new List<ItemMarketPrice>();

                if (sectorList.Count > 0)
                {
                    foreach (TSector s in sectorList)
                    {
                        if (Main.sellTargets.Values.ToList().FindAll(sellSector => sellSector.Index == s.Index).Count >= Main.cfgMaxSuppliersPerSector.Value)
                            continue;

                        int min = minLvl >= 2 ? minLvl - 2 : 1;
                        ItemMarketPrice zeroStockItem = GetRandomInsufficientStockItemInSector(s, min, maxLvl + 2);
                        if (zeroStockItem != null)
                            zeroStockItems.Add(zeroStockItem);
                    }

                    if (zeroStockItems.Count > 0)
                    {
                        foreach (ItemMarketPrice zeroStockItem in zeroStockItems)
                        {
                            Tuple<TSector, float> buyLoc = UtilityMethods.GetLowestAvailableNearbySellingPriceForItem(zeroStockItem.AsItem, commerceLevel, Mathf.RoundToInt((dynChar.MaxWarpDistance * (Main.cfgRandomWarpTries.Value * 0.75f))), fromSector, maxSectorLevel, UtilityMethods.GetBuyingPrice(zeroStockItem, zeroStockItem.mpc.sector), zeroStockItem.mpc.sector);
                            if (buyLoc.Item1 != null)
                            {
                                int maxQnt = Mathf.Clamp((int)(dynChar.credits / buyLoc.Item2), 0, (int)(dynChar.CargoSpace / zeroStockItem.AsItem.weight));
                                zeroStockBuyLocs.Add(zeroStockItem, new BuyReservation(dynChar.id, buyLoc.Item1.Index, zeroStockItem.itemID, maxQnt));
                            }
                        }

                        if (zeroStockBuyLocs.Count > 0)
                            finalItem = zeroStockBuyLocs.Keys.ToList()[rand.Next(0, zeroStockBuyLocs.Keys.Count)];
                    }
                }

                if (finalItem == null)
                {
                    maxLvl += 2;
                }
            }

            if (finalItem == null)
                return null;

            lock (Main.listLock)
            {
                Main.sellTargets.Add(dynChar.id, finalItem.mpc.sector);
                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " planning to supply Item: " + ItemDB.GetItem(finalItem.AsItem.id).itemName + " (" + finalItem.AsItem.id + ")" + " to sector: " + finalItem.mpc.sector.coords + ".  Selling list count: " + Main.sellTargets.Count);
                Main.buyReservations.Add(zeroStockBuyLocs[finalItem]);
                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " reserving Item: " + ItemDB.GetItem(finalItem.AsItem.id).itemName + " (" + finalItem.AsItem.id + ")" + " Qnt: " + zeroStockBuyLocs[finalItem].qnt + " in sector: " + GameData.data.sectors[zeroStockBuyLocs[finalItem].sectorIndex].coords + " - Reserved list count: " + Main.buyReservations.Count);
            }
            return finalItem.AsItem;
        }

        private static ItemMarketPrice GetRandomInsufficientStockItemInSector(TSector sector, int minItemLvl, int maxItemLvl)
        {
            List<ItemMarketPrice> list = new List<ItemMarketPrice>();

            for(int i = 0; i < sector.stationIDs.Count; i++)
            {
                Station station = GameData.GetStation(sector.stationIDs[i]);
                if (station == null) continue;

                SM_Market market = station.MarketModule;
                if (market == null) continue;

                foreach(MarketItem item in market.MarketList)
                    if (item.JustBuying && item.Stock <= Main.cfgInsufficientStockLimit.Value)
                        list.Add(sector.MarketPriceControl().prices.Find(imp => imp.itemID == item.itemID));
            }

            if (list.Count <= 0)
                return null;

            return list[sector.MarketPriceControl().Rand.Next(0, list.Count)];
        }

        internal static Item GetBestItemOfferNearby(int dynCharID, int commerceLevel, float maxRange, TSector fromSector, int maxSectorLevel, System.Random rand)
        {
            if (rand == null)
                rand = new System.Random();

            Tuple<ItemMarketPrice, int> finalResult = null;
            int maxLvl = commerceLevel + 2;
            int minLvl = Main.cfgMinSectorLevelBuying.Value <= 1 ? 1 : Mathf.FloorToInt(maxLvl / Main.cfgMinSectorLevelBuying.Value);
            if (minLvl < 1) minLvl = 1;
            while (finalResult == null && maxLvl <= maxSectorLevel)
            {
                List<TSector> list = GameData.data.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(fromSector.posV2) <= maxRange && s.MarketPriceControl().HasAnyItemOffer(minLvl - 2, maxLvl + 2));
                List<Tuple<ItemMarketPrice, int>> bestItemOffers = new List<Tuple<ItemMarketPrice, int>>();

                if (list.Count > 0)
                {
                    foreach (TSector s in list)
                    {
                        int min = minLvl >= 2 ? minLvl : 0;
                        Tuple<ItemMarketPrice, int> bestSectorItem = GetBestItemOfferInSector(dynCharID, s.MarketPriceControl(), min, maxLvl + 2);
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
                return null;

            lock (Main.listLock)
            {
                Main.buyReservations.Add(new BuyReservation(dynCharID, finalResult.Item1.mpc.sector.Index, finalResult.Item1.AsItem.id, finalResult.Item2));
                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + GameData.data.characterSystem.dynChars.Find(dc => dc.id == dynCharID).name + " (" + dynCharID + ")" + " reserving Item: " + ItemDB.GetItem(finalResult.Item1.AsItem.id).itemName + " (" + finalResult.Item1.AsItem.id + ")" + " Qnt: " + finalResult.Item2 + " in sector: " + finalResult.Item1.mpc.sector.coords + ".  Reserved list count: " + Main.buyReservations.Count);
            }
            return finalResult.Item1.AsItem;
        }

        private static Tuple<ItemMarketPrice, int> GetBestItemOfferInSector(int dynCharID, MarketPriceControl __instance, int minItemLvl, int maxItemLvl)
        {
            DynamicCharacter dynChar = GameData.data.characterSystem.dynChars.Find(dc => dc.id == dynCharID);

            List<ItemMarketPrice> newList = UtilityMethods.FilterBuyingMarketItemList(
                __instance.prices.FindAll((ItemMarketPrice p) => p.IsSelling && p.AsItem.itemLevel >= minItemLvl && p.AsItem.itemLevel <= maxItemLvl),
                __instance.sector,
                dynChar);

            if (newList.Count <= 0)
                return null;

            return UtilityMethods.GetFinalBuyingItem(__instance, newList, dynChar);
        }

    }
}
