using System;
using System.Collections.Generic;
using UnityEngine;

namespace MC_SVTraderItemReserve
{
    internal class UtilityMethods
    {
        internal static TSector GetNextSectorTowardsTarget(DynamicCharacter dynChar, TSector currentSector, TSector targetSector)
        {
            TSector nextSector = null;
            if (targetSector.DistanceToPositionInGalaxy(currentSector.posV2) <= dynChar.MaxWarpDistance)
                nextSector = targetSector;
            else
            {
                float warpAdjust = -0.05f;
                int tries = 20;
                do
                {
                    warpAdjust += 0.05f;
                    tries--;
                    nextSector = GetClosestSectorToTargetInWarpRange(currentSector, targetSector, Mathf.RoundToInt(dynChar.MaxWarpDistance * (1 + warpAdjust)));                    
                }
                while (nextSector == null && tries > 0);

                if (Main.cfgDebug.Value)
                {
                    if (tries > 0)
                        Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " jumping to: " + nextSector.coords + " warp cheat: +" + (warpAdjust * 100) + "%");
                    else
                        Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + " failed to find next sector.");
                }
            }

            return nextSector;
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

        internal static List<ItemMarketPrice> FilterBuyingMarketItemList(List<ItemMarketPrice> list, TSector sector, DynamicCharacter dynChar)
        {
            List<ItemMarketPrice> newList = new List<ItemMarketPrice>();
            lock (Main.listLock)
            {
                foreach (ItemMarketPrice imp in list)
                {
                    BuyReservation re = BuyReservation.EntryExists(Main.buyReservations, sector.Index, imp.itemID);
                    if (re == null)
                    {
                        Station station = sector.GetStationSellingItem(imp.AsItem, -1, 10, out float _);
                        if (station != null)
                            newList.Add(imp);
                    }
                    else
                    {
                        Station station = sector.GetStationSellingItem(imp.AsItem, -1, 1, out float unitPrice);
                        if (station != null)
                        {
                            SM_Market market = station.MarketModule;
                            if (market != null)
                            {
                                int availableQnt = market.GetMarketItem(3, imp.itemID, imp.AsItem.rarity, null).Stock - BuyReservation.GetTotalReservedQuantity(Main.buyReservations, imp.itemID, sector.Index);

                                unitPrice = GetSellingPrice(imp, sector);

                                int desiredQnt = Mathf.Clamp((int)(dynChar.credits / unitPrice), 0, (int)(dynChar.CargoSpace / imp.AsItem.weight));

                                if (desiredQnt <= availableQnt)
                                    newList.Add(imp);
                                else if (Main.cfgDebug.Value)
                                    Main.log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to insufficent stock: " + availableQnt + " (desired: " + desiredQnt + ")");
                            }
                            else if (Main.cfgDebug.Value)
                                Main.log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to NULL MARKET MODULE");
                        }
                        else if (Main.cfgDebug.Value)
                            Main.log.LogInfo("Item: " + ItemDB.GetItem(imp.itemID).itemName + " (" + imp.itemID + ")" + " in sector: " + sector.coords + " blocked due to NULL STATION");
                    }
                }
            }

            return newList;
        }

        internal static Tuple<ItemMarketPrice, int> GetFinalBuyingItem(MarketPriceControl mpc, List<ItemMarketPrice> list, DynamicCharacter dynChar)
        {
            ItemMarketPrice finalIMP = list[mpc.Rand.Next(0, list.Count)];
            float finalUnitPrice = GetSellingPrice(finalIMP, mpc.sector);
            int maxQnt = Mathf.Clamp((int)(dynChar.credits / finalUnitPrice), 0, (int)(dynChar.CargoSpace / finalIMP.AsItem.weight));

            if (list.Count == 1 &&
                GetHighestNearbyBuyingPriceForItem(finalIMP.AsItem, dynChar.level, Mathf.RoundToInt((dynChar.MaxWarpDistance * (Main.cfgRandomWarpTries.Value * 0.75f))), mpc.sector, dynChar.level + 5, finalUnitPrice, maxQnt).Item2 == 0)
                return null;

            foreach (ItemMarketPrice item in list)
            {
                float itemUnitPrice = GetSellingPrice(item, mpc.sector);
                maxQnt = Mathf.Clamp((int)(dynChar.credits / finalUnitPrice), 0, (int)(dynChar.CargoSpace / finalIMP.AsItem.weight));

                if ((itemUnitPrice / item.AsItem.basePrice) < (finalUnitPrice / finalIMP.AsItem.basePrice) &&
                    GetHighestNearbyBuyingPriceForItem(item.AsItem, dynChar.level, Mathf.RoundToInt((dynChar.MaxWarpDistance * (Main.cfgRandomWarpTries.Value * 0.75f))), mpc.sector, dynChar.level + 5, itemUnitPrice, maxQnt).Item2 != 0)
                {
                    finalIMP = item;
                    finalUnitPrice = itemUnitPrice;
                }
            }

            return new Tuple<ItemMarketPrice, int>(finalIMP, maxQnt);
        }

