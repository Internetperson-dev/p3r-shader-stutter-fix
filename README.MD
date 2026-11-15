# Persona 3 Reload Shader Preload (Reloaded-II)

A [Reloaded-II](https://reloaded-project.github.io/Reloaded-II/) mod that reduces Persona 3 Reload's shader-compilation
stutter. It has two modes:

- **Precompile** (default): compiles the game's shaders into your GPU driver's shader cache before the game creates its
  first Direct3D 12 device. The game waits for the compile, then starts with the shaders already warm.
- **Async**: installs the *Stuttering Fix DX12 Async Compile* shim (`d3d12.dll`) into the game folder, which compiles
  pipelines asynchronously while the game runs.

The precompile is powered by [SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) v1.4.7. The mod bundles a
modified SCSKiller CLI (with a `prewarm` command) and SCSKiller's native D3D12 warm shims, so nothing needs to be
installed separately.

## Install

1. Install Reloaded-II and add Persona 3 Reload (`P3R.exe`) as an application.
2. Download the latest release zip and extract it so that `ModConfig.json` sits in
   `Reloaded-II/Mods/P3R.ShaderPreload/`.
3. Enable **Persona 3 Reload Shader Preload** for Persona 3 Reload in the Reloaded launcher.
4. Start the game through Reloaded-II.

The mod works with Steam, Game Pass and other installs. It finds the running game by its executable path; no manual
game setup is required.

## First launch (Precompile)

On the first launch the mod shows an SCSKiller console while it compiles shaders. **The game is held at its first
Direct3D 12 device creation until the compile finishes**, so the splash screen or a black window may stay on screen
for a while. This is expected.

- A cold compile can take from several minutes to tens of minutes, depending on the CPU, GPU and driver.
- Do not close the console unless you want to skip the rest of the compile; the game then starts with only the shaders
  compiled so far.
- Compiled pipelines go into your GPU driver's shader cache. SCSKiller's own plans and recordings live in
  `%LOCALAPPDATA%\SCSKiller\`. P3R's game files are not modified.
- The first compile may download the Oodle codec (a pinned, hash-checked DLL) into `%LOCALAPPDATA%\SCSKiller\codecs`.
  An internet connection is needed once; a failed download is logged and the game still starts.

On later launches the mod runs a hidden check (a few seconds). If the game build or driver changed, the shader cache is
stale and the compile console is shown again. If nothing needs compiling, the game starts immediately after the check.

## Async mode

Select **Async** in the mod's settings and start the game **once more**: the shim takes effect on the next launch,
because `P3R.exe` has already loaded its D3D12 DLL for the current launch.

Async mode avoids blocking the game start, but it can cause visible graphical glitches and does not eliminate every
hitch. It is the original *Persona 3 Reload Stuttering Fix DX12 Async Compile* behaviour, not a precompile.

Switching back to **Precompile** removes the shim the mod installed. The mod tracks the files it placed and never
overwrites or deletes a `d3d12.dll` that belongs to another mod or to the game.

## Settings

Settings are available in the Reloaded launcher (mod Config).

| Setting | Default | What it does |
|---|---|---|
| Shader mode | Precompile | `Precompile` compiles before the game starts; `Async` installs the async shim. |
| Precompile threads | 0 (automatic) | Worker threads for the compile. `0` lets SCSKiller choose from the CPU and GPU. |
| Precompile timeout (minutes) | 0 (no limit) | Stop waiting and let the game start after this long. `0` waits until the compile ends or its console is closed. |
| Show compile console | On | Show the SCSKiller console during a compile. When off, progress goes to the Reloaded log and the mod's log file. |

A mode change applies on the next game start.

## Logs and troubleshooting

- Reloaded's log shows the mod's messages.
- The mod also writes `prewarm\prewarm.log` in the mod folder.
- If precompiling is not possible (for example, the game's files cannot be read), the log says why and the game starts
  normally. You can switch to Async mode as a fallback.
- This mod and the standalone SCSKiller app share `%LOCALAPPDATA%\SCSKiller`, so a game already warmed with the app is
  recognised as warm. If the log says Persona 3 Reload's files are encrypted and SCSKiller cannot find the AES key,
  run the SCSKiller app once for the game (or give it the key with `scskiller key <game> <hex>`); the mod then reuses
  that key on its next launch.
- If no shaders are compiled on the first launch, check that the release was extracted completely (`prewarm\cli\scskiller.exe`
  and `prewarm\native\...` must exist) and that no antivirus quarantined the helper.

## Upgrading from version 1.x

Version 1.x copied helper files into the game folder. The current version keeps everything in the mod folder, so you
can remove these leftovers from the folder that contains `P3R.exe` if they are still there:

`scskiller.ini`, `scskiller_warm.exe`, `scskiller_ags.dll`, and the `d3d12.dll` that version 1.x installed for Async
mode. Do not remove `d3d12.dll` if another mod (for example ReShade) owns it.

## AMD shader-cache notes

AMD's driver manages a bounded shader cache and may evict compiled pipelines, so some shaders can still compile during
play even after a successful precompile. Driver updates also clear the driver cache, which makes the mod recompile on
the next launch. See the [SCSKiller AMD discussion](https://www.reddit.com/r/radeon/comments/1wymc5d/scskiller_might_be_useless_with_some_games/)
for context. NVIDIA users get the segment-heap warm helper automatically; AMD users use the regular one.

## Building from source

The GitHub Actions workflow builds everything on Windows. It downloads the pinned SCSKiller v1.4.7 release
(SHA-256 verified), publishes this repository's modified SCSKiller CLI, stages the CLI and native warm helpers in
`P3R.ShaderPreload\prewarm`, then publishes and zips the Reloaded mod.

For a local build you need the .NET 9 and .NET 10 SDKs on Windows:

```powershell
# 1. Download and extract SCSKiller v1.4.7, then copy its cli\ and native\ files into P3R.ShaderPreload\prewarm
#    (cli\ next to scskiller.exe, native\ as a sibling of cli\).
# 2. Publish the modified CLI.
dotnet publish DLL-SCSKiller/src/SCSKiller.Cli/SCSKiller.Cli.csproj -c Release -p:Version=1.4.7 -o Publish/cli
Copy-Item Publish/cli/* P3R.ShaderPreload/prewarm/cli -Recurse -Force
# 3. Publish the mod.
dotnet publish P3R.ShaderPreload/P3R.ShaderPreload.csproj -c Release --self-contained false -o Publish/mod
```

`P3R.ShaderPreload\prewarm` is generated and is not committed. Without it the mod still builds, and at runtime it
logs that the helper is missing and lets the game start.

## Credits

- [SCSKiller](https://github.com/BlueHeisenberg/SCSKiller) by BlueHeisenberg: shader/pipeline discovery, planning,
  warming and the native D3D12 warm shims. This repository vendors SCSKiller v1.4.7 and adds a `prewarm` command and a
  prelaunch guard mode.
- [Persona 3 Reload Stuttering Fix DX12 Async Compile](https://gamebanana.com/mods/687363) by Ali_Remi (Shaderless):
  the async `d3d12.dll` used in Async mode.
- [Reloaded-II](https://reloaded-project.github.io/Reloaded-II/) and Reloaded.Hooks: mod loader, configuration and the
  `D3D12CreateDevice` hook.

Not affiliated with Atlus or SEGA. Persona 3 Reload is a trademark of its respective owners.

## Licensing

- SCSKiller source and binaries: **GPL-3.0-or-later**, with SCSKiller's additional permission for Oodle, the Windows
  App SDK and graphics driver libraries. See [`DLL-SCSKiller/LICENSE`](DLL-SCSKiller/LICENSE) and
  [`DLL-SCSKiller/LICENSE-EXCEPTION.txt`](DLL-SCSKiller/LICENSE-EXCEPTION.txt). Copies are also bundled next to the
  helper in `prewarm\SCSKILLER-LICENSE*.txt`.
- Async compile shim (`Inject\d3d12.dll`): **CC BY-NC-ND 4.0**, by Ali_Remi.
- The Reloaded-II mod glue in `P3R.ShaderPreload` is provided as-is for use with this project.
