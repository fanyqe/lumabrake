using System.Diagnostics;

namespace LumaBrake.Cli.Tests;

internal sealed class CliTestRunner
{
    private static readonly Lazy<string> DotnetHost = new(ResolveDotnetHostPath);

    private readonly string _repositoryRoot = FindRepositoryRoot();
    private readonly string _settingsRoot;

    public CliTestRunner(string settingsRoot)
    {
        _settingsRoot = settingsRoot;
    }

    public CliResult Run(params string[] arguments)
    {
        var cliProjectPath = Path.Combine(_repositoryRoot, "src", "LumaBrake.Cli", "LumaBrake.Cli.csproj");
        var dotnetHostPath = DotnetHost.Value;
        var startInfo = new ProcessStartInfo
        {
            FileName = dotnetHostPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = _repositoryRoot
        };

        if (Path.IsPathFullyQualified(dotnetHostPath))
        {
            startInfo.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(dotnetHostPath);
        }

        startInfo.Environment["LUMABRAKE_SETTINGS_ROOT"] = _settingsRoot;
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(cliProjectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(TestConfiguration());
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("CLI process could not be started.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new CliResult(process.ExitCode, standardOutput, standardError);
    }

    public static void AssertSuccess(CliResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"exit={result.ExitCode}{Environment.NewLine}stdout={result.StandardOutput}{Environment.NewLine}stderr={result.StandardError}");
    }

    private static string ResolveDotnetHostPath()
    {
        foreach (var candidate in DotnetHostCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "dotnet";
    }

    private static IEnumerable<string?> DotnetHostCandidates()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dotnet",
            "dotnet.exe");

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return Path.Combine(dotnetRoot, "dotnet.exe");
        }

        yield return Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            yield return processPath;
        }
    }

    private static string TestConfiguration()
    {
        var pathSegments = AppContext.BaseDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathSegments.Contains("Release", StringComparer.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LumaBrake.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root could not be found.");
    }
}

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
