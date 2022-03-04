using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Clarin
{
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
                       .Select (line => MetaDict.TryParseKeyValue (line, out var k, out var v)
                                    ? Tuple.Create (k, v)
                                    : null)
                       .Where (t => t != null)
                       .ToDictionary (t => t.Item1, t => (object) t.Item2);
        }

        public bool TryParse (bool logError = true, MetaDict overrides = null)
        {
            if (_parsed)
                return true;

            var iniPath = Path.Combine (RootPath, "site.ini");
            if (!File.Exists (iniPath))
            {
                if (logError)
                    Log.Error ($"{iniPath} not found.");

                return false;
            }

            _meta.Merge (new MetaDict (LoadConfigFile ("site.ini")));

            if (overrides != null)
                _meta.Merge (overrides);

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
                try
                {
                    Directory.Delete (OutputPath, true);
                }
                catch (IOException ex)
                {
                    Log.Warn ($"> {ex.Message}");
                }
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
                var count = m.Groups[2].Success ? int.Parse (m.Groups[2].Value) : -1;
                return GenerateIndex (m.Groups[1].Value, count, m.Groups[3].Value, meta);
            });

            text = _rref.Replace (text, m => {
                var f = _files.FirstOrDefault (f => f.Meta.Get ("slug").Equals (m.Groups[1].Value));
                return f?.Url ?? string.Empty;
            });

            return _rtag.Replace (text, m => {
                var v = meta.Get (m.Groups[1].Value);
                return (m.Groups[2].Success) ? ApplyFilter (v, m.Groups[2].Value) : v;
            });
        }

        string GenerateIndex (string category, int limit, string pattern, MetaDict meta)
        {
            var files = Files.Where (f => f.IsContent && f.Meta.Get ("category").Equals (category))
                             .OrderByDescending (f => TryParseDate (f.Meta.Get ("date"), out var dt) ? dt : DateTime.Now)
                             .ToList ();
            if (limit > 0)
                files = files.Take (limit).ToList ();

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
            if (finfo != null)
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

                try
                {
                    if (File.Exists (output)) File.Delete (output);
                }
                catch (IOException ex)
                {
                    Log.Error (ex.Message);
                }

                try
                {
                    File.Copy (finfo.Path, output);
                }
                catch (IOException ex)
                {
                    Log.Error (ex.Message);
                }

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
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue (
                                "Basic", Convert.ToBase64String (auth));
                        Log.Info ($"> Using authenticated Github requests as {ghusername}.");
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
                    Log.Info ($"> {(int) resp.StatusCode} ({resp.StatusCode})");

                    string result;
                    using (StreamReader sr = new StreamReader (resp.Content.ReadAsStream ()))
                        result = sr.ReadToEnd ();

                    if (resp.IsSuccessStatusCode)
                    {
                        if (resp.Headers.TryGetValues ("X-RateLimit-Limit", out var reqLimit)
                            && resp.Headers.TryGetValues ("X-RateLimit-Remaining", out var reqRemaining)
                            && resp.Headers.TryGetValues ("X-RateLimit-Reset", out var reqReset))
                            Log.Info (
                                $"> Remaining requests {reqRemaining.First ()}/{reqLimit.First ()} (reset on {new DateTime (1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds (double.Parse (reqReset.First ())).ToLocalTime ()})");

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
                            .Where (s => !Path.GetFileNameWithoutExtension (s).StartsWith ("_"))
                            .Select (path => new FileInfo (this, Path.GetFullPath (path)))
                            .Where (f => !f.IsContent || (f.IsContent && !f.Meta.Get ("draft")
                                                                           .Equals (
                                                                               "true",
                                                                               StringComparison
                                                                                   .InvariantCultureIgnoreCase)));
        }

        static bool TryParseDate (string date, out DateTime result)
        {
            var formats = new string[] { "yyyyMMdd", "yyyy-MM-dd", "yyyy.MM.dd", "yyyy/MM/dd", "yyyyMMddhhmmss" };
            return DateTime.TryParseExact (date, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal,
                                           out result);
        }

        readonly Dictionary<string, Func<Site, string, string>> _filters =
            new Dictionary<string, Func<Site, string, string>> (StringComparer.InvariantCultureIgnoreCase) {
                {"upper",    (site, s) => s.ToUpper ()},
                {"lower",    (site, s) => s.ToLower ()}, {
                    "date",
                    (site, s) => TryParseDate (s, out var dt)
                        ? dt.ToString (site.Meta.Get ("dateFormat", "yyyyMMdd"))
                        : s
                },
                {"rfc822",   (site, s) => TryParseDate (s, out var dt) ? dt.ToString ("r") : s},
            };

        List<FileInfo> _files;

        bool _parsed;
        readonly MetaDict _meta;
        readonly MetaDict _env;

        HttpClient _httpClient = null;

        static readonly Regex
            _rindex = new Regex (@"\{\%index\|([a-zA-Z0-9-_]+)(?:\((\d+)\))?\|([^%]+)\%\}",
                                 RegexOptions.Compiled); // {%index|category(limit)|pattern%}

        static readonly Regex _rinc = new Regex (@"\{\%inc\|([^%]+)\%\}", RegexOptions.Compiled); // {%inc|template%}
        static readonly Regex _rtag = new Regex (@"\{([a-zA-Z0-9-_.]+)(?:\|([^}]+))?\}", RegexOptions.Compiled); // {key}
        static readonly Regex _rref = new Regex (@"\{\#([^}]+)\}", RegexOptions.Compiled); // {#slug}
    }
}