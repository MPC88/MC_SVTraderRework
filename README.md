# MC_SVTraderRework  
  
Backup your save before using any mods.  
  
Uninstall any mods and attempt to replicate issues before reporting any suspected base game bugs on official channels.  
  
Install  
=======  
1. Install BepInEx - https://docs.bepinex.dev/articles/user_guide/installation/index.html Stable version 5.4.21 x86.  
2. Run the game at least once to initialise BepInEx and quit.  
3. Download latest mod release .zip archive.  
4. Place MC_SVTraderRework.dll into .\SteamLibrary\steamapps\common\Star Valor\BepInEx\plugins\  
  
Use / Configuration  
=====
- AI as well as player markets for stopped production lines are prioritized (trigger stock quantity configurable)  
- Up to 10 traders will jump on a stopped production line at a time to prevent oversupply (configurable)  
- Lower level limit removed, all traders retain the ability to transport iron/aluminium/etc (configurable)  
- Buy items are being reserved - no more 250 traders coming for the same 2 coffee in your station with 249 traders leaving empty  
- Traders check for sales before buying, preventing looping issues  
  
All of this means traders transport a lot more different types of goods, they spread out more, are more efficient, and the vast majority of producers in the galaxy remain active.  
  
Note that all this means trader update cycles take longer to run, but as it is background work it shouldn't be noticable during gameplay.  
  
After first run, a configuration file mc.starvalor.traderrework.cfg will be created in .\Star Valor\BepInEx\Config\.  This file has several settings to customise trader behaviour.  Entries marked with a * can improve performance if changed:  
  
Buying related:  
1. Minimum supply quantity to buy - Minimum quantity a trader wants when buying an item to supply a station.  
2. *Minimum buying sector level - Minimum sector level a trader will search for a purchase in.  1 = level 1.  Higher values are divisors of max level (trader level + 5) e.g. a value of 3 will be 1/3 of trader's max level.  A value of 2 or 1.n can improve performance by limiting sector range.  
3. *Minimum supply sector level - As above, but when looking for stations to supply.  A value of 2 or 1.n can improve performance by limiting sector range.  
4. Insufficient stock limit - The stock level of an input item required for a trader to consider it in need of supplying.  
  
Selling related:  
1. Max suppliers per sector - Limits how many traders will try to supply a single sector at any one time.  
2. *Random warp tries - How many random warps a trader makes looking for a sell location for a bought item.  Lower values can improve performance.  
3. Sell quantity target % - % of current cargo a trader will want to offload when attempting to sell (meant to prevent 1 by 1 selling).  
  
Debug:  
1. Debug enable/disable.  You wont need this, just leave it off.
   
Credit  
=====  
Huge thanks to Ladyhawk for all the data analysis and testing I could never bring myself to do.  
