using HarmonyLib;
using UnityEngine;

namespace MC_SVTraderItemReserve
{
    internal class AITraderControlPatches
    {
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
                    lock (GameData.threadSaveLock)
                    {
                        if (Main.sellTargets.TryGetValue(__instance.dynChar.id, out TSector targetSector) && targetSector != currentSector)
                        {
                            if (targetSector.IsBeingAttacked)
                            {
                                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.dynChar.name + " (" + __instance.dynChar.id + "), in sector: " + targetSector.coords + " - Removing Sell Target - Target under attack.");
                                Main.sellTargets.Remove(__instance.dynChar.id);
                                return false;
                            }

                            TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance.dynChar, currentSector, targetSector);
                            if (nextSector != null)
                            {
                                __instance.WarpDisappear(true);
                                __instance.dynChar.GoToSector(nextSector);
                                return false;
                            }
                        }
                        station = currentSector.GetStationBuyingItem(stockDataByIndex.AsItem, -1, out var _);
                        if (station == null)
                        {
                            if (Main.sellTargets.TryGetValue(__instance.dynChar.id, out TSector sellTargetsector))
                            {
                                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.dynChar.name + " (" + __instance.dynChar.id + "), in sector: " + sellTargetsector.coords + " - Removing Sell Target - At target, no buying station.");
                                Main.sellTargets.Remove(__instance.dynChar.id);
                            }
                            __instance.WarpDisappear(true);
                            return false;
                        }
                    }
                }
                else
                {
                    if (__instance.dynChar.wantsToBuyItem == null)
                    {
                        lock (GameData.threadSaveLock)
                        {
                            Main.curTraderID = __instance.dynChar.id;
                            DynamicCharacterPatches.ClearReservations(__instance.dynChar, "Looking For New Item");

                            __instance.dynChar.wantsToBuyItem = currentSector.MarketPriceControl().GetRandomItemToBuy(__instance.dynChar.CommerceLevel, 5);
                        }
                    }
                    if (__instance.dynChar.wantsToBuyItem != null)
                    {
                        lock (GameData.threadSaveLock)
                        {
                            BuyReservation reserveEntry = Main.buyReservations.Find(re => re.traderID == __instance.dynChar.id);

                            if (reserveEntry != null)
                            {
                                if (currentSector.Index != reserveEntry.sectorIndex)
                                {
                                    TSector targetSector = GameData.data.sectors[reserveEntry.sectorIndex];
                                    TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance.dynChar, currentSector, targetSector);
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
                        if (station == null)
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
    }
}
