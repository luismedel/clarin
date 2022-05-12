using System;
using System.Collections.Generic;
using System.IO;

namespace Clarin.IO
{
    public static class DirectoryWalker
    {
        public static IEnumerable<string> EnumerateDirectories (string path)
        {
            yield return path;

            foreach (var subdir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(subdir, ".clarinignore")))
                    continue;

                yield return subdir;
            }
        }

        public static IEnumerable<string> EnumerateFiles (string path)
        {
            foreach (var dir in EnumerateDirectories (path))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                    yield return file;
            }
        }
    }
}
