using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MC_SVTraderItemReserve
{
    internal class DynamicCharacterPatches
    {
        [HarmonyPatch(typeof(CharacterSystem), nameof(CharacterSystem.UpdateCharacters))]
        [HarmonyPrefix]
        private static void CharacterSystemUpdate_Pre(CharacterSystem __instance, bool forced)
        {
            if (forced || GameData.timePlayed - __instance.timeCountOnLastUpdate >= 30f)
                if (Main.cfgDebug.Value) Main.log.LogInfo("-------------------------------------------------- UPDATING TRADERS --------------------------------------------------");
        }


        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.DetermineItemToBuy))]
        [HarmonyPrefix]
        private static bool DynCharDetermineItemToBuy_Pre(DynamicCharacter __instance)
        {
            Main.curTraderID = __instance.id;
            ClearReservations(__instance, "Looking For New Item");
            return true;
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
                if (Main.sellTargets.TryGetValue(__instance.id, out TSector targetSector) && targetSector != __instance.Sector)
                {
                    if (targetSector.IsBeingAttacked)
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - Target under attack.");
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
                else
                {
                    if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ")");
                    float higherThanValue = ((__instance.failedAttempts < Main.randomWarpsBeforeSellingAtZero) ? __instance.lastUnitPricePaid : 0f);
                    if (higherThanValue == 0)
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") - Failed 3 attempts, setting target value to 0 and randomly warping.");
                    TSector nearbySectorWithHighestBuyingPrice = GameData.data.GetNearbySectorWithHighestBuyingPrice(stockDataByIndex.AsItem, __instance.level, __instance.MaxWarpDistance, __instance.Sector, __instance.level + 5, higherThanValue);
                    if (nearbySectorWithHighestBuyingPrice == null)
                    {
                        __instance.failedAttempts++;
                        __instance.GoToRandomSector(__instance.level + 5, 10);
                        return false;
                    }
                    if (nearbySectorWithHighestBuyingPrice != __instance.Sector)
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ") trying to sell: " + ItemDB.GetItem(stockDataByIndex.itemID).itemName + " (" + stockDataByIndex.itemID + ") - Found nearby sector, going to: " + nearbySectorWithHighestBuyingPrice.coords);
                        __instance.GoToSector(nearbySectorWithHighestBuyingPrice);
                        return false;
                    }
                }

                float unitPrice;
                Station stationBuyingItem = __instance.Sector.GetStationBuyingItem(stockDataByIndex.AsItem, -1, out unitPrice);
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
                return false;
            }
            if (__instance.wantsToBuyItem == null)
            {
                __instance.DetermineItemToBuy(rand);

                if (__instance.wantsToBuyItem == null)
                {
                    if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.name + " (" + __instance.id + ")" + " found nothing to buy.  Warping");
                    if (Main.sellTargets.TryGetValue(__instance.id, out TSector targetSector))
                    {
                        if (Main.cfgDebug.Value) Main.log.LogInfo("Ttrader: " + __instance.name + " (" + __instance.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - At target, no buying station.");
                        Main.sellTargets.Remove(__instance.id);
                    }
                    __instance.GoToRandomSectorWithUnlimitedRange();
                    return false;
                }
            }
            if (__instance.wantsToBuyItem != null)
            {
                BuyReservation reserveEntry = Main.buyReservations.Find(re => re.traderID == __instance.id);

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

                        TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance, __instance.Sector, targetSector);

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

        internal static void ClearReservations(DynamicCharacter dynChar, string cause)
        {
            if (Main.cfgDebug.Value)
            {
                List<BuyReservation> remove = new List<BuyReservation>();
                foreach (BuyReservation re in Main.buyReservations)
                {
                    if (re.traderID == dynChar.id)
                    {
                        Main.log.LogInfo("Trader: " + dynChar.name + " (" + dynChar.id + ")" + ", Item: " + ItemDB.GetItem(re.itemID).itemName + " (" + re.itemID + ")" + ", qnt: " + re.qnt + ", sector: " + GameData.data.sectors[re.sectorIndex].coords + " - Removing Reservation - " + cause);
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

        [HarmonyPatch(typeof(DynamicCharacter), nameof(DynamicCharacter.ClearWishlist))]
        [HarmonyPostfix]
        private static void DynamicCharacterClearWishlist_Post(DynamicCharacter __instance)
        {
            lock (GameData.threadSaveLock)
            {
                ClearReservations(__instance, "Wish list Clear");
            }
        }
    }
}
