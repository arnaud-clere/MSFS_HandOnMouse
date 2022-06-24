# MSFS_HandOnMouse
MSFS Utility to keep your HandOnMouse for easier/better flying

## Version 3.0

ALPHA version, testing with MSFS Sim Update 9 to do

Please install in a separate place than version 2 as some user configuration settings changed. 
Customized .ini mappings files from version 2 "Mappings" folder can be copied to the new version "Mappings" folder without change.
* Improves mouse move sensitivity when connected
* Adds control (in %/s) of axis changes from MSFS to avoid, e.g. throttle set to zero when exiting MSFS "options" menu
* Improves main window to select predefined axis mappings more intuitively and hide/lock specific ones from the main window
* Adds ability to hide topmost gauges window
* Adds ability to lock specific axis
* Adds standard gear icons for advanced settings instead of "..." buttons
* Alt+F4 does not quit HandsOnMouse anymore when connected
* Removes unreliable SimConnect in-sim Text messages that appeared indefinitely in specific conditions
* Fixes sporadic input loss after loading an aircraft
* Adds detection of missing trim axis for hiding mappings

## Version 2.1

* Improves axis window to understand options more intuitively
* Adds ability to hide specific axis (those Not Available are hidden automatically)

## Version 2.0

* Adds support for triggering axis changes from HID compatible device buttons like joystick triggers
* Adds configuration button to change mappings trigger, direction and smart options and save them to a user specific file or reset them to the defaults defined in the mappings file
* Adds optional log file for troubleshooting if needed

## Version 1.4

* Adds vJoy.ini mappings using vJoy including some smart features (detents, autocentering) to better support planes not totally supporting SimVars like Aerosoft's CRJ

## Version 1.3

* Adds visual cues to reverse throttle and center of left/right axis
* Adds other smart mappings, used in Thr+Yoke+Pedals.ini to replace a yoke or pedals : aileron, rudder and brakes

## Version 1.2

* Adds other smart mappings :
- flaps maximum value adapts to the current aircraft
- throttle minimum value adapts to the current aircraft to enable reverse throttle when available

## Version 1.1

* Adds smart trim mappings making it a lot easier and faster to trim an aircraft:
- elevator/aileron trim sensitivity adapts to aircraft's IAS and design Vc to compensate higher aerodynamic forces for higher velocities 
- elevator trim automatically compensates centering elevator inputs to compensate for the absence of Force-Feedback hardware

## Version 1

* Initial release
