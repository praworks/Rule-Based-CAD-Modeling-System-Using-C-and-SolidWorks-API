namespace AICAD.Services.Logging
{
    internal sealed class PromptMetadata
    {
        public PromptMetadata(string stage, string systemPromptKey, string templateKey)
        {
            Stage = stage;
            SystemPromptKey = systemPromptKey;
            TemplateKey = templateKey;
        }

        public string Stage { get; }

        public string SystemPromptKey { get; }

        public string TemplateKey { get; }
    }
}
