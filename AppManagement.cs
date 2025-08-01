using Serilog;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;
using System.Text.Json;
using System.Text;

public enum BuildType
{
    Production,
    Beta,
    DoNotDistribute
}

public static class LaunchOptions
{
    public static void Initialize(string[] args)
    {

    }
}

public static class AppManagement
{
    private static ReaderWriterLockSlim registeryLock = new ReaderWriterLockSlim();
    private static Dictionary<string, string> registry = [];

    public static CancellationTokenSource RunApp { get; } = new CancellationTokenSource();

    public static BuildType BuildType => BuildType.Production;

    public static bool IsFirstRun { get; set; }

    public static bool IsActivated { get; set; }
    public static bool IsPortableInstall { get; set; }

    public static DateTime? ActivatedAtUtc { get; set; }

    public static string DataDirectory
    {
        get
        {
            if (IsPortableInstall)
            {
                return Path.Combine(AppContext.BaseDirectory, "config_and_data");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        ".tractussdlmon");
            }
            else
            {
                return Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Tractus.Ndi.SdlMonitor");

            }
        }
    }

    public static void DeleteFileFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.Delete(path);
    }

    public static bool DirectoryExistsInDataDirectory(string name)
    {
        return Directory.Exists(Path.Combine(DataDirectory, name));
    }
    public static bool FileExistsInDataDirectory(string fileName)
    {
        return File.Exists(Path.Combine(DataDirectory, fileName));
    }

    public static string GetFullyQualifiedPathOfFileInDataDirectory(string fileName)
    {
        return Path.Combine(DataDirectory, fileName);
    }

    public static string[] ReadFileFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        return File.ReadAllLines(path);
    }

    public static string ReadTextFromDataDirectory(string fileName)
    {
        var path = Path.Combine(DataDirectory, fileName);

        return File.ReadAllText(path);
    }

    public static void WriteFileToDataDirectory(string fileName, string[] lines)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.WriteAllLines(path, lines);
    }

    public static void WriteFileToDataDirectory(string fileName, string content)
    {
        var path = Path.Combine(DataDirectory, fileName);

        File.WriteAllText(path, content);
    }

    public static void EnsureDirectoryExists(string name)
    {
        var path = Path.Combine(DataDirectory, name);
        Directory.CreateDirectory(path);
    }

    public static Stream OpenStreamForWriting(string combine)
    {
        var filePath = GetFullyQualifiedPathOfFileInDataDirectory(combine);

        if (!File.Exists(filePath))
        {
            return File.Create(filePath);
        }

        return File.Open(filePath, FileMode.OpenOrCreate);
    }

    public static string AppName => Assembly.GetEntryAssembly().GetName().Name;

    public static string Version
    {
        get
        {
            var version = Assembly.GetEntryAssembly().GetName().Version;

            return version.ToString();
        }
    }

    public static string InstanceName
    {
        get
        {
            var machineName = Environment.MachineName;

            var osPlatform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "macos"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "linux"
                : "other";

            var bitness = RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "x86_64"
                : RuntimeInformation.ProcessArchitecture == Architecture.X86
                ? "x86_32"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "arm64"
                : RuntimeInformation.ProcessArchitecture == Architecture.Arm
                ? "arm"
                : RuntimeInformation.ProcessArchitecture.ToString();

            return $"{osPlatform}_{bitness}_{machineName}";
        }
    }

    public static void SaveConfigurationKey(string key, string value)
    {
        registeryLock.EnterWriteLock();
        registry[key] = value;
        WriteFileToDataDirectory("registry.json", JsonSerializer.Serialize(registry));
        registeryLock.ExitWriteLock();
    }

    public static string? GetConfigurationKey(string key)
    {
        registeryLock.EnterReadLock();
        try
        {
            if(!registry.TryGetValue(key, out var value))
            {
                return null;
            }

            return value;
        }
        finally
        {
            registeryLock.ExitReadLock();
        }
    }

    public static void Initialize(string[] args)
    {
        if (File.Exists("PORTABLE") || File.Exists("portable.txt"))
        {
            IsPortableInstall = true;
        }

        if (!Directory.Exists(AppManagement.DataDirectory))
        {
            IsFirstRun = true;
            Directory.CreateDirectory(DataDirectory);
        }

        var currentDomain = AppDomain.CurrentDomain;
        currentDomain.UnhandledException += OnAppDomainUnhandledException;

        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel
            .Information();

        // TODO: We should move these to LaunchOptions.
        var isDebugMode = args.Any(x => x.Equals("--debug"));

        if (isDebugMode)
        {
            loggerConfig = loggerConfig.MinimumLevel.Debug();
        }

        var quietMode = args.Any(x => x.Equals("--quiet"));

        if (!quietMode)
        {
            loggerConfig = loggerConfig.WriteTo.Console();
        }

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        loggerConfig = loggerConfig.WriteTo.File(System.IO.Path.Combine(
                documentsPath,
                $"{AppName}_log.txt"), rollingInterval: RollingInterval.Day);

        if (FileExistsInDataDirectory("registry.json"))
        {
            var json = ReadTextFromDataDirectory("registry.json");
            registry = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }


        Log.Logger = loggerConfig.CreateLogger();

        Log.Information($"{AppName} starting up.");
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = (Exception)e.ExceptionObject;

        Log.Error("Unhandled exception in appdomain: {@ex}", exception);
        if (e.IsTerminating)
        {
            Log.Error("Runtime is terminating. Fatal exception.");
        }
        else
        {
            Log.Error("Runtime is not terminating.");
        }
    }
}
