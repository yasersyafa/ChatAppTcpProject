using System;
using System.IO;
using System.Threading.Tasks;

public static class LoggingService
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly string LogFileName = $"server_{DateTime.Now:yyyyMMdd}.log";
        private static readonly object LockObject = new object();

        static LoggingService()
        {
            // Ensure log directory exists
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }
        }

        public static void LogInfo(string message)
        {
            LogMessage("INFO", message);
        }

        public static void LogWarning(string message)
        {
            LogMessage("WARN", message);
        }

        public static void LogError(string message)
        {
            LogMessage("ERROR", message);
        }

        public static void LogError(string message, Exception ex)
        {
            LogMessage("ERROR", $"{message} - Exception: {ex.Message}\nStack Trace: {ex.StackTrace}");
        }

        public static void LogConnection(string clientInfo, string action)
        {
            LogMessage("CONN", $"{action}: {clientInfo}");
        }

        public static void LogMessage(string level, string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logEntry = $"[{timestamp}] [{level}] {message}";

            // Console output
            Console.WriteLine(logEntry);

            // File output
            lock (LockObject)
            {
                try
                {
                    var logPath = Path.Combine(LogDirectory, LogFileName);
                    File.AppendAllText(logPath, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] Failed to write to log file: {ex.Message}");
                }
            }
        }

        public static async Task CleanupOldLogsAsync(int daysToKeep = 7)
        {
            try
            {
                await Task.Run(() =>
                {
                    var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                    var logFiles = Directory.GetFiles(LogDirectory, "server_*.log");

                    foreach (var logFile in logFiles)
                    {
                        var fileInfo = new FileInfo(logFile);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            File.Delete(logFile);
                            LogInfo($"Deleted old log file: {Path.GetFileName(logFile)}");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogError("Failed to cleanup old log files", ex);
            }
        }
    }
