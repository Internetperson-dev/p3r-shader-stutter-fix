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
/// Your mod logic goes here.
/// </summary>
public partial class Mod : ModBase // <= Do not Remove.
{
    /// <summary>
    /// Provides access to the mod loader API.
    /// </summary>
    private readonly IModLoader _modLoader;

    /// <summary>
    /// Provides access to the Reloaded.Hooks API.
    /// </summary>
    /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
    private readonly IReloadedHooks? _hooks;

    /// <summary>
    /// Provides access to the Reloaded logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Entry point into the mod, instance that created this class.
    /// </summary>
    private readonly IMod _owner;

    /// <summary>
    /// Provides access to this mod's configuration.
    /// </summary>
    private Config _configuration;

    /// <summary>
    /// The configuration of the currently executing mod.
    /// </summary>
    private readonly IModConfig _modConfig;



    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

        // Load different native libraries depending on the selected options.
        if (_configuration.AsyncShaders)
        {
            var modDir = _modLoader.GetDirectoryForModId(context.ModConfig.ModId);
            var gameDir = Path.GetDirectoryName(_modLoader.GetAppConfig().AppLocation) ?? string.Empty;

            foreach (var fileName in new[] { "d3d12.dll", "d3d12.pdb" })
            {
                var source = Path.Combine(modDir, fileName);
                var destination = Path.Combine(gameDir, fileName);

                // Deploy from mod files into the game directory without moving the source.
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
        // Apply settings from configuration.
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying (AsyncShaders={_configuration.AsyncShaders}, ShaderPrecompilation={_configuration.ShaderPrecompilation})");
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}
