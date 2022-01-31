using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Clarin
{
    class FileInfo
    {
        static readonly string[] ContentExt = {".html", ".htm", ".md", ".xml"};

        public Site Site { get; private set; }
        public string Path { get; private set; }
        public MetaDict Meta => _meta;

        public string OutputPath
        {
            get
            {
                var relPath = Site.MakeRelative (Path, Site.ContentPath);
                var destPath = System.IO.Path.GetDirectoryName (System.IO.Path.Combine (Site.OutputPath, relPath));

                return IsContent
                    ? System.IO.Path.Combine (destPath, Meta.Get ("slug") + ".html")
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
                {'á', 'a'}, {'é', 'e'}, {'í', 'i'},
                {'ó', 'o'}, {'ú', 'u'}, {'ñ', 'n'},
            };

            s = s.ToLower ();
            foreach (var kv in replacements)
                s = s.Replace (kv.Key, kv.Value);
            return Regex.Replace (s, @"[^a-z0-9-_]+", "-");
        }

        void Parse ()
        {
            using (StreamReader sr = new StreamReader (File.OpenRead (this.Path)))
            {
                if (sr.ReadLine ()?.Trim () != "---")
                    _meta["content"] = File.ReadAllText (this.Path);
                else
                {
                    Log.Info ($"Parsing file {this.Path}...");

                    while (true)
                    {
                        var line = sr.ReadLine ()?.Trim ();
                        if (line == null)
                            break;

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
}