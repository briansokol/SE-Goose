# SE Goose Inventory Sorter - Functional Features
This document details out a wishlist of features for a Space Engineers Inventory Sorting Script
## Similar Functionality
- Automated Inventory Sorting
    - Oroginal https://steamcommunity.com/sharedfiles/filedetails/?id=321588701 
    - Munchies Fork https://steamcommunity.com/sharedfiles/filedetails/?id=2593266146
- Isy's https://steamcommunity.com/sharedfiles/filedetails/?id=1226261795
## Core Features
- **By Default, the script will NOT sort any subgrid conneced via a connector**
    - It should still sort subgrids connected via pistonm, Rotor and Hinge.
    - to allow script to sort accros a connector, the connecting grid's connector AND the host grid connector MUST have `[ALLOW]` in ==Custom Data==
- **Sorting Inventory by Item Category and custom Categories**
    - `Ingots`
        - `Ingots-<ingot>` (sorting for that ingot only)
    - `Ores`
        - `Ores-<ore>` (sorting for that ore only)
    - `Components` (all non Prototech Components)
        - `Component-<item>` (sorting for that item only)
    - `Prototech` 
    - `Tools` (All Tools and bottles)
        - `Bottles` (Bottles only)
    - `Weapons`
    - `Ammo` (All Ammo)
        - `Ammo-Mag` (handheld weapon Ammo minus Rockets)
        - `Ammo-Stat` (Ammo for grid based static and turret weapons)
    - `Consumables` (all consumables that are not ingredients or meals)
    - `Ingredients` (all cooking ingredients including raw AND cooked meats)
    - `Meals` (all producted meal packets)
    - `Misc` (includes plushies, datapads and Credits)
- **Use of Block ==Custom Data== to define sorting, as well as tagging the container names**
- **Ability to string sorting and priorities**
- **Operators**
    - `[Ignore]` or `[Locked]` (Script will not touch contents of the block)
    - `[Manual]` script will ignore that production block and not manage it.
    - `[Stock]` or `[Custom]`
- **Container Priority**
    - Use of `[P:##]`
        - with `[P:01]` being the highest priority and `[P:99]` being the lowest
        - No priority assigned is the lowest priority.  Eseentially `[P:100]`
        - O2/H2 Gens, Reactors, Refineries, Irrigation, Weapons (static and turrets) all have the highest priority.  Essentially `[P:00]`
## Optional Functionaling
- **Autofilling Bottles not at 100% before sorting to `[Tools]` or `[Bottles]`**
- **ON by default**
    - Rector Handling
        - Automatically balance Uranium between all reacters on the grid.  Ammount can be modified in script
    - Weapon Handling
        - Automatically loads X number of rounds to each weapong type.  Ammounts can be modified in script
    - O2/H2 Generator and Irrigation System Handling
        - Balance Ice accross all blocks on the grid.  Ammounts can be modified in script
- **OFF by default**
    - Refinery Handling
    - Assembler Autocrafting
    - Food Processor Autocrafting
    - Cross Connector sorting
## MOD Integration/Support 
- Integration with ==**GSIM Integration**== https://steamcommunity.com/sharedfiles/filedetails/?id=3613336393 
- Support for ==**Colorful Icons**== https://steamcommunity.com/sharedfiles/filedetails/?id=801185519
## LCD Integration and Script Control
Other inventory management scripts/mods include LCD Control.  At first I didn't really consider that a super important feature as most of us use AutoLCD; however, there are some benifits to using LCDs.  Not just for displaying data, but also for controlling the script (instead of needed to contstantly go back to the programable block).
### LCD Types
Most of the control/options should be handled in ==Custom Data== of the LCD
- Core/Status `[GCore]`
    - Summary Data for the script. Counts for assemblers, refineries, cargo, o2/h2, etc.
    - Other data needed or wanted
- Error/activity Log `[GError]`
    - Simply display the error or full activity log
- Distribution/Priority Management `[GRef]`
    - O2/H2 Generators: *Toggle On/Off, controll ammounts for Large and Small grid.*
    - Irrigation System: *Toggle On/Off, controll ammounts for Large and Small grid.*
    - Static Weapons & Turrets: *Toggle On/Off, controll ammounts for Large and Small grid.*
    - Refineries: Global Ore Priority so script knows what ores to keep in front of the refinery que.
        - *Each Refinery's ==Custom Data== can be used to determine which Ores are allowed int that refinery and what their priority is.*
- Autocrafting Management `[GCraft]`
    - required if autocrafting is enabled
    - I really like how GOAT handles this feature.
    - Separate LCDs for Assemblers and Food Processors maybe?
- Category Management `[GCat]`
    - The idea here is how GOAT uses ==Custom Data== on the Programable Block to manage categories.  I REALLY like this feature of GOAT and it's where the *GSIM Integration* mod comes into play.