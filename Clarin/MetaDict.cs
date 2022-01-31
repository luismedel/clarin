using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Clarin
{
    class MetaDict
    {
        public IEnumerable<string> Keys => _dict.Keys;

        public object this [string key]
        {
            private get => _dict.TryGetValue (key, out object v)
                ? (v is Func<string> fn) ? fn.Invoke () :
                _rref.Replace (v.ToString (), m => this.Get (m.Groups[1].Value))
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

        public string Get (string key, string @default = "") => (string) this[key] ?? @default;

        public void Merge (MetaDict other, string prefix = "")
        {
            foreach (var kv in other._dict)
                _dict[prefix + kv.Key] = kv.Value;
        }

        static string SysDate () => DateTime.Now.ToString ("yyyyMMddhhmmss");

        readonly Dictionary<string, object> _dict =
            new Dictionary<string, object> (StringComparer.InvariantCultureIgnoreCase) {
#pragma warning disable CS8974
                {"sys.date", new Func<string> (SysDate)},
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
}