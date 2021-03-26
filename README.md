# MSFS_HandOnMouse
MSFS Utility to keep your HandOnMouse for easier/better flying

> MSFS virtual cockpits are wonderfully immersive but can become challenging when flying requires to actuate critical knobs and levers quickly.
> * Takeoffs may require to quickly retract gear and/or flaps before critical speeds are reached.
> * Landings may require to adjust flaps (or even gliders' spoilers) and/or trim without delay.
> 
> Even with dedicated hardware such as quadrants, having to leave your mouse may badly delay other actions such as preparing the aircraft (lights, accessories), completing checklists, answering ATC, etc. I guess using VR mode makes all of these even more challenging.
> 
> The solution I use for quite some time now is this small utility that connects to MSFS to map critical actions to mouse moves combined with dedicated buttons pressed.

## Quick start guide

1. Run the application
2. Test the mouse sensitivity and adjust as required (depends on mouse resolution and desired range)
3. Use the default mappings or click "..." to select mappings more adapted to your plane
4. Connect to MSFS and check that you can actuate the chosen controls with the mouse
5. Either hide the application window with "-" button or keep it on top in a reduced form with "=" button

Since many mouse are equipped with forward/backward buttons and these are not mapped by default to anything, the utility comes with preconfigured mappings suited to various aircraft types using these buttons:
Mouse Button(s)      | MSFS Control
---------------      | ------------
**Forward alone**    | **Throttle**
**Forward+Backward** | **Propeller** or **Mixture** or **Spoilers** (for jets and gliders)  
**Backward alone**   | **Flaps** (forward-backward move) or **Landing Gear** (left-right move)

*NB: Controls with few positions such as flaps and gear are configured to wait for the buttons to be released before actually doing anything so you can revert an inadvertent move.*

Otherwise, you can edit a documented Custom.ini mappings file to use other buttons, control other Simulation Variables, etc.

Enjoy, and let me know what you think!

Cheers, 
Arnaud

## Version 1

Initial release
