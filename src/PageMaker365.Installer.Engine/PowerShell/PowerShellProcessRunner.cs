using System.Diagnostics;
using System.Text;

namespace PageMaker365.Installer.Engine.PowerShell;

public sealed class PowerShellProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan TerminationWaitTimeout = TimeSpan.FromSeconds(5);

    public async Task<PowerShellExecutionResult> RunAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero && effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "PowerShell timeout must be greater than zero.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new PowerShellExecutionResult
            {
                ExitCode = -2,
                StandardError = "PowerShell command was canceled before it started.",
                Canceled = true,
                FailureReason = "PowerShell command was canceled before it started."
            };
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var outputLock = new object();
        var errorLock = new object();
        process.OutputDataReceived += (_, args) => AppendLine(standardOutput, outputLock, args.Data);
        process.ErrorDataReceived += (_, args) => AppendLine(standardError, errorLock, args.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start PowerShell.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCancellation = CreateTimeoutCancellationTokenSource(effectiveTimeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        var stopReason = StopReason.Completed;
        var terminationMessage = "";
        var exitedAfterTermination = true;

        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || timeoutCancellation.IsCancellationRequested)
        {
            if (HasExited(process))
            {
                stopReason = StopReason.Completed;
            }
            else
            {
                stopReason = cancellationToken.IsCancellationRequested
                    ? StopReason.Canceled
                    : StopReason.TimedOut;
                terminationMessage = TryTerminate(process);
                exitedAfterTermination = process.WaitForExit((int)TerminationWaitTimeout.TotalMilliseconds);
            }
        }

        if (HasExited(process))
        {
            process.WaitForExit();
        }

        var output = ReadCapturedOutput(standardOutput, outputLock);
        var error = ReadCapturedOutput(standardError, errorLock);

        if (stopReason != StopReason.Completed)
        {
            return CreateStoppedResult(
                stopReason,
                output,
                error,
                effectiveTimeout,
                terminationMessage,
                exitedAfterTermination);
        }

        return new PowerShellExecutionResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = output,
            StandardError = error
        };
    }

    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(TimeSpan timeout)
    {
        return timeout == Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource()
            : new CancellationTokenSource(timeout);
    }

    private static void AppendLine(StringBuilder builder, object syncLock, string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (syncLock)
        {
            builder.AppendLine(value);
        }
    }

    private static string ReadCapturedOutput(StringBuilder builder, object syncLock)
    {
        lock (syncLock)
        {
            return builder.ToString();
        }
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static string TryTerminate(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            return "";
        }
        catch (InvalidOperationException)
        {
            return "";
        }
        catch (Exception treeException)
        {
            try
            {
                if (!HasExited(process))
                {
                    process.Kill();
                    return "PowerShell process tree termination was unavailable; terminated the root process instead.";
                }
            }
            catch (Exception processException)
            {
                return $"PowerShell process termination failed: {processException.Message}";
            }

            return $"PowerShell process tree termination was unavailable: {treeException.Message}";
        }
    }

    private static PowerShellExecutionResult CreateStoppedResult(
        StopReason stopReason,
        string standardOutput,
        string standardError,
        TimeSpan timeout,
        string terminationMessage,
        bool exitedAfterTermination)
    {
        var failureReason = stopReason == StopReason.TimedOut
            ? $"PowerShell command timed out after {timeout} and was terminated."
            : "PowerShell command was canceled and was terminated.";

        if (!string.IsNullOrWhiteSpace(terminationMessage))
        {
            failureReason = $"{failureReason} {terminationMessage}";
        }

        if (!exitedAfterTermination)
        {
            failureReason = $"{failureReason} The process did not exit within {TerminationWaitTimeout} after termination was requested.";
        }

        return new PowerShellExecutionResult
        {
            ExitCode = stopReason == StopReason.TimedOut ? -1 : -2,
            StandardOutput = standardOutput,
            StandardError = string.IsNullOrWhiteSpace(standardError)
                ? failureReason
                : $"{failureReason}{Environment.NewLine}{standardError}",
            TimedOut = stopReason == StopReason.TimedOut,
            Canceled = stopReason == StopReason.Canceled,
            FailureReason = failureReason
        };
    }

    private enum StopReason
    {
        Completed,
        TimedOut,
        Canceled
    }
}
