using System.Runtime.InteropServices;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using P3R.ShaderPreload.Template;
using P3R.ShaderPreload.Configuration;
#if DEBUG
using System.Diagnostics;
#endif

namespace P3R.ShaderPreload;

/// <summary>
/// Mod that loads the correct shader DLLs depending on Reloaded-II options.
/// </summary>
public partial class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger;
    private readonly IMod _owner;
    private Config _configuration;
    private readonly IModConfig _modConfig;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

        var modDir = _modLoader.GetDirectoryForModId(context.ModConfig.ModId);
        var gameDir = Path.GetDirectoryName(_modLoader.GetAppConfig().AppLocation) ?? string.Empty;

        // Always deploy scskiller.ini with prelaunch mode if ShaderPrecompilation enabled.
        if (_configuration.ShaderPrecompilation)
        {
            var iniSource = Path.Combine(modDir, "scskiller.ini");
            var iniDest = Path.Combine(gameDir, "scskiller.ini");
            if (!File.Exists(iniSource))
            {
                File.WriteAllText(iniSource, "[scskiller]\nmode=prelaunch\nworkdir=" + gameDir + "\n");
            }
            if (File.Exists(iniSource) && (!File.Exists(iniDest) || File.ReadAllText(iniDest).IndexOf("prelaunch") < 0))
            {
                File.Copy(iniSource, iniDest, overwrite: true);
            }
        }

        // Load/deploy native DLLs based on selected options.
        if (_configuration.AsyncShaders)
        {
            // Deploy proxy d3d12.dll (shader recording / async compilation shim)
            foreach (var fileName in new[] { "d3d12.dll", "d3d12.pdb" })
            {
                var source = Path.Combine(modDir, "Inject", fileName);
                var destination = Path.Combine(gameDir, fileName);
                if (File.Exists(source) && !File.Exists(destination))
                {
                    File.Copy(source, destination, overwrite: false);
                }
            }
        }

        if (_configuration.ShaderPrecompilation)
        {
            // Deploy shader precompile executable (scskiller_warm.exe) and precompile DLL
            foreach (var fileName in new[] { "scskiller_warm.exe", "scskiller_ags.dll", "amd_ags_x64.dll" })
            {
                var source = Path.Combine(modDir, "Inject", fileName);
                // Also fall back to DLL-SCSKiller native output if present
                if (!File.Exists(source))
                {
                    source = Path.Combine(modDir, "..", "DLL-SCSKiller", "proxy", "build", "Release", fileName);
                    if (!File.Exists(source))
                    {
                        source = Path.Combine(modDir, "..", "DLL-SCSKiller", "proxy", fileName);
                    }
                }
                var destination = Path.Combine(gameDir, fileName);
                if (File.Exists(source) && !File.Exists(destination))
                {
                    File.Copy(source, destination, overwrite: false);
                }
            }
        }
    }

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying (AsyncShaders={_configuration.AsyncShaders}, ShaderPrecompilation={_configuration.ShaderPrecompilation})");
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}
