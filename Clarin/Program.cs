using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net.Http;

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

    class MetaDict
    {
        public IEnumerable<string> Keys => _dict.Keys;

        public object this[string key]
        {
            private get => _dict.TryGetValue (key, out object v)
                   ? (v is Func<string> fn) ? fn.Invoke () : _rref.Replace (v.ToString (), m => this.Get (m.Groups[1].Value))
                   : null;

            set => _dict[key] = value;
        }

        public MetaDict ()
        { }

        public MetaDict (MetaDict other)
            : this (other._dict)
        { }

        public MetaDict (IDictionary<string, object> contents)
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

        static string SysDate () => DateTime.Now.ToString ("yyyyMMddhhmmss");

        readonly Dictionary<string, object> _dict = new Dictionary<string, object> (StringComparer.InvariantCultureIgnoreCase) {
#pragma warning disable CS8974
            { "sys.date", new Func<string> (SysDate) },
#pragma warning restore CS8974
        };

        public static bool TryParseKeyValue (string s, out string key, out string value)
        {
            var m = _rkeyval.Match (s);
            if (!m.Success)
            {
                key = value = String.Empty;
                return false;
            }

            key = m.Groups[1].Value;
            value = m.Groups[2].Value;
            if ((value.EndsWith ('"') && value.EndsWith ('"'))
             || (value.EndsWith ('\'') && value.EndsWith ('\'')))
                value = value.Substring (1, value.Length - 2);

            return true;
        }

        static readonly Regex _rref = new Regex (@"\$([a-zA-Z0-9-_.]+)");

        static readonly Regex _rkeyval = new Regex (@"^\s*([a-zA-Z0-9-_]+)\s*\=\s*(.+)$", RegexOptions.Compiled);
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
            this.Path = path.EndsWith ('/') ? path.Substring (0, path.Length - 1) : path;

            if (this.IsContent)
            {
                _meta = new MetaDict ();
                Parse ();
            }
        }

        string Slugify (string s)
        {
            Dictionary<char, char> replacements = new Dictionary<char, char> {
                { 'á', 'a' }, { 'é', 'e' }, { 'í', 'i' },
                { 'ó', 'o' }, { 'ú', 'u' }, { 'ñ', 'n' },
            };

            s = s.ToLower ();
            foreach (var kv in replacements)
                s = s.Replace (kv.Key, kv.Value);
            return Regex.Replace (s, @"[^a-z0-9-_]+", "-");
        }

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
                        var line = sr.ReadLine ().Trim ();
                        if (line == "---")
                            break;

                        if (MetaDict.TryParseKeyValue (line, out var k, out var v))
                            _meta[k] = v;
                    }

                    _meta["content"] = sr.ReadToEnd ();
                }
            }

            if (string.IsNullOrEmpty (_meta.Get ("slug")) || string.IsNullOrEmpty (_meta.Get ("date")))
            {
                // Try to extract slug and date from the filename (we expect it to be 'date-title.ext')
                var m = Regex.Match (System.IO.Path.GetFileNameWithoutExtension (Path), @"^(\d+)\-(.+)$");
                if (m.Success)
                {
                    _meta["date"] = m.Groups[1].Value;
                    _meta["slug"] = Slugify (m.Groups[2].Value);
                }
                else
                    _meta["slug"] = Slugify (System.IO.Path.GetFileNameWithoutExtension (Path));
            }

            _meta["title"] = _meta.Get ("title", _meta.Get ("slug"));
            _meta["url"] = Url;

            _meta.Merge (Site.Meta, "site.");
        }

        public override string ToString () => Path;

        readonly MetaDict _meta;
    }

    class Site
    {
        public string RootPath { get; private set; }
        public string OutputPath { get; private set; }
        public string ContentPath { get; private set; }
        public string TemplatesPath { get; private set; }
        public string Url => _meta.Get ("url");
        public MetaDict Meta => _meta;

        List<FileInfo> Files
        {
            get
            {
                if (_files == null)
                    _files = EnumerateFiles ().ToList ();
                return _files;
            }
        }

        public Site (string localRoot)
        {
            RootPath = localRoot;

            _meta = new MetaDict ();
            _env = new MetaDict (LoadConfigFile (".env"));

            ContentPath = Path.Combine (localRoot, "content");
            TemplatesPath = Path.Combine (localRoot, "templates");
            OutputPath = Path.Combine (localRoot, "output");
        }

        string Env (string key)
        {
            var result = _env.Get (key);
            return String.IsNullOrEmpty (result) ? Environment.GetEnvironmentVariable (key) : result;
        }

        Dictionary<string, object> LoadConfigFile (string filename)
        {
            var path = Path.Combine (RootPath, filename);
            if (!File.Exists (path))
                return new Dictionary<string, object> ();

            return File.ReadAllLines (path)
                       .Select (line => MetaDict.TryParseKeyValue (line, out var k, out var v) ? Tuple.Create (k, v) : null)
                       .Where (t => t != null)
                       .ToDictionary (t => t.Item1, t => (object)t.Item2);
        }

        public bool TryParse ()
        {
            if (_parsed)
                return true;

            var iniPath = Path.Combine (RootPath, "site.ini");
            if (!File.Exists (iniPath))
            {
                Log.Error ($"{iniPath} not found.");
                return false;
            }

            _meta.Merge (new MetaDict (LoadConfigFile ("site.ini")));

            if (!_meta.Get ("url").EndsWith ("/"))
                _meta["url"] = _meta.Get ("url") + "/";

            _parsed = true;
            return true;
        }

        public void Emit ()
        {
            if (!TryParse ())
                return;

            if (!Directory.Exists (ContentPath))
            {
                Log.Info ("No content directory found. Exiting.");
                return;
            }

            if (Directory.Exists (OutputPath))
            {
                Log.Info ("Deleting previous output directory...");
                try { Directory.Delete (OutputPath, true); }
                catch (IOException ex) { Log.Warn ($"> {ex.Message}"); }
            }

            foreach (var finfo in Files)
                EmitFile (finfo);

            Log.Info ($"Done. {Files.Count} files processed.");
        }

        public void EnsurePathExists (string path)
        {
            if (!Directory.Exists (path))
                Directory.CreateDirectory (path);
        }

        public string MakeRelative (string path, string root)
        {
            var result = path.Substring (root.Length);
            if (result.StartsWith (Path.DirectorySeparatorChar))
                result = result.Substring (1);

            return result;
        }

        string LoadTemplate (string name)
        {
            if (string.IsNullOrEmpty (name))
                return String.Empty;

            var tplPath = Path.Combine (TemplatesPath, name);
            if (!File.Exists (tplPath))
            {
                Log.Warn ($"> Template {tplPath} not found.");
                return string.Empty;
            }

            return File.ReadAllText (tplPath);
        }

        string ApplyFilter (string value, string filter)
        {
            if (!_filters.TryGetValue (filter, out var fn))
                return TryParseDate (value, out var dt) ? dt.ToString (filter) : value;
            else
                return fn (this, value);
        }

        string ExpandMeta (string text, MetaDict meta)
        {
            while (_rinc.IsMatch (text))
            {
                text = _rinc.Replace (text, m => {
                    Log.Info ($"> Including '{m.Groups[1].Value}'...");
                    return LoadTemplate (m.Groups[1].Value);
                });
            }

            text = _rindex.Replace (text, m => {
                Log.Info ($"> Generating index for category '{m.Groups[1].Value}'...");
                return GenerateIndex (m.Groups[1].Value, m.Groups[2].Value, meta);
            });

            return _rtag.Replace (text, m => {
                var v = meta.Get (m.Groups[1].Value);
                return (m.Groups[2].Success) ? ApplyFilter (v, m.Groups[2].Value) : v;
            });
        }

        string GenerateIndex (string category, string pattern, MetaDict meta)
        {
            var files = Files.Where (f => f.IsContent && f.Meta.Get ("category").Equals (category))
                             .OrderBy (f => TryParseDate (f.Meta.Get ("date"), out var dt) ? dt : DateTime.Now);

            StringBuilder sb = new StringBuilder ();
            foreach (var finfo in files)
            {
                var fmeta = new MetaDict (finfo.Meta);
                fmeta.Merge (meta, "page.");
                sb.Append (ExpandMeta (pattern, fmeta));
            }

            return sb.ToString ();
        }

        public void EmitFile (string path)
        {
            FileInfo finfo = Files.FirstOrDefault (f => f.Path.Equals (path));
            if (finfo == null)
                Files.Remove (finfo);

            finfo = new FileInfo (this, path);
            Files.Add (finfo);

            EmitFile (finfo);
        }

        public void EmitFile (FileInfo finfo)
        {
            Log.Info ($"Emiting file {finfo.Path}...");
            Log.Indent ();

            var output = finfo.OutputPath;
            EnsurePathExists (Path.GetDirectoryName (output));

            if (!finfo.IsContent)
            {
                Log.Info ($"> Copying file to {output}...");
                File.Copy (finfo.Path, output);
                Log.Unindent ();
                return;
            }

            MetaDict meta = new MetaDict (finfo.Meta);

            Log.Info ($"> Expanding contents...");

            meta["content"] = ExpandMeta (meta.Get ("content"), meta);

            var ext = Path.GetExtension (finfo.Path);
            if (ext.Equals (".md", StringComparison.InvariantCultureIgnoreCase))
            {
                Log.Info ($"> Rendering markdown content...");
                Log.Indent ();
                meta["content"] = RenderMarkdown (meta.Get ("content"));
                Log.Unindent ();
                ext = ".html";
            }

            var template = LoadTemplate (meta.Get ("template"));
            if (!string.IsNullOrEmpty (template))
                    meta["content"] = ExpandMeta (template, meta);

            var foutput = Path.Combine (Path.GetDirectoryName (output), meta.Get ("slug") + ext);
            if (File.Exists (foutput))
                File.Delete (foutput);

            Log.Info ($"> Writing contents to {foutput}...");
            File.WriteAllText (foutput, meta.Get ("content"));

            Log.Unindent ();
            Log.Info ($"> Done.");
        }

        string RenderMarkdown (string text)
        {
            try
            {
                if (_httpClient == null)
                {
                    var ghusername = Env ("CLARIN_GHUSER");
                    var ghtoken = Env ("CLARIN_GHTOKEN");

                    _httpClient = new HttpClient ();
                    if (!string.IsNullOrWhiteSpace (ghusername) && !string.IsNullOrWhiteSpace (ghtoken))
                    {
                        var auth = Encoding.ASCII.GetBytes ($"{ghusername}:{ghtoken}");
                        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue ("Basic", Convert.ToBase64String (auth));
                        Log.Info ($"> Using autenticated Github requests as {ghusername}.");
                    }
                    else
                        Log.Info ($"> Using unauthenticated Github requests.");
                }

                using (var req = new HttpRequestMessage (HttpMethod.Post, "https://api.github.com/markdown/raw"))
                {
                    req.Headers.Add ("User-Agent", "Clarin (https://github.com/luismedel/clarin)");
                    req.Headers.Add ("Accept", "application/vnd.github.v3+json");
                        
                    req.Content = new StringContent (text, Encoding.UTF8, "text/plain");

                    Log.Info ($"> {req.RequestUri}");
                    var resp = _httpClient.Send (req);
                    Log.Info ($"> {(int)resp.StatusCode} ({resp.StatusCode})");

                    string result;
                    using (StreamReader sr = new StreamReader (resp.Content.ReadAsStream ()))
                        result = sr.ReadToEnd ();

                    if (resp.IsSuccessStatusCode)
                    {
                        if (resp.Headers.TryGetValues ("X-RateLimit-Limit", out var reqLimit)
                         && resp.Headers.TryGetValues ("X-RateLimit-Remaining", out var reqRemaining)
                         && resp.Headers.TryGetValues ("X-RateLimit-Reset", out var reqReset))
                            Log.Info ($"> Remaining requests {reqRemaining.First ()}/{reqLimit.First ()} (reset on {new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds (double.Parse (reqReset.First ())).ToLocalTime ()})");

                        return result;
                    }
                    else
                    {
                        Log.Warn ($"> {result}");
                        return text;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error ($" > {ex.Message}");
                return text;
            }
        }

        IEnumerable<FileInfo> EnumerateFiles ()
        {
            return Directory.EnumerateFiles (ContentPath, "*", SearchOption.AllDirectories)
                            .Where (s => !s.StartsWith ("_"))
                            .Select (path => new FileInfo (this, path))
                            .Where (f => !f.IsContent || (f.IsContent && !f.Meta.Get ("draft").Equals ("true", StringComparison.InvariantCultureIgnoreCase)));
        }

        static bool TryParseDate (string date, out DateTime result)
        {
            var formats = new string[] { "yyyyMMdd","yyyy-MM-dd", "yyyy.MM.dd", "yyyy/MM/dd" };
            return DateTime.TryParseExact (date, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        readonly Dictionary<string, Func<Site, string, string>> _filters = new Dictionary<string, Func<Site, string, string>> (StringComparer.InvariantCultureIgnoreCase) {
            { "upper",    (site,s) => s.ToUpper () },
            { "lower",    (site,s) => s.ToLower () },
            { "date",     (site,s) => TryParseDate (s, out var dt) ? dt.ToString (site.Meta.Get ("dateFormat", "yyyyMMdd")) : s },
            { "rfc822",   (site,s) => TryParseDate (s, out var dt) ? dt.ToString ("r") : s },
        };

        List<FileInfo> _files;

        bool _parsed;
        readonly MetaDict _meta;
        readonly MetaDict _env;

        HttpClient _httpClient = null;

        static readonly Regex _rindex = new Regex (@"\{\%index\|([a-zA-Z0-9-_]+)\|([^&]+)\%\}", RegexOptions.Compiled); // {%index|category|pattern%}
        static readonly Regex _rinc = new Regex (@"\{\%inc\|([^%]+)\%\}", RegexOptions.Compiled); // {%inc|template%}
        static readonly Regex _rtag = new Regex (@"\{([a-zA-Z0-9-_.]+)(?:\|([^}]+))?\}", RegexOptions.Compiled); // {key}
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
  init        inits a new site
  add         adds a new empty entry in <path>/content
  version     prints the version number of Clarin

<path> defaults to current directory if not specified.
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
                Log.Error ($"Unknown command '{command}'.");
                return;
            }

            var path = args.Length == 1 ? "." : args[1];
            cmd (new Site (path));
        }

        static void CmdEmit (Site site)
        {
            if (!site.TryParse ())
                return;

            site.Emit ();
        }

        static void CmdInit (Site site)
        {
            if (site.TryParse ())
            {
                Log.Error ($"{site.RootPath} already contains a site.");
                return;
            }

            site.EnsurePathExists (site.RootPath);
            site.EnsurePathExists (site.ContentPath);
            site.EnsurePathExists (site.TemplatesPath);

            File.WriteAllText (Path.Combine (site.RootPath, "site.ini"), @"
title = ""my new site""
description = ""my new site description""
; Root url for your site
url = ""http://127.0.0.1/""
; Defines how Clarin prints the dates when using the '|date' filter
dateFormat  = ""yyyy-MM-dd""
");
        }

        static void CmdAdd (Site site)
        {
            if (!site.TryParse ())
                return;

            var filename = DateTime.Now.ToString ("yyyyMMdd") + "-new-entry";
            var path = Path.Combine (site.ContentPath, $"{filename}.md");

            File.WriteAllText (path, $@"
---
title: ""New entry""
slug: ""new-entry""
date: ""{DateTime.Now:yyyyMMdd}""
category: ""blog""
draft: ""true""
---

Your content here.
");
            Console.WriteLine ($"Added {path}.");
        }

        static void CmdVersion (Site site)
        {
            var version = typeof (Program).Assembly.GetName ().Version;
            Console.WriteLine ($"Clarin v{version}");
        }

        static readonly Dictionary<string, Action<Site>> _commands = new Dictionary<string, Action<Site>> {
            { "build", new Action<Site> (CmdEmit) },
            { "init", new Action<Site> (CmdInit) },
            { "add", new Action<Site> (CmdAdd) },
            { "version", new Action<Site> (CmdVersion) },
        };
    }
}