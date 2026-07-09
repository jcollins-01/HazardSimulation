Welcome to use Ignis - Interactive Fire System!
Full instructions provided in User_Instructions.pdf

Quick-start
Install Visual Effects Graph from Package manager:
Window/Package Manager -> Visual effects graph
Install

--->!!!!! If flames/shaders are not rendering you may need to Open VFX to compile them. !!!!! <-----
Open and close ALL VFX you want to use once: OAVA-Flame/VFX/Flame_Box...
This will compile and convert them to your Rendering Pipeline.

Check that everything is working correctly:
Open HDRP/URP/Standard Demo scene from OAVA-Flame/Scenes
Press play.

Standard RP Sample scene:
If you want post processing, you need to have Post Processing V2 installed from the package manager.

--------------------------------------------------------------------------------------------------------------------------------

INFORMATION FOR BUILT-IN PIPELINE

!!! IF YOU ARE USING STANDARD/BUILT-IN PIPELINE AND VISUAL EFFECTS GRAPH 10.4.0+:
The Visual Effect Graph for Standard/Built-in Pipeline in this exact minor version 10.4.0 seems to be completely broken and doesn't work at all (even a blank new VFX does not work). I suppose and hope this will be fixed in the upcoming version by Unity. Meanwhile here are instructions for downgrading to previous minor version:
1. Download Visual Effects Graph 10.2.0 from the package manager if you can find it there.
2. If you cannot downgrade from the package manager find this file in your project folder: Your_Project/Packages/manifest.json
3. Change Visual Effects Graph Version to 10.2.0
4. Close and Open your project, wait for the import.
5. Done!

!!! IF YOU ARE USING STANDARD/BUILT-IN PIPELINE AND VISUAL EFFECTS GRAPH 7.6.1-7.9.9:
1. Download Visual Effects Graph 7.3.1 from the package manager if you can find it there.
2. If you cannot downgrade from the package manager find this file in your project folder: Your_Project/Packages/manifest.json
3. Change Visual Effects Graph Version to 7.3.1
4. Delete the whole Ignis folder
5. Import Ignis again from the asset store
6. Follow the normal install procedure (Open the VFX graphs etc.)
7. Done!


Also Unity Visual effects graph cannot be downgraded currently on 2021.2.7+ Editor because Unity dropped support, please use editor version below 2021.2.7 to use it with built-in RP.


Instructions for converting the objects and using the system in Documentation/User_Instructions.pdf!
Also video tutorials are available, see the Asset Page.