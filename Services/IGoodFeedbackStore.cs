using System.Collections.Generic;
using System.Threading.Tasks;

namespace AICAD.Services
{
    internal interface IGoodFeedbackStore
    {
        string LastError { get; }
        Task<bool> SaveGoodAsync(string runId, string prompt, string model, string planJson, string comment);
        List<string> GetRecentFewShots(int max = 2);
    }
}
