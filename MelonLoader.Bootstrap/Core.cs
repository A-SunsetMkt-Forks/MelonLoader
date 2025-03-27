using System.Diagnostics;
using MelonLoader.Logging;
using MelonLoader.Bootstrap.RuntimeHandlers.Il2Cpp;
using MelonLoader.Bootstrap.RuntimeHandlers.Mono;
using MelonLoader.Bootstrap.Utils;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using MelonLoader.Bootstrap.Logging;
using Tomlet;

namespace MelonLoader.Bootstrap;

public static class Core
{
#if LINUX || OSX
    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    private delegate nint DlsymFn(nint handle, string symbol);
    private static readonly DlsymFn HookDlsymDelegate = HookDlsym;
#endif
#if WINDOWS
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate nint GetProcAddressFn(nint handle, string symbol);
    private static readonly GetProcAddressFn HookGetProcAddressDelegate = HookGetProcAddress;
#endif

    public static nint LibraryHandle { get; private set; }

    internal static InternalLogger Logger { get; private set; } = new(ColorARGB.BlueViolet, "MelonLoader.Bootstrap");
    internal static InternalLogger PlayerLogger { get; private set; } = new(ColorARGB.Turquoise, "UNITY");
    public static string DataDir { get; private set; } = null!;
    public static string GameDir { get; private set; } = null!;

    private static bool _runtimeInitialised;

    [RequiresDynamicCode("Calls InitConfig")]
    public static void Init(nint moduleHandle)
    {
        LibraryHandle = moduleHandle;

        var exePath = Environment.ProcessPath!;
        GameDir = Path.GetDirectoryName(exePath)!;

#if !OSX
        DataDir = Path.Combine(GameDir, Path.GetFileNameWithoutExtension(exePath) + "_Data");
#else
        DataDir = Path.Combine(Path.GetDirectoryName(GameDir)!, "Resources", "Data");
#endif
        if (!Directory.Exists(DataDir))
            return;

        InitConfig();

        if (LoaderConfig.Current.Loader.Disable)
            return;

        MelonLogger.Init();

#if LINUX || OSX
        PltHook.InstallHooks
        ([
            ("dlsym", Marshal.GetFunctionPointerForDelegate(HookDlsymDelegate))
        ]);
#endif
#if WINDOWS
        PltHook.InstallHooks
        ([
            ("GetProcAddress", Marshal.GetFunctionPointerForDelegate(HookGetProcAddressDelegate))
        ]);
#endif
    }


    private static readonly Il2CppLib.InitFn Il2CPPInitDetour = Il2CppHandler.InitDetour;
    private static readonly Il2CppLib.RuntimeInvokeFn InvokeDetour = Il2CppHandler.InvokeDetour;
    private static readonly MonoLib.JitInitVersionFn MonoInitDetour = MonoHandler.InitDetour;
    private static readonly MonoLib.JitParseOptionsFn JitParseOptionsDetour = MonoHandler.JitParseOptionsDetour;
    private static readonly MonoLib.DebugInitFn DebugInitDetour = MonoHandler.DebugInitDetour;
    private static readonly unsafe MonoLib.ImageOpenFromDataWithNameFn ImageOpenFromDataWithName = MonoHandler.ImageOpenFromDataWithNameDetour;

    private static readonly Dictionary<string, (Action<nint> InitMethod, IntPtr detourPtr)> SymbolRedirects = new()
    {
        { "il2cpp_init", (Il2CppHandler.Initialize, Marshal.GetFunctionPointerForDelegate(Il2CPPInitDetour))},
        { "il2cpp_runtime_invoke", (Il2CppHandler.Initialize, Marshal.GetFunctionPointerForDelegate(InvokeDetour))},
        { "mono_jit_init_version", (MonoHandler.Initialize, Marshal.GetFunctionPointerForDelegate(MonoInitDetour))},
        { "mono_jit_parse_options", (MonoHandler.Initialize, Marshal.GetFunctionPointerForDelegate(JitParseOptionsDetour))},
        { "mono_debug_init", (MonoHandler.Initialize, Marshal.GetFunctionPointerForDelegate(DebugInitDetour))},
        { "mono_image_open_from_data_with_name", (MonoHandler.Initialize, Marshal.GetFunctionPointerForDelegate(ImageOpenFromDataWithName))}
    };

    private static nint RedirectSymbol(nint handle, string symbolName, nint originalSymbolAddress)
    {
        if (!SymbolRedirects.TryGetValue(symbolName, out var redirect))
            return originalSymbolAddress;

        MelonDebug.Log($"Redirecting {symbolName}");
        if (!_runtimeInitialised)
            redirect.InitMethod(handle);
        _runtimeInitialised = true;
        return redirect.detourPtr;
    }

#if LINUX || OSX
    private static nint HookDlsym(nint handle, string symbol)
    {
        nint originalSymbolAddress = LibcNative.Dlsym(handle, symbol);
        return RedirectSymbol(handle, symbol, originalSymbolAddress);
    }
#endif
#if WINDOWS
    private static nint HookGetProcAddress(nint handle, string symbol)
    {
        nint originalSymbolAddress = WindowsNative.GetProcAddress(handle, symbol);
        return RedirectSymbol(handle, symbol, originalSymbolAddress);
    }
#endif

