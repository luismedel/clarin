using System;
using System.Collections.Generic;
using System.IO;

namespace Clarin
{
    class Program
    {
        static void ShowUsage ()
        {
            Console.WriteLine (@"
Usage:
clarin <command> [--local] [<path>]

Commands:
build       generates the site in <path>/output
watch       watches for changes and builds the site continuously
init        inits a new site
add         adds a new empty entry in <path>/content
version     prints the version number of Clarin

--local     overrides the value in site.ini and sets the site url
            to the current site local path

<path> defaults to current directory if not specified.
");
        }

        static string NextOpt (string[] args, ref int index) => args.Length <= index ? null : args[index++];

        static void Main (string[] args)
        {
            System.Diagnostics.Trace.Listeners.Add (new System.Diagnostics.ConsoleTraceListener ());

            if (args.Length == 0)
            {
                ShowUsage ();
                return;
            }

            var cfg = new RunConfig ();

            var optidx = 0;
            cfg.Command = NextOpt (args, ref optidx);
            var opt = NextOpt (args, ref optidx);
            var isLocal = false;
            if (opt == "--local")
            {
                cfg.Path = NextOpt (args, ref optidx) ?? ".";
                isLocal = true;
            }
            else
                cfg.Path = opt ?? ".";

            cfg.Site = new Site (Path.GetFullPath (cfg.Path));

            if (isLocal)
            {
                var url = Path.GetFullPath (cfg.Site.OutputPath);
                if (!url.EndsWith ("/"))
                    url += "/";
                cfg.SiteOverrides["url"] = url;
            }

            if (!_commands.TryGetValue (cfg.Command, out var cmd))
            {
                Log.Error ($"Unknown command '{cfg.Command}'.");
                return;
            }

            cmd (cfg);
        }

        static void CmdEmit (RunConfig cfg)
        {
            Site site = cfg.Site;
            if (!site.TryParse (overrides:cfg.SiteOverrides))
                return;

            site.Emit ();
        }

        static void CmdInit (RunConfig cfg)
        {
            Site site = cfg.Site;
            if (site.TryParse (logError: false, overrides:cfg.SiteOverrides))
            {
                Log.Error ($"{site.RootPath} already contains a site.");
                return;
            }

            site.EnsurePathExists (site.RootPath);
            site.EnsurePathExists (site.ContentPath);
            site.EnsurePathExists (site.TemplatesPath);

            File.WriteAllText (Path.Combine (site.RootPath, "site.ini"), @$"
title = ""my new site""
description = ""my new site description""

; Root url for your site. Can be a local path too
url = ""http://127.0.0.1/""
;url = ""{site.OutputPath}""

; Defines how Clarin prints the dates when using the '|date' filter
dateFormat  = ""yyyy-MM-dd""
");
        }

        static void CmdWatch (RunConfig cfg)
        {
            Site site = cfg.Site;
            if (!site.TryParse (overrides:cfg.SiteOverrides))
                return;

            site.Emit ();

            Log.Info ($"Waiting for changes in {Path.GetFullPath (site.ContentPath)}. Press any key to stop.");

            using (var fsw = new FileSystemWatcher ())
            {
                fsw.Path = Path.GetFullPath (site.ContentPath);
                fsw.Filter = "*.*";
                fsw.NotifyFilter = NotifyFilters.Size
                    | NotifyFilters.CreationTime
                    | NotifyFilters.LastWrite
                    | NotifyFilters.FileName;

                fsw.Renamed += (sender,  args) => site.EmitFile (args.FullPath);
                fsw.Changed += (sender,  args) => site.EmitFile (args.FullPath);
                fsw.Created += (sender,  args) => site.EmitFile (args.FullPath);

                fsw.EnableRaisingEvents = true;

                Console.ReadKey ();

                fsw.EnableRaisingEvents = false;
            }
        }

        static void CmdAdd (RunConfig cfg)
        {
            Site site = cfg.Site;
            if (!site.TryParse (overrides:cfg.SiteOverrides))
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

        static void CmdVersion (RunConfig cfg)
        {
            var version = typeof (Program).Assembly.GetName ().Version;
            Console.WriteLine ($"Clarin v{version}");
        }

        static readonly Dictionary<string, Action<RunConfig>> _commands = new Dictionary<string, Action<RunConfig>> {
            {"build", CmdEmit},
            {"watch", CmdWatch},
            {"init", CmdInit},
            {"add", CmdAdd},
            {"version", CmdVersion},
        };
    }
}