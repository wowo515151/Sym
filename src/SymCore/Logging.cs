// Copyright Warren Harding 2026
namespace SymCore;

public static class Logging
{
    public static bool LoggingErrorsIsOn { get; set; } = false;

    private const string ErrorsDirectory = @"C:\Users\wowod\Desktop\Code2025\SymAI.net\Errors";

    public static void LogError(string detectorName, string message, string? details = null)
    {
        if (!LoggingErrorsIsOn) return;

        try
        {
            if (!Directory.Exists(ErrorsDirectory))
            {
                Directory.CreateDirectory(ErrorsDirectory);
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"{detectorName}_{timestamp}.txt";
            var filePath = Path.Combine(ErrorsDirectory, fileName);

            var content = $"Detector: {detectorName}\n";
            content += $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n";
            content += $"Message: {message}\n";
            if (!string.IsNullOrEmpty(details))
            {
                content += $"\nDetails:\n{details}\n";
            }

            File.WriteAllText(filePath, content);
        }
        catch
        {
            // Fail silently to avoid crashing during error logging
        }
    }
}
