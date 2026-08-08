namespace MosquitoNetCalculator.Models
{
    /// <summary>
    /// Identifies which API provider serves a model. Each provider has its own
    /// endpoint and its own API key (user-configured or built-in).
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

        public AiModelOption() { }

        public AiModelOption(string id, string displayName, AiProvider provider = AiProvider.OpenRouter)
        {
            Id = id;
            DisplayName = displayName;
            Provider = provider;
        }
    }
}
