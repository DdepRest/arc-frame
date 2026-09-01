namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Identifies which API provider serves a model. Each provider has its own
    /// endpoint and its own user-configured API key.
    /// </summary>
    public enum AiProvider
    {
        OpenRouter = 0,
        Nvidia = 1
    }

    /// <summary>
    /// Represents an available AI model option (id and display name).
    /// </summary>
    public sealed class AiModelOption
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>Provider that serves this model (OpenRouter by default).</summary>
        public AiProvider Provider { get; set; } = AiProvider.OpenRouter;

        /// <summary>
        /// True when the provider catalog reported the model accepts image input
        /// (OpenRouter <c>architecture.input_modalities</c> contains "image").
        /// Null = unknown (NVIDIA catalog exposes no modality metadata, and cached
        /// entries saved by older builds have no value) — routing falls back to
        /// name heuristics then.
        /// </summary>
        public bool? SupportsVision { get; set; }

        public AiModelOption() { }

        public AiModelOption(string id, string displayName, AiProvider provider = AiProvider.OpenRouter)
        {
            Id = id;
            DisplayName = displayName;
            Provider = provider;
        }
    }
}