        internal static Tuple<TSector, float> GetHighestNearbyBuyingPriceForItem(Item item, int baseLevel, float maxRange, TSector closeToSector, int maxSectorLevel, float higherThanValue, int qntTarget)
        {
            int minLvl = 0;
            int maxLvl = baseLevel + 2;
            float finalHighestBuyingPrice = 0f;
            TSector sector = null;
            while (finalHighestBuyingPrice == 0 && maxLvl <= maxSectorLevel)
            {
                List<TSector> list = GameData.data.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(closeToSector.posV2) <= maxRange);

                foreach(TSector s in list)
                {
                    for (int j = 0; j < s.stationIDs.Count; j++)
                    {
                        Station station = GameData.GetStation(s.stationIDs[j], allowDestroyedOrWrecked: false);
                        if (station != null)
                        {
                            float marketPriceForItem = station.GetMarketPriceForItem(item, 1, out int buyingQnt);

                            if (marketPriceForItem != -1f && marketPriceForItem > finalHighestBuyingPrice && buyingQnt > (Mathf.CeilToInt(qntTarget * (Main.cfgSellQntTarget.Value / 100))))
                            {
                                finalHighestBuyingPrice = marketPriceForItem;
                                sector = s;
                            }
                        }
                    }
                }
                if (finalHighestBuyingPrice == 0)
                {
                    maxLvl += 2;
                }
            }
            return new Tuple<TSector, float>(sector, finalHighestBuyingPrice);
        }

        internal static Tuple<TSector, float> GetLowestAvailableNearbySellingPriceForItem(Item item, int baseLevel, float maxRange, TSector closeToSector, int maxSectorLevel, float lowerThanValue)
        {
            int minLvl = 0;
            int maxLvl = baseLevel + 2;
            float finalLowestSellingPrice = 99999;
            TSector sector = null;
            lock (Main.listLock)
            {
                while (finalLowestSellingPrice == 99999 && maxLvl <= maxSectorLevel)
                {
                    List<TSector> list = GameData.data.sectors.FindAll((TSector s) => s.level >= minLvl && s.level <= maxLvl && !s.IsBeingAttacked && s.DistanceToPositionInGalaxy(closeToSector.posV2) <= maxRange);
                    foreach (TSector s in list)
                    {
                        for (int j = 0; j < s.stationIDs.Count; j++)
                        {
                            Station station = GameData.GetStation(s.stationIDs[j], allowDestroyedOrWrecked: false);
                            if (station != null)
                            {
                                float marketPriceForItem = station.GetMarketPriceForItem(item, 0, out int curStock);

                                int availableStock = station.GetItemStationStock(item) - BuyReservation.GetTotalReservedQuantity(Main.buyReservations, item.id, s.Index);

                                if (marketPriceForItem != -1f && marketPriceForItem < finalLowestSellingPrice && availableStock > 0)
                                {
                                    finalLowestSellingPrice = marketPriceForItem;
                                    sector = s;
                                }
                            }
                        }
                    }
                    if (finalLowestSellingPrice == 99999)
                    {
                        maxLvl += 2;
                    }
                }
            }

            if (sector == null)
                if(Main.cfgDebug.Value) Main.log.LogInfo("No buy location nearby to fill demand for " + item.itemName + " (" + item.id + ") in sector " + closeToSector.coords + " in range: " + maxRange + " <= level: " + maxSectorLevel);

            return new Tuple<TSector, float>(sector, finalLowestSellingPrice);
        }

        internal static float GetSellingPrice(ItemMarketPrice item, TSector sector)
        {
            return item.sellingPrice == -1 ? (item.tradePrice == -1 ? GameData.data.GalacticMarket().GetItemPriceOnSector(item.AsItem.id, sector) : item.tradePrice) : item.sellingPrice;
        }

        internal static float GetBuyingPrice(ItemMarketPrice item, TSector sector)
        {
            return item.buyingPrice == -1 ? (item.tradePrice == -1 ? GameData.data.GalacticMarket().GetItemPriceOnSector(item.AsItem.id, sector) : item.tradePrice) : item.buyingPrice;
        }

        internal static Station GetStationBuyingItem(TSector sector, Item item, int factionID, int qntTarget, out float unitPrice)
        {
            if (factionID == -2)
            {
                factionID = sector.factionControl;
            }
            Station station = null;
            float num = -1f;
            for (int i = 0; i < sector.stationIDs.Count; i++)
            {
                Station station2 = GameData.GetStation(sector.stationIDs[i], allowDestroyedOrWrecked: false);
                if (station2 != null && (station2.factionIndex == factionID || factionID == -1))
                {
                    float marketPriceForItem = station2.GetMarketPriceForItem(item, 1, out int buyingQnt);
                    if (marketPriceForItem != -1f && marketPriceForItem > num && buyingQnt > (Mathf.CeilToInt(qntTarget * (Main.cfgSellQntTarget.Value / 100))))
                    {
                        num = marketPriceForItem;
                        station = station2;
                    }
                }
            }
            if (station != null)
            {
                unitPrice = num;
                return station;
            }
            unitPrice = -1f;
            return null;
        }
    }
}
