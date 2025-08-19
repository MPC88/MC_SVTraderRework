using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        internal static BuyReservation EntryExists(List<BuyReservation> list, int sectorIndex, int itemID)
        {
            foreach (BuyReservation entry in list)
                if (entry.sectorIndex == sectorIndex && entry.itemID == itemID)
                    return entry;

            return null;
        }

        internal static int GetTotalReservedQuantity(List<BuyReservation> list, int itemID)
        {
            int qnt = 0;

            foreach (BuyReservation re in list)
                if (re.itemID == itemID)
                    qnt += re.qnt;

            return qnt;
        }
    }
}
