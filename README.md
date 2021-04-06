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
**Backward alone**   | **Flaps** (forward-backward move) or **Landing Gear** (left-right move)<br>or with mapping files with trim:<br>**Elevator trim** (forward-backward move) or **Aileron trim** (left-right move)
**Forward+Backward** | **Propeller** or **Mixture** or **Spoilers** (for jets and gliders)  

*NB: Controls with few positions such as flaps and gear are configured to wait for the buttons to be released before actually doing anything so you can revert an inadvertent move.*

Otherwise, you can edit a documented Custom.ini mappings file to use other buttons, control other Simulation Variables, etc.

Enjoy, and let me know what you think!

Cheers, 
Arnaud

## Version 1

Initial release

## Version 1.1

Adds smart trim mappings making it a lot easier and faster to trim an aircraft:
- elevator/aileron trim sensitivity adapts to aircraft's IAS and design Vc to compensate higher aerodynamic forces for higher velocities 
- elevator trim automatically compensates centering elevator inputs to compensate for the absence of Force-Feedback hardware

> When you already have a good way to actuate flaps and ailerons, and a spring-centered stick/yoke, you should **try to trim with the mouse as this will save you previous time during your approaches**.
> Trimming is essential to a stabilized approach but it is difficult to simulate due to the variety of trimming implementations and the sparsity of force-feedback hardware. 
> In MSFS it is more complex than it is in real life, at least in GA aircrafts and gliders. 
> So, the utility implements it in a more efficient way so you can concentrate on all the other things to do during an approach.

When you need to apply too much effort to maintain your stick/yoke in position:
1. Use the mouse to grab the trim control (press Backward button)
2. Gently center back your stick/yoke (you can also move the mouse forward/backward to maintain a perfect attitude) 
3. Release the trim control (Backward button)
4. If trimming is not perfect, you can adjust it with the mouse, or with stick/yoke (back to #1)

*NB: The default smart trim sensitivity works well for most default aircrafts but not all, so, you can adjust it in a custom mappings file (use the "..." button)*


