namespace WorkingService;

// Writes messages to a text file. One file per day: Log_20261015.txt
public static class LogWriter
{
    private static readonly object _lock = new object();
    private static string _folder = string.Empty;

    public static void Init(string folder)
    {
        _folder = folder;
    }

    private static string CreateLogFile()
    {
        Directory.CreateDirectory(_folder);   // does nothing if it already exists
        return Path.Combine(_folder, "Log_" + DateTime.Now.ToString("yyyyMMdd") + ".txt");
    }

    public static void LogInfo(string pContent)
    {
        try
        {
            lock (_lock)   // only one thread writes at a time
            {
                string sFileName = CreateLogFile();
                File.AppendAllText(sFileName, Environment.NewLine);
                File.AppendAllText(sFileName, DateTime.Now.ToString("G") + Environment.NewLine);
                File.AppendAllText(sFileName, "------------------------" + Environment.NewLine);
                File.AppendAllText(sFileName, pContent + Environment.NewLine);
            }
        }
        catch
        {
            // a logging problem should never stop the service
        }
    }

    public static void LogError(string pContent, Exception? ex = null)
    {
        LogInfo("ERROR: " + pContent + (ex != null ? Environment.NewLine + ex : ""));
    }
}