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
                    lock (Main.listLock)
                    {
                        if (Main.sellTargets.TryGetValue(__instance.dynChar.id, out TSector targetSector) && targetSector != currentSector)
                        {
                            if (targetSector.IsBeingAttacked)
                            {
                                Main.ClearSellTargets(__instance.dynChar, "TargetUnderAttack");
                                return false;
                            }

                            TSector nextSector = UtilityMethods.GetNextSectorTowardsTarget(__instance.dynChar, currentSector, targetSector);
                            if (nextSector != null)
                            {
                                __instance.dynChar.SetActiveState(mode: false);
                                __instance.dynChar.GoToSector(nextSector);
                                (__instance as AIControl).WarpDisappear(true);
                                return false;
                            }
                        }

                        station = currentSector.GetStationBuyingItem(stockDataByIndex.AsItem, -1, out var _);

                        if (station == null)
                        {
                            if (Main.sellTargets.TryGetValue(__instance.dynChar.id, out TSector sellTargetsector))
                            {
                                if (Main.cfgDebug.Value) Main.log.LogInfo("Trader: " + __instance.dynChar.name + " (" + __instance.dynChar.id + "), in sector: " + sellTargetsector.coords + " - Removing Sell Target - At target, no buying station.  Selling list count: " + Main.sellTargets.Count);
                                Main.sellTargets.Remove(__instance.dynChar.id);
                            }
                            __instance.WarpDisappear(true);
                            __instance.dynChar.GoToRandomSector(__instance.dynChar.level + 10, 10);
                            return false;
                        }
                    }
                }
                else
                {
                    if (__instance.dynChar.wantsToBuyItem == null)
                    {
                        Main.ClearReservations(__instance.dynChar, "Looking For New Item");

                        __instance.dynChar.wantsToBuyItem = Main.GetRandomItemToBuy(__instance.dynChar, currentSector.MarketPriceControl(), __instance.dynChar.CommerceLevel, 5);
                    }

                    if (__instance.dynChar.wantsToBuyItem != null)
                    {
                        lock (Main.listLock)
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
                                        __instance.dynChar.SetActiveState(mode: false);
                                        __instance.dynChar.GoToSector(nextSector);
                                        (__instance as AIControl).WarpDisappear(true);                                        
                                        return false;
                                    }
                                }
                            }
                        }

                        station = currentSector.GetStationSellingItem(__instance.dynChar.wantsToBuyItem, -1, 1, out var _);
                        if (station == null)
                        {
                            __instance.WarpDisappear(true);
                            __instance.dynChar.GoToRandomSector(__instance.dynChar.level + 10, 10);
                            __instance.dynChar.wantsToBuyItem = null;

                            Main.ClearBuyReservations(__instance.dynChar, "Null selling station in target sector (local).");

                            lock (Main.listLock)
                            {
                                if (Main.sellTargets.TryGetValue(__instance.dynChar.id, out _))
                                {
                                    Main.ClearSellTargets(__instance.dynChar, "Null selling station in target sector (local).");
                                }
                            }
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
