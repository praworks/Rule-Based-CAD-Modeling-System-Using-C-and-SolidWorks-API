using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AICAD.Services
{
    internal interface IStepStore
    {
        string LastError { get; }
        Task<bool> SaveRunWithStepsAsync(
            string runKey,
            string prompt,
            string model,
            string planJson,
            StepExecutionResult exec,
            TimeSpan llm,
            TimeSpan total,
            string error);

        Task<bool> SaveFeedbackAsync(string runKey, bool up, string comment);

        List<string> GetRelevantFewShots(string prompt, int max = 3);
        List<RunRow> GetRecentRuns(int max = 50);
        List<StepRow> GetStepsForRun(string runKey);
    }

    internal class RunRow
    {
        public string RunKey { get; set; }
        public string Timestamp { get; set; }
        public string Prompt { get; set; }
        public string Model { get; set; }
        public string Plan { get; set; }
        public bool Success { get; set; }
        public long LlmMs { get; set; }
        public long TotalMs { get; set; }
        public string Error { get; set; }
    }

    internal class StepRow
    {
        public int StepIndex { get; set; }
        public string Op { get; set; }
        public string ParamsJson { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
    }
}
