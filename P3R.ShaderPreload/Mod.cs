using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Mod.Interfaces;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;
using P3R.ShaderPreload.Template;
using P3R.ShaderPreload.Configuration;

namespace P3R.ShaderPreload;

/// <summary>
/// Precompiles Persona 3 Reload's shaders before the game creates its first D3D12 device, or deploys the async-compilation
/// shim. The compile itself is SCSKiller's CLI (a separate process): the mod holds the game's first
/// <c>D3D12CreateDevice</c> call until that process has finished, so the game can't compile shaders while it plays.
/// </summary>
public partial class Mod : ModBase
{
    private const string AsyncMarkerName = "async-install.json";
    private const string WarmedMarkerName = ".warmed";

    // D3D12CreateDevice is exported from whichever d3d12.dll the game loaded: a proxy beside its exe, else the system one.
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchSystem32 = 0x00000800;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(45);
    private const int TimeoutExit = -1;

    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger;
    private readonly IMod _owner;
    private Config _configuration;
    private readonly IModConfig _modConfig;

    private readonly string _modDir;
    private readonly string _gameDir;
    private readonly string _gameExe;
    private readonly object _logLock = new();
    private readonly object _processLock = new();

    private ManualResetEventSlim? _prewarmDone;
    private Process? _cliProcess;
    private IHook<D3D12CreateDeviceDelegate>? _d3d12Hook;
    private D3D12CreateDeviceDelegate? _d3d12Detour;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

        _modDir = _modLoader.GetDirectoryForModId(context.ModConfig.ModId);
        _gameExe = _modLoader.GetAppConfig().AppLocation;
        _gameDir = Path.GetDirectoryName(_gameExe) ?? string.Empty;

        Log($"Loaded. Mode: {_configuration.Mode}.");

        try
        {
            if (_configuration.Mode == ShaderMode.Async)
            {
                DeployAsyncShim();
                Log("Async mode: the shim is in the game folder. If it was not already loaded by this launch, restart the game once.");
            }
            else
            {
                RemoveAsyncShim();
                StartPrewarm();
                InstallD3D12Hook();
            }
        }
        catch (Exception e)
        {
            Log($"Setup failed: {e}");
            _prewarmDone?.Set();   // never hold the game behind a failed setup
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => StopPrewarmProcess();
    }

    #region Precompile mode

    private void StartPrewarm()
    {
        _prewarmDone = new ManualResetEventSlim(false);
        var cli = Path.Combine(_modDir, "prewarm", "cli", "scskiller.exe");
        if (!File.Exists(cli))
        {
            Log($"The prewarm helper is missing: {cli}. The game starts without precompiling.");
            _prewarmDone.Set();
            return;
        }

        // Never block the mod constructor: Reloaded's launcher waits only 30 s for mods to initialize.
        _ = Task.Run(async () =>
        {
            try { await RunPrewarmAsync(cli); }
            catch (Exception e) { Log($"Prewarm failed: {e}"); }
            finally { _prewarmDone.Set(); }
        });
    }

