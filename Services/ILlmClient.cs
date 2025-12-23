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
        /// Human-readable model identifier used by this client.
        /// </summary>
        string Model { get; }
    }
}
