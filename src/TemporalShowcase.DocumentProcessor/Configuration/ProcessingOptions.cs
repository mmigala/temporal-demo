namespace TemporalShowcase.DocumentProcessor.Configuration;

/// <summary>Configurable simulated processing delay, bound from configuration/environment variables.</summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    public TimeSpan ProcessingDelay { get; set; } = TimeSpan.FromSeconds(2);
}
