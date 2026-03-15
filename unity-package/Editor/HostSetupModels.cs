namespace Patina.Editor
{
    public enum HostSetupMode
    {
        Automatic,
        ExportOnly
    }

    public enum HostSetupStatus
    {
        Configured,
        UpdatedStaleEntry,
        Removed,
        NotPresent,
        NotDetected,
        Failed
    }

    public sealed class HostDetectionResult
    {
        public string DisplayName { get; set; }
        public bool IsDetected { get; set; }
        public string ConfigPath { get; set; }
        public string Notes { get; set; }
    }

    public sealed class HostSetupResult
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public HostSetupMode Mode { get; set; }
        public HostSetupStatus Status { get; set; }
        public string ConfigPath { get; set; }
        public string Summary { get; set; }
        public string ExportSnippet { get; set; }
        public bool RequiresRestart { get; set; }
    }
}
