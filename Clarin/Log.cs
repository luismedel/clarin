namespace Clarin
{
    static class Log
    {
        public static void Trace(string text) => System.Diagnostics.Trace.WriteLine("[TRACE]" + text);
        public static void Info (string text) => System.Console.WriteLine (text);
        public static void Warn (string text) => System.Console.Error.WriteLine ("[WARN] " + text);
        public static void Error (string text) => System.Console.Error.WriteLine ("[ERROR] " + text);
    }
}