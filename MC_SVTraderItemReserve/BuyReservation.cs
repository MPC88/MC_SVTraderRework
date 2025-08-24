using System.Collections.Generic;

namespace MC_SVTraderItemReserve
{
    internal class BuyReservation
    {
        internal int traderID;
        internal int sectorIndex;
        internal int itemID;
        internal int qnt;

        internal BuyReservation(int traderID, int sectorIndex, int itemID, int qty)
        {
            this.traderID = traderID;
            this.sectorIndex = sectorIndex;
            this.itemID = itemID;
            this.qnt = qty;
        }

        internal static int GetTotalReservedQuantity(List<BuyReservation> list, int itemID, int sectorIndex)
        {
            int qnt = 0;

            foreach (BuyReservation re in list)
                if (re.itemID == itemID && re.sectorIndex == sectorIndex)
                    qnt += re.qnt;

            return qnt;
        }
    }
}
