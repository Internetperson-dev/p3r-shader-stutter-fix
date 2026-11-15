# Third-party notices

This mod distributes the following third-party components.

## SCSKiller 1.4.7

The `prewarm` folder contains a modified build of SCSKiller's command-line tool and SCSKiller's native D3D12 warm
helpers.

- Project: https://github.com/BlueHeisenberg/SCSKiller
- Source for this build: https://github.com/Internetperson-dev/p3r-shader-stutter-fix (the `DLL-SCSKiller` folder)
- Licence: GPL-3.0-or-later, with SCSKiller's additional permission for Oodle, the Windows App SDK and graphics driver
  libraries. The licence texts are bundled as `prewarm/SCSKILLER-LICENSE.txt` and
  `prewarm/SCSKILLER-LICENSE-EXCEPTION.txt`.

The modified CLI adds a `prewarm` command and a `SCSKILLER_PRELAUNCH` guard mode. Its source is part of this
repository, so the corresponding source is available from the same place as the binary.

## Stuttering Fix DX12 Async Compile

The `Inject/d3d12.dll` file is Ali_Remi's Persona 3 Reload async-compilation shim, redistributed unmodified for
non-commercial use.

- Project: https://gamebanana.com/mods/687363
- Author: Ali_Remi (Shaderless)
- Licence: Creative Commons Attribution-NonCommercial-NoDerivatives 4.0 International
  (https://creativecommons.org/licenses/by-nc-nd/4.0/)

## Reloaded-II

The mod is loaded by Reloaded-II and uses Reloaded.Hooks. Those components are provided by the Reloaded-II
installation and are not included here.

- Project: https://reloaded-project.github.io/Reloaded-II/
