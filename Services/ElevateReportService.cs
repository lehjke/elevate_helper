using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ElevateHelperWinUI.Models;

namespace ElevateHelperWinUI.Services;

public sealed class ElevateReportService : IElevateReportService
{
    public async Task<ProcessingResult> PrintReportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ProcessingResult.Fail("Path is empty.");
        }

        if (!Directory.Exists(path))
        {
            return ProcessingResult.Fail($"Path does not exist: {path}");
        }

        string batchResultsPath = Path.Combine(path, "batch_results.csv");
        if (!File.Exists(batchResultsPath))
        {
            return ProcessingResult.Fail($"batch_results.csv not found: {batchResultsPath}");
        }

        string? repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return ProcessingResult.Fail("Cannot find repository root containing body.py and report_lib.py.");
        }

        if (!TryGetPythonCommand(repositoryRoot, out PythonCommand? pythonCommand))
        {
            return ProcessingResult.Fail("Python interpreter not found. Expected .venv\\Scripts\\python.exe or python in PATH.");
        }

        string escapedRoot = EscapePythonLiteral(repositoryRoot);
        string escapedPath = EscapePythonLiteral(path);
        string script = $"""
            import sys
            sys.path.insert(0, r'{escapedRoot}')
            import body
            body.print_report(r'{escapedPath}')
            """;

        ProcessStartInfo startInfo = new()
        {
            FileName = pythonCommand.FileName,
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (string arg in pythonCommand.PrefixArguments)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(script);

        try
        {
            using Process process = new()
            {
                StartInfo = startInfo,
            };

            _ = process.Start();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                string message = BuildProcessErrorMessage(process.ExitCode, stdout, stderr);
                return ProcessingResult.Fail(message);
            }

            return ProcessingResult.Ok("Report generated.");
        }
        catch (Exception ex)
        {
            return ProcessingResult.Fail("An exception occurred while running Python report generation.", ex);
        }
    }

    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string bodyPath = Path.Combine(current.FullName, "body.py");
            string reportLibPath = Path.Combine(current.FullName, "report_lib.py");
            if (File.Exists(bodyPath) && File.Exists(reportLibPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool TryGetPythonCommand(
        string repositoryRoot,
        [NotNullWhen(true)] out PythonCommand? command)
    {
        string venvPython = Path.Combine(repositoryRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
        {
            command = new PythonCommand(venvPython, []);
            return true;
        }

        string? fromPath = FindExecutableInPath("python.exe");
        if (!string.IsNullOrWhiteSpace(fromPath))
        {
            command = new PythonCommand(fromPath, []);
            return true;
        }

        string? pyLauncher = FindExecutableInPath("py.exe");
        if (!string.IsNullOrWhiteSpace(pyLauncher))
        {
            command = new PythonCommand(pyLauncher, ["-3"]);
            return true;
        }

        command = null;
        return false;
    }

    private static string? FindExecutableInPath(string executableName)
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathVariable))
        {
            return null;
        }

        foreach (string rawPart in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = rawPart.Trim();
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            string candidate = Path.Combine(part, executableName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string EscapePythonLiteral(string input)
    {
        StringBuilder builder = new(input.Length);
        foreach (char character in input)
        {
            _ = character switch
            {
                '\\' => builder.Append(@"\\"),
                '\'' => builder.Append(@"\'"),
                _ => builder.Append(character),
            };
        }

        return builder.ToString();
    }

    private static string BuildProcessErrorMessage(int exitCode, string stdout, string stderr)
    {
        StringBuilder builder = new();
        _ = builder.Append("Python report command failed with exit code ").Append(exitCode).Append('.');

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            _ = builder.Append(" stdout: ").Append(stdout.Trim());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _ = builder.Append(" stderr: ").Append(stderr.Trim());
        }

        return builder.ToString();
    }

    private sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArguments);
}
