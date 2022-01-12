using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace Clarin
{
    static class Log
    {
        public static void Indent () => System.Diagnostics.Debug.Indent ();
        public static void Unindent () => System.Diagnostics.Debug.Unindent ();
        public static void Info (string text) => System.Diagnostics.Debug.WriteLine (text);
        public static void Warn (string text) => System.Diagnostics.Debug.WriteLine ("[WARN] " + text);
        public static void Error (string text) => System.Diagnostics.Debug.WriteLine ("[ERROR] " + text);
    }

    class MetaDict
    {
        public object this[string key]
        {
            private get => _dict.TryGetValue (key, out object v)
                   ? (v is Func<string> fn) ? fn.Invoke () : v.ToString ()
                   : null;

            set => _dict[key] = value;
        }

        public MetaDict ()
        {
            _dict["sys.date"] = new Func<string> (Sys_Date);
        }

        public MetaDict (IDictionary<string, object> contents)
            : this ()
        {
            foreach (var kv in contents)
                _dict[kv.Key] = kv.Value;
        }

        public string Get (string key, string @default="") => (string)this[key] ?? @default;

        public void Merge (MetaDict other, string prefix="")
        {
            foreach (var kv in other._dict)
                _dict[prefix + kv.Key] = kv.Value;
        }

        static string Sys_Date () => DateTime.Now.ToString ("yyyyMMddhhmmss");

        readonly Dictionary<string, object> _dict = new Dictionary<string, object> (StringComparer.InvariantCultureIgnoreCase);
    }

    class FileInfo
    {
        static readonly string[] ContentExt = { ".html", ".htm", ".md", ".xml" };
        
        public Site Site { get; private set; }
        public string Path { get; private set; }
        public MetaDict Meta => _meta;

        public string OutputPath
        {
            get
            {
                var relPath = Site.MakeRelative (Path, Site.ContentPath);
                var destPath = System.IO.Path.GetDirectoryName (System.IO.Path.Combine (Site.OutputPath, relPath));

                return IsContent ? System.IO.Path.Combine (destPath, Meta.Get ("slug") + ".html")
                                 : System.IO.Path.Combine (Site.OutputPath, relPath);
            }
        }

        public string Url => Site.Url + Site.MakeRelative (OutputPath, Site.OutputPath);
        public bool IsContent => Array.IndexOf (ContentExt, System.IO.Path.GetExtension (this.Path)) != -1;

        public FileInfo (Site site, string path)
        {
            this.Site = site;
            this.Path = path;

            if (this.IsContent)
            {
                _meta = new MetaDict ();
                Parse ();
            }
        }

        string GetValue (object v) => (v is Func<string> fn) ? fn.Invoke () : v.ToString ();

        void Parse ()
        {
            Log.Info ($"Parsing file {this.Path}...");

            using (StreamReader sr = new StreamReader (File.OpenRead (this.Path)))
            {
                if (sr.ReadLine ().Trim () != "---")
                    _meta["content"] = File.ReadAllText (this.Path);
                else
                {
                    while (true)
                    {
                        var m = _rkeyval.Match (sr.ReadLine ().Trim ());
                        if (!m.Success)
                            break;

                        _meta[m.Groups[1].Value] = m.Groups[2].Value.Trim ();
                    }

                    _meta["content"] = sr.ReadToEnd ();
                }
            }

            if (string.IsNullOrEmpty (_meta.Get ("slug")))
            {
                // Extract the slug (we expect the filename to be yyyymmdd-title)
                var parts = System.IO.Path.GetFileNameWithoutExtension (Path).Split (new char[] { '-' }, 2);
                _meta["slug"] = parts.Length == 2 ? parts[1] : parts[0];
            }

            _meta["url"] = Url;

            _meta.Merge (Site.Meta, "site.");
        }

        readonly MetaDict _meta;

        readonly Regex _rkeyval = new Regex (@"^\s*([a-zA-Z0-9-_]+)\s*\:\s*(.+)$");
    }

    class Site
    {
        public string RootPath { get; private set; }
        public string OutputPath { get; private set; }
        public string ContentPath { get; private set; }
        public string TemplatesPath { get; private set; }
        public string Url => _meta.Get ("url");
        public MetaDict Meta => _meta;

        public Site (string localRoot)
        {
            RootPath = localRoot;

            _meta = new MetaDict ();

            ContentPath = Path.Combine (localRoot, "content");
            TemplatesPath = Path.Combine (localRoot, "templates");
            OutputPath = Path.Combine (localRoot, "output");

            _files = EnumerateFiles ().ToList ();
        }

        public bool Parse ()
        {
            var iniPath = Path.Combine (RootPath, "site.ini");
            if (!File.Exists (iniPath))
            {
                Log.Error ($"{iniPath} not found.");
                return false;
            }

            _meta.Merge (new MetaDict (File.ReadAllLines (Path.Combine (RootPath, "site.ini"))
                                      .Select (line => _rkeyval.Match (line))
                                      .Where (m => m.Success)
                                      .ToDictionary (m => m.Groups[1].Value, m => (object)m.Groups[2].Value)));

            if (!_meta.Get ("url").EndsWith ("/"))
                _meta["url"] = _meta.Get ("url") + "/";

            return true;
        }

        public void Emit ()
        {
            if (!Directory.Exists (ContentPath))
            {
                Log.Info ("No content directory found. Exiting.");
                return;
            }

            if (Directory.Exists (OutputPath))
            {
                Log.Info ("Deleting previous output directory...");
                Directory.Delete (OutputPath, true);
            }

            foreach (var finfo in _files)
                EmitFile (finfo);

            Log.Info ($"Done. {_files.Count} files processed.");
        }

        void EnsurePathExists (string path)
        {
            if (!Directory.Exists (path))
                Directory.CreateDirectory (path);
        }

        internal string MakeRelative (string path, string root)
        {
            var result = path.Substring (root.Length);
            if (result.StartsWith (Path.DirectorySeparatorChar))
                result = result.Substring (1);

            return result;
        }

        string ApplyFilter (string value, string filterName)
        {
            if (!_filters.TryGetValue (filterName, out var fn))
            {
                Log.Warn ($"Filter {filterName} not found.");
                return value;
            }

            return fn (value);
        }

        string ExpandMeta (string template, FileInfo finfo) => _rkey.Replace (template, m => {
            var v = finfo.Meta.Get (m.Groups[1].Value);
            return (m.Groups[2].Success) ? ApplyFilter (v, m.Groups[2].Value) : v;
        });

        string GenerateIndex (string category, string pattern)
        {
            var files = _files.Where (f => f.IsContent && (bool)f.Meta.Get ("category").Equals (category));

            StringBuilder sb = new StringBuilder ();
            foreach (var finfo in files)
                sb.Append (ExpandMeta (pattern, finfo));
            return sb.ToString ();
        }

        public void EmitFile (string path)
        {
            FileInfo finfo = _files.FirstOrDefault (f => f.Path.Equals (path));
            if (finfo == null)
                _files.Remove (finfo);

            finfo = new FileInfo (this, path);
            _files.Add (finfo);

            EmitFile (finfo);
        }

        public void EmitFile (FileInfo finfo)
        {
            Log.Info ($"Emiting file {finfo.Path}...");
            Log.Indent ();

            string output = finfo.OutputPath;
            EnsurePathExists (Path.GetDirectoryName (output));

            if (!finfo.IsContent)
            {
                Log.Info ($"> Copying contents to {output}...");
                File.Copy (finfo.Path, output);
            }
            else
            {
                Log.Info ($"> Expanding contents...");
                finfo.Meta["content"] = _rindex.Replace (finfo.Meta.Get ("content"), m => {
                    Log.Info ($"> Generating index for category '{m.Groups[1].Value}'...");
                    return GenerateIndex (m.Groups[1].Value, m.Groups[2].Value);
                });

                var ext = Path.GetExtension (finfo.Path);
                if (ext.Equals (".md", StringComparison.InvariantCultureIgnoreCase))
                {
                    Log.Info ($"> Rendering markdown content...");
                    finfo.Meta["content"] = RenderMarkdown (finfo.Meta.Get ("content"));
                    ext = ".html";
                }

                string content = ExpandMeta (finfo.Meta.Get ("content") , finfo);

                var tpl = finfo.Meta.Get ("template");
                if (!string.IsNullOrEmpty (tpl))
                {
                    Log.Info ($"> Using template '{tpl}'...");
                    var tplPath = Path.Combine (TemplatesPath, tpl);
                    if (!File.Exists (tplPath))
                        Log.Warn ($"Template {tplPath} not found.");
                    else
                        content = ExpandMeta (File.ReadAllText (Path.Combine (TemplatesPath, tpl)), finfo);
                }

                var foutput = Path.Combine (Path.GetDirectoryName (output), finfo.Meta.Get ("slug") + ext);
                if (File.Exists (foutput))
                    File.Delete (foutput);

                Log.Info ($"> Writing contents to {foutput}...");
                File.WriteAllText (foutput, content);
            }

            Log.Unindent ();
            Log.Info ($"> Done.");
        }

        string RenderMarkdown (string text)
        {
            using (var wc = new System.Net.WebClient ())
            {
                wc.Headers.Add (System.Net.HttpRequestHeader.UserAgent, "sitegen (https://github.com/luismedel/sitegen)");
                wc.Headers.Add (System.Net.HttpRequestHeader.ContentType, "text/plain");
                wc.Headers.Add (System.Net.HttpRequestHeader.Accept, "application/vnd.github.v3+json");
                var result = wc.UploadData ("https://api.github.com/markdown/raw", System.Text.Encoding.UTF8.GetBytes (text));
                return Encoding.UTF8.GetString (result);
            }
        }

        IEnumerable<FileInfo> EnumerateFiles ()
        {
            return Directory.EnumerateFiles (ContentPath, "*", System.IO.SearchOption.AllDirectories)
                            .Where (s => !s.StartsWith ("_"))
                            .Select (path => new FileInfo (this, path))
                            .Where (f => !f.IsContent || (f.IsContent && f.Meta.Get ("status") != "draft"));
        }

        static bool TryParseDate (string date, out DateTime result) => DateTime.TryParseExact (date, new string[] { "yyyyMMdd", "yyyyMMddhhmm", "yyyyMMddhhmmss" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

        readonly Dictionary<string, Func<string, string>> _filters = new Dictionary<string, Func<string, string>> (StringComparer.InvariantCultureIgnoreCase) {
            { "upper",    s => s.ToUpper () },
            { "lower",    s => s.ToLower () },
            { "rfc822",   s => TryParseDate (s, out var dt) ? dt.ToString ("r") : s },
            { "yyyymmdd", s => TryParseDate (s, out var dt) ? dt.ToString ("yyyyMMdd") : s },
        };

        readonly List<FileInfo> _files;

        readonly MetaDict _meta;

        readonly Regex _rindex = new Regex (@"\{\%index\|([a-zA-Z0-9-_]+)\|([^&]+)\%\}"); // {%index|category|pattern%}
        readonly Regex _rkey = new Regex (@"\{([a-zA-Z0-9-_.]+)(?:\|([a-zA-Z0-9-_]+))?\}"); // {key}
        readonly Regex _rkeyval = new Regex (@"^\s*([a-zA-Z0-9-_]+)\s*\=\s*(.+)$");
    }

    class Program
    {
        static void ShowUsage ()
        {
            Console.WriteLine (@"
Usage:

    clarin <command> [<path>]

Commands:

    build       generates the site in <path>/output
    watch       watches for changes and generates the site continuously
    init        inits a new site
");
        }

        static void Main (string[] args)
        {
            System.Diagnostics.Trace.Listeners.Add (new System.Diagnostics.ConsoleTraceListener ());

            if (args.Length == 0)
            {
                ShowUsage ();
                return;
            }

            var command = args[0];
            if (!_commands.TryGetValue (command, out var cmd))
            {
                Log.Error ($"Unrecognized command '{command}'.");
                return;
            }

            var path = args.Length == 1 ? "." : args[1];
            cmd (new Site (path));
        }

        static void CmdEmit (Site site)
        {
            if (!site.Parse ())
                return;

            site.Emit ();
        }

        static void CmdObserve (Site site)
        {
            if (!site.Parse ())
                return;

            site.Emit ();

            using (FileSystemWatcher fsw = new FileSystemWatcher (site.ContentPath))
            {
                fsw.Changed += (object sender, FileSystemEventArgs e) => site.EmitFile (e.FullPath);
                fsw.Created += (object sender, FileSystemEventArgs e) => site.EmitFile (e.FullPath);
                fsw.Renamed += (object sender, RenamedEventArgs e) => site.EmitFile (e.FullPath);
                fsw.EnableRaisingEvents = true;

                Console.WriteLine ("Press any key to stop...");
                Console.ReadKey ();

                fsw.EnableRaisingEvents = false;
            }
        }

        static void CmdInit (Site site)
        {
            if (site.Parse ())
            {
                Log.Error ($"{site.RootPath} already contains a site.");
                return;
            }

            Directory.CreateDirectory (site.ContentPath);
            Directory.CreateDirectory (site.TemplatesPath);

            File.WriteAllText (Path.Combine (site.RootPath, "site.ini"), @"
title = my new site
description = my new site description
url = http://127.0.0.1/
");
        }

        static readonly Dictionary<string, Action<Site>> _commands = new Dictionary<string, Action<Site>> {
            { "build", new Action<Site> (CmdEmit) },
            { "observe", new Action<Site> (CmdObserve) },
            { "init", new Action<Site> (CmdInit) },
        };
    }
}