namespace TemporalShowcase.Contracts;

/// <summary>Stable Temporal signal name shared between the workflow and the document processor,
/// which signals by name only and does not reference the workflow assembly.</summary>
public static class TemporalSignalNames
{
    public const string DocumentProcessed = "DocumentProcessed";
}
