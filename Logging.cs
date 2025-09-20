using System;
using System.IO;
using System.Text;

namespace YouTubeDownloader;

public static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YouTubeDownloader");
            EnsureDirectory(root);
            return root;
        }
    }

    public static string LogsDirectory
    {
        get
        {
            var dir = Path.Combine(AppDataDirectory, "logs");
            EnsureDirectory(dir);
            return dir;
        }
    }

    public static string LogFilePath => Path.Combine(LogsDirectory, "errors.log");

    private static void EnsureDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch
        {
            // Ignore path creation failures - logging will surface elsewhere if needed.
        }
    }
}

public static class AppLogger
{
    private static readonly object Sync = new();

    public static string LogDirectory => AppPaths.LogsDirectory;
    public static string LogFile => AppPaths.LogFilePath;

    public static void LogInfo(string message) => Write("INFO", message);
    public static void LogWarning(string message) => Write("WARN", message);
    public static void LogError(string message, Exception? ex = null) => Write("ERROR", BuildMessage(message, ex));

    public static void LogProcessFailure(string context, ProcessExecutionException ex)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine(context.Trim());
        }

        builder.AppendLine($"Command: \"{ex.FileName}\" {ex.Arguments}");
        builder.AppendLine($"Exit code: {ex.ExitCode}");

        if (!string.IsNullOrWhiteSpace(ex.StdError))
        {
            builder.AppendLine("stderr:");
            builder.AppendLine(ex.StdError.Trim());
        }

        if (!string.IsNullOrWhiteSpace(ex.StdOutput))
        {
            builder.AppendLine("stdout:");
            builder.AppendLine(ex.StdOutput.Trim());
        }

        Write("ERROR", BuildMessage(builder.ToString(), ex));
    }

    private static string BuildMessage(string message, Exception? ex)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(message))
        {
            sb.AppendLine(message.Trim());
        }

        if (ex != null)
        {
            sb.AppendLine(ex.ToString());
        }

        return sb.ToString().TrimEnd();
    }

    private static void Write(string level, string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var normalized = (message ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length == 0)
            {
                lines = new[] { string.Empty };
            }

            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                using var writer = new StreamWriter(LogFile, append: true);
                for (var i = 0; i < lines.Length; i++)
                {
                    var prefix = i == 0 ? $"{timestamp} [{level}] " : "    ";
                    writer.WriteLine(prefix + lines[i]);
                }
            }
        }
        catch
        {
            // Never throw from logging.
        }
    }
}

public sealed class ProcessExecutionException : Exception
{
    public ProcessExecutionException(string message, string fileName, string arguments, int exitCode, string stdOutput, string stdError)
        : base(message)
    {
        FileName = fileName;
        Arguments = arguments;
        ExitCode = exitCode;
        StdOutput = stdOutput;
        StdError = stdError;
    }

    public string FileName { get; }
    public string Arguments { get; }
    public int ExitCode { get; }
    public string StdOutput { get; }
    public string StdError { get; }
}
