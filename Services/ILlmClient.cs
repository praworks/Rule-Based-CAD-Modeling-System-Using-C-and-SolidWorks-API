using System.Threading.Tasks;

namespace AICAD.Services
{
    public interface ILlmClient
    {
        /// <summary>
        /// Sends a prompt to the configured LLM and returns the assistant text.
        /// </summary>
        Task<string> GenerateAsync(string prompt);

        /// <summary>
        /// Streams assistant text tokens for the given prompt. Implementations that do not support
        /// true streaming may invoke the callback once with the full response.
        /// </summary>
        Task StreamAsync(string prompt, System.Action<string> onDelta, System.Threading.CancellationToken cancellationToken);

        /// <summary>
        /// Human-readable model identifier used by this client.
        /// </summary>
        string Model { get; }
    }
}
