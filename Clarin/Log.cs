namespace Clarin
{
    static class Log
    {
        public static void Indent () => System.Diagnostics.Trace.Indent ();
        public static void Unindent () => System.Diagnostics.Trace.Unindent ();
        public static void Info (string text) => System.Diagnostics.Trace.WriteLine (text);
        public static void Warn (string text) => System.Diagnostics.Trace.WriteLine ("[WARN] " + text);
        public static void Error (string text) => System.Diagnostics.Trace.WriteLine ("[ERROR] " + text);
    }
}