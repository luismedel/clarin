using System;
using System.Collections.Generic;
using System.IO;

namespace Clarin.IO
{
    public static class DirectoryWalker
    {
        public static IEnumerable<string> EnumerateDirectories (string path, bool recursive=false, bool force=false)
        {
            var queue = new Queue<string> (new[] { path });
            var so = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!force && File.Exists(Path.Combine(current, ".clarinignore")))
                    continue;

                foreach (var s in Directory.EnumerateDirectories(current, "*", so))
                {
                    queue.Enqueue(s);
                    yield return s;
                }
            }
        }

        public static IEnumerable<string> EnumerateFiles (string path, bool recursive = false, bool force = false)
        {
            var so = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var dir in EnumerateDirectories (path, recursive, force))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", so))
                    yield return file;

            }
        }
    }
}