    private async Task RunPrewarmAsync(string cli)
    {
        var marker = Path.Combine(_modDir, "prewarm", WarmedMarkerName);
        int code;
        if (!File.Exists(marker))
        {
            // First launch (or the last compile failed): show the console straight away, since the compile can be long.
            Log("No finished compile is recorded yet: starting the precompile.");
            code = await RunCliAsync(cli, check: false, timeout: CompileTimeout());
        }
        else
        {
            Log("Checking whether the shader cache is still current for this game build and driver.");
            code = await RunCliAsync(cli, check: true, timeout: CheckTimeout);
            if (code == TimeoutExit)
            {
                Log("The check is taking long (a new driver or game build): showing the compile console.");
                code = await RunCliAsync(cli, check: false, timeout: CompileTimeout());
            }
            else if (code == 0)
            {
                Log("Shaders are already compiled for this game build and driver.");
                return;
            }
            else if (code == 10)
            {
                Log("Shaders need a compile: starting it.");
                code = await RunCliAsync(cli, check: false, timeout: CompileTimeout());
            }
            else
            {
                Log(code == 11
                    ? "SCSKiller can't compile this game's shaders from its files (unsupported, or it needs a recording first). Letting the game start."
                    : $"The prewarm check failed (exit {code}). Letting the game start; see the log above.");
                return;
            }
        }

        if (code == 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllText(marker, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            Log("Shader precompile finished.");
        }
        else if (code == TimeoutExit)
        {
            Log("The precompile exceeded its timeout, so it was stopped. The game starts with the shaders compiled so far.");
        }
        else if (code == 11)
        {
            Log("SCSKiller can't compile this game's shaders from its files. Letting the game start.");
        }
        else
        {
            Log($"The precompile failed (exit {code}). The game starts without it.");
        }
    }

    private TimeSpan CompileTimeout() => _configuration.PrecompileTimeoutMinutes > 0
        ? TimeSpan.FromMinutes(_configuration.PrecompileTimeoutMinutes)
        : Timeout.InfiniteTimeSpan;

    /// <summary>Runs the bundled CLI, hidden for a check and with a console for a compile (unless the config hides it).</summary>
    private async Task<int> RunCliAsync(string cli, bool check, TimeSpan timeout)
    {
        var hidden = check || !_configuration.ShowCompileConsole;
        var start = new ProcessStartInfo
        {
            FileName = cli,
            WorkingDirectory = Path.GetDirectoryName(cli)!,
            UseShellExecute = false,
            CreateNoWindow = hidden,
            RedirectStandardOutput = hidden,
            RedirectStandardError = hidden,
        };
        start.ArgumentList.Add("prewarm");
        start.ArgumentList.Add(_gameExe);
        if (check) start.ArgumentList.Add("--check");
        else if (_configuration.PrecompileThreads > 0)
        {
            start.ArgumentList.Add("--threads");
            start.ArgumentList.Add(_configuration.PrecompileThreads.ToString(CultureInfo.InvariantCulture));
        }
        // Say why the game's own process must not count as "running", and allow the real %LOCALAPPDATA% data folder
        // (a raw CLI build otherwise refuses it).
        start.Environment["SCSKILLER_PRELAUNCH"] = "1";
        start.Environment["SCSKILLER_ALLOW_LIVE_DATA"] = "1";

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (hidden)
        {
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log("  " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Log("  " + e.Data); };
        }

        lock (_processLock) _cliProcess = process;
        try
        {
            Log(check ? "Running the hidden prewarm check." : "Starting the SCSKiller precompile console.");
            if (!process.Start()) return 1;
            if (hidden)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            using var cts = timeout == Timeout.InfiniteTimeSpan ? new CancellationTokenSource() : new CancellationTokenSource(timeout);
            try
            {
                // Poll for the exit instead of WaitForExitAsync: that also waits for the redirected pipes to close, which a
                // grandchild (the warm helper's own child) can keep open after the CLI itself has finished.
                while (!process.HasExited) await Task.Delay(200, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Log($"Stopping the SCSKiller process after {(timeout == Timeout.InfiniteTimeSpan ? "the timeout" : $"{timeout.TotalMinutes:0.#} min")}.");
                KillTree(process);
                for (var waited = 0; waited < 10_000 && !process.HasExited; waited += 200) await Task.Delay(200);
                return TimeoutExit;
            }

            return process.ExitCode;
        }
        finally
        {
            lock (_processLock) _cliProcess = null;
        }
    }

    private static void KillTree(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception) { /* already gone */ }
    }

    private void StopPrewarmProcess()
    {
        Process? process;
        lock (_processLock) process = _cliProcess;
        if (process == null) return;
        KillTree(process);
    }

    private void InstallD3D12Hook()
    {
        if (_hooks == null)
        {
            Log("Reloaded.Hooks is unavailable, so the game can't be held for the precompile. It will start without the wait.");
            _prewarmDone?.Set();
            return;
        }

        var proxy = Path.Combine(_gameDir, "d3d12.dll");
        IntPtr module;
        string source;
        if (File.Exists(proxy))
        {
            // A proxy beside the exe (another mod's, or our async shim from an earlier launch): hook the export the game uses.
            module = LoadLibraryEx(proxy, IntPtr.Zero, LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
            source = proxy;
        }
        else
        {
            // Load the system DLL ourselves, so the game's later import resolution reuses it. Loading it by name without the
            // system32 flag could pick a proxy that appeared after this check.
            module = LoadLibraryEx("d3d12.dll", IntPtr.Zero, LoadLibrarySearchSystem32);
            source = @"system32\d3d12.dll";
        }

        if (module == IntPtr.Zero)
        {
            Log($"Couldn't load {source} (error {Marshal.GetLastWin32Error()}). The game starts without the precompile wait.");
            _prewarmDone?.Set();
            return;
        }

        var address = GetProcAddress(module, "D3D12CreateDevice");
        if (address == IntPtr.Zero)
        {
            Log($"{source} has no D3D12CreateDevice export. The game starts without the precompile wait.");
            _prewarmDone?.Set();
            return;
        }

        _d3d12Detour = D3D12CreateDeviceDetour;
        _d3d12Hook = _hooks.CreateHook<D3D12CreateDeviceDelegate>(_d3d12Detour, address);
        _d3d12Hook.Activate();
        Log($"Holding the game's first D3D12CreateDevice from {source} until the precompile finishes.");
    }

    private int D3D12CreateDeviceDetour(IntPtr adapter, int minimumFeatureLevel, IntPtr riid, IntPtr device)
    {
        var done = _prewarmDone;
        if (done is { IsSet: false })
        {
            Log("The game is creating its D3D12 device: waiting for the shader precompile (its console shows progress).");
            done.Wait();
            Log("The precompile is done; the game can create its device.");
        }

        return _d3d12Hook!.OriginalFunction(adapter, minimumFeatureLevel, riid, device);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int D3D12CreateDeviceDelegate(IntPtr adapter, int minimumFeatureLevel, IntPtr riid, IntPtr device);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);

    #endregion

    #region Async mode

    private sealed class AsyncInstall
    {
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private string AsyncMarker => Path.Combine(_modDir, AsyncMarkerName);

    /// <summary>Copies the shim beside the game exe, but only files this mod placed (tracked by SHA-256 in the marker) are
    /// ever replaced or removed: another mod's d3d12.dll is left alone.</summary>
    private void DeployAsyncShim()
    {
        var marker = LoadAsyncMarker();
        foreach (var name in new[] { "d3d12.dll", "d3d12.pdb" })
        {
            var source = Path.Combine(_modDir, "Inject", name);
            if (!File.Exists(source))
            {
                if (name == "d3d12.dll") throw new FileNotFoundException($"The async shim is missing: {source}", source);
                continue;
            }

            var sourceHash = HashFile(source);
            var destination = Path.Combine(_gameDir, name);
            if (File.Exists(destination))
            {
                var destinationHash = HashFile(destination);
                if (marker.Files.TryGetValue(name, out var placed) && destinationHash == placed)
                {
                    if (destinationHash != sourceHash)
                    {
                        if (TryCopy(source, destination, overwrite: true))
                        {
                            marker.Files[name] = sourceHash;
                            Log($"Updated the async shim: {destination}");
                        }
                    }
                    continue;
                }

                if (marker.Files.Remove(name))
                    Log($"{destination} changed since this mod placed it; leaving it alone.");
                else
                    Log(destinationHash == sourceHash
                        ? $"{destination} is already there (same file); leaving it unmanaged."
                        : $"{destination} belongs to something else; not replacing it.");
                continue;
            }

            if (TryCopy(source, destination, overwrite: false))
            {
                marker.Files[name] = sourceHash;
                Log($"Installed the async shim: {destination}");
            }
        }

        SaveAsyncMarker(marker);
    }

    /// <summary>Removes only the shim files this mod placed and that are still unchanged. A loaded shim stays until the next
    /// launch; its marker entry stays so that launch tries again.</summary>
    private void RemoveAsyncShim()
    {
        var marker = LoadAsyncMarker();
        if (marker.Files.Count == 0) return;

        var left = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expected) in marker.Files.ToList())
        {
            var destination = Path.Combine(_gameDir, name);
            if (!File.Exists(destination)) continue;
            try
            {
                if (HashFile(destination) != expected)
                {
                    Log($"{destination} changed since this mod placed it; leaving it alone.");
                    continue;
                }
                File.Delete(destination);
                Log($"Removed the async shim: {destination}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                left[name] = expected;
                Log($"Couldn't remove {destination} yet ({e.Message}); it is removed on the next launch.");
            }
        }

        marker.Files = left;
        SaveAsyncMarker(marker);
    }

    private AsyncInstall LoadAsyncMarker()
    {
        try
        {
            if (!File.Exists(AsyncMarker)) return new AsyncInstall();
            return JsonSerializer.Deserialize<AsyncInstall>(File.ReadAllText(AsyncMarker)) ?? new AsyncInstall();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            Log($"Couldn't read {AsyncMarker}: {e.Message}");
            return new AsyncInstall();
        }
    }

    private void SaveAsyncMarker(AsyncInstall marker)
    {
        try
        {
            if (marker.Files.Count == 0)
            {
                if (File.Exists(AsyncMarker)) File.Delete(AsyncMarker);
                return;
            }
            File.WriteAllText(AsyncMarker, JsonSerializer.Serialize(marker, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log($"Couldn't write {AsyncMarker}: {e.Message}");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>Copies the shim, but a destination locked by this launch (the game already loaded it) only waits for the
    /// next one: it must not fail the mod's setup.</summary>
    private bool TryCopy(string source, string destination, bool overwrite)
    {
        try
        {
            File.Copy(source, destination, overwrite);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log($"Couldn't copy {source} to {destination} yet ({e.Message}); it is retried on the next launch.");
            return false;
        }
    }

    #endregion

    #region Logging

    private void Log(string message)
    {
        _logger.WriteLine($"[{_modConfig.ModId}] {message}");
        try
        {
            var folder = Path.Combine(_modDir, "prewarm");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "prewarm.log");
            lock (_logLock)
            {
                if (File.Exists(file) && new FileInfo(file).Length > 2 * 1024 * 1024)
                    File.Move(file, file + ".old", overwrite: true);
                File.AppendAllText(file, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception)
        {
            // The Reloaded log above is enough; never fail a launch over the mod's own log file.
        }
    }

    #endregion

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        var previous = _configuration.Mode;
        _configuration = configuration;
        Log($"Config updated: mode {previous} -> {configuration.Mode}. A mode change applies on the next game start.");

        try
        {
            if (configuration.Mode == ShaderMode.Async) DeployAsyncShim();
            else RemoveAsyncShim();
        }
        catch (Exception e)
        {
            Log($"Applying the new mode's files failed: {e}");
        }
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}
