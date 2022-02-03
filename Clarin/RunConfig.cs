namespace Clarin
{
    class RunConfig
    {
        public string Path { get; set; }
        public string Command { get; set; }
        public Site Site { get; set; }

        public MetaDict SiteOverrides { get; } = new MetaDict ();
    }
}