    [RequiresDynamicCode("Dynamically accesses LoaderConfig properties")]
    private static void InitConfig()
    {
        var customBaseDir = ArgParser.GetValue("melonloader.basedir");
        var baseDir = 
#if OSX
            Path.GetDirectoryName(
                Path.GetDirectoryName(
                    Path.GetDirectoryName(
                        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!))!)!;
#else
            Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!;
#endif
        if (Directory.Exists(customBaseDir))
            baseDir = Path.GetFullPath(customBaseDir);

        var path = Path.Combine(baseDir, "UserData", "Loader.cfg");

        if (File.Exists(path))
        {
            try
            {
                var doc = TomlParser.ParseFile(path);

                LoaderConfig.Current = TomletMain.To<LoaderConfig>(doc) ?? new();
            }
            catch { }
        }

        var doc2 = TomletMain.TomlStringFrom(LoaderConfig.Current);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, doc2);
        }
        catch { }

        // Override configs defined by launch options, without overriding the file

        LoaderConfig.Current.Loader.BaseDirectory = baseDir;

        if (ArgParser.IsDefined("melonloader.debug"))
            LoaderConfig.Current.Loader.DebugMode = true;

        if (ArgParser.IsDefined("--melonloader.captureplayerlogs"))
            LoaderConfig.Current.Loader.CapturePlayerLogs = true;

        if (Enum.TryParse<LoaderConfig.CoreConfig.HarmonyLogVerbosity>(ArgParser.GetValue("--melonloader.harmonyloglevel"), out var harmonyLogLevel))
            LoaderConfig.Current.Loader.HarmonyLogLevel = harmonyLogLevel;

        if (ArgParser.IsDefined("no-mods"))
            LoaderConfig.Current.Loader.Disable = true;

        if (ArgParser.IsDefined("quitfix"))
            LoaderConfig.Current.Loader.ForceQuit = true;

        if (ArgParser.IsDefined("melonloader.disablestartscreen"))
            LoaderConfig.Current.Loader.DisableStartScreen = true;

        if (ArgParser.IsDefined("melonloader.launchdebugger"))
            LoaderConfig.Current.Loader.LaunchDebugger = true;

        if (int.TryParse(ArgParser.GetValue("melonloader.consolemode"), out var valueint))
            LoaderConfig.Current.Loader.Theme = (LoaderConfig.CoreConfig.LoaderTheme)Math.Clamp(valueint, (int)LoaderConfig.CoreConfig.LoaderTheme.Normal, (int)LoaderConfig.CoreConfig.LoaderTheme.Lemon);

        if (ArgParser.IsDefined("melonloader.hideconsole"))
            LoaderConfig.Current.Console.Hide = true;

        if (ArgParser.IsDefined("melonloader.consoleontop"))
            LoaderConfig.Current.Console.AlwaysOnTop = true;

        if (ArgParser.IsDefined("melonloader.consoledst"))
            LoaderConfig.Current.Console.DontSetTitle = true;

        if (ArgParser.IsDefined("melonloader.hidewarnings"))
            LoaderConfig.Current.Console.HideWarnings = true;

        if (uint.TryParse(ArgParser.GetValue("melonloader.maxlogs"), out var maxLogs))
            LoaderConfig.Current.Logs.MaxLogs = maxLogs;

        if (ArgParser.IsDefined("melonloader.debugsuspend"))
            LoaderConfig.Current.MonoDebugServer.DebugSuspend = true;

        var debugIpAddress = ArgParser.GetValue("melonloader.debugipaddress");
        if (debugIpAddress != null)
            LoaderConfig.Current.MonoDebugServer.DebugIpAddress = debugIpAddress;

        if (uint.TryParse(ArgParser.GetValue("melonloader.debugport"), out var debugPort))
            LoaderConfig.Current.MonoDebugServer.DebugPort = debugPort;

        var unityVersionOverride = ArgParser.GetValue("melonloader.unityversion");
        if (unityVersionOverride != null)
            LoaderConfig.Current.UnityEngine.VersionOverride = unityVersionOverride;

        if (ArgParser.IsDefined("melonloader.disableunityclc"))
            LoaderConfig.Current.UnityEngine.DisableConsoleLogCleaner = true;

        var monoSearchPathOverride = ArgParser.GetValue("melonloader.monosearchpathoverride");
        if (monoSearchPathOverride != null)
            LoaderConfig.Current.UnityEngine.MonoSearchPathOverride = monoSearchPathOverride;

        if (ArgParser.IsDefined("melonloader.agfregenerate"))
            LoaderConfig.Current.UnityEngine.ForceRegeneration = true;

        if (ArgParser.IsDefined("melonloader.agfoffline"))
            LoaderConfig.Current.UnityEngine.ForceOfflineGeneration = true;

        var forceRegex = ArgParser.GetValue("melonloader.agfregex");
        if (forceRegex != null)
            LoaderConfig.Current.UnityEngine.ForceGeneratorRegex = forceRegex;

        var forceDumperVersion = ArgParser.GetValue("melonloader.agfvdumper");
        if (forceDumperVersion != null)
            LoaderConfig.Current.UnityEngine.ForceIl2CppDumperVersion = forceDumperVersion;

        if (ArgParser.IsDefined("cpp2il.callanalyzer"))
            LoaderConfig.Current.UnityEngine.EnableCpp2ILCallAnalyzer = true;

        if (ArgParser.IsDefined("cpp2il.nativemethoddetector"))
            LoaderConfig.Current.UnityEngine.EnableCpp2ILNativeMethodDetector = true;
    }
}
