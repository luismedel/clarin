namespace Clarin
{
    class RunConfig
    {
        public string Path { get; set; }
        public string Command { get; set; }
        public bool IsLocal { get; set; } = false;
        public Site Site { get; set; }
    }
}