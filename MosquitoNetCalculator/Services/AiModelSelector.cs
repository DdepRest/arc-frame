using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Automatic model selector that picks the best free models for a given task type.
    /// Scoring is heuristic-based: larger instruction-tuned models score higher for
    /// Calculator tasks; smaller fast models are preferred for Help. Every model in
    /// the catalog gets a score ≥ 0 so it can still be used as fallback when top
    /// picks are unavailable.
    /// </summary>
    public static class AiModelSelector
    {
        /// <summary>
        /// Returns models ordered from best to worst for the given task, filtered to
        /// currently available catalog entries. Falls back to the full catalog if the
        /// intersection is empty.
        /// </summary>
        public static IReadOnlyList<string> SelectForTask(
            AiTaskType task,
            IReadOnlyList<AiModelOption> availableModels)
        {
            if (availableModels == null || availableModels.Count == 0)
                return Array.Empty<string>();

            var scored = availableModels
                .Select(m => (Model: m, Score: ScoreModel(m, task)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Model.Id)
                .ToList();

            return scored;
        }

        /// <summary>
        /// Merges the task-specific ordered list with the user's manually selected
        /// fallback models. In auto mode, task-ranked models come first; manually
        /// selected models are retained as an explicit fallback chain. Deduplication
        /// preserves order.
        /// </summary>
        public static IReadOnlyList<string> MergeWithUserSelection(
            IReadOnlyList<string> autoOrdered,
            IReadOnlyList<string> userSelected)
        {
            var merged = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var userIds = new HashSet<string>(
                userSelected ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Auto-selected models get first chance. Do not let a manually
            // selected model keep its old position from the full catalog: in
            // auto mode checked models are an explicit fallback, not a priority.
            foreach (var id in autoOrdered ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id)
                    && !userIds.Contains(id)
                    && seen.Add(id))
                {
                    merged.Add(id);
                }
            }

            foreach (var id in userSelected ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
                    merged.Add(id);
            }

            return merged;
        }

        /// <summary>
        /// Heuristic score for a model given a task type.
        /// Components (0–100 each): instruction-following, raw-capability, speed.
        /// </summary>
        private static double ScoreModel(AiModelOption model, AiTaskType task)
        {
            var id = model.Id?.ToLowerInvariant() ?? "";
            var name = model.DisplayName?.ToLowerInvariant() ?? "";

            // ── Base capability by model family ─────────────────
            double capability = 40; // default

            if (id.Contains("gemini-2.5-pro") || name.Contains("gemini 2.5 pro"))
                capability = 95;
            else if (id.Contains("gemini-2.5-flash") || name.Contains("gemini 2.5 flash"))
                capability = 80;
            else if (id.Contains("gemma-3-27b") || name.Contains("gemma 3 27b"))
                capability = 75;
            else if (id.Contains("gemma-4-31b") || name.Contains("gemma 4 31b"))
                capability = 78;
            else if (id.Contains("gemma-4-26b") || name.Contains("gemma 4 26b"))
                capability = 72;
            else if (id.Contains("nemotron-70b") || name.Contains("nemotron"))
                capability = 88;
            else if (id.Contains("llama-4") || name.Contains("llama 4"))
                capability = 82;
            else if (id.Contains("llama-3.3-70b") || name.Contains("llama 3.3"))
                capability = 80;
            else if (id.Contains("deepseek-v4-pro"))
                capability = 90;
            else if (id.Contains("deepseek-v4-flash"))
                capability = 70;
            else if (id.Contains("deepseek-chat") || id.Contains("deepseek-v3"))
                capability = 78;
            else if (id.Contains("qwen-2.5-72b") || name.Contains("qwen 2.5 72b"))
                capability = 76;
            else if (id.Contains("mistral-7b") || name.Contains("mistral 7b"))
                capability = 55;
            else if (id.Contains("cohere") && name.Contains("north"))
                capability = 50;

            // ── Instruction-following (JSON compliance) ────────
            double instructionScore = 50;

            // Gemini family is strong at structured outputs
            if (id.Contains("gemini") || id.Contains("gemma"))
                instructionScore = 85;
            else if (id.Contains("nemotron"))
                instructionScore = 78;
            else if (id.Contains("llama") && id.Contains("4"))
                instructionScore = 72;
            else if (id.Contains("deepseek"))
                instructionScore = 68;
            else if (id.Contains("qwen"))
                instructionScore = 65;

            // ── Speed proxy (smaller = faster heuristic) ───────
            double speedScore = 50;
            if (capability >= 85)
                speedScore = 30;   // big models are slower
            else if (capability >= 75)
                speedScore = 50;
            else if (capability >= 60)
                speedScore = 70;
            else
                speedScore = 90;   // small models are fast

            // ── Combine based on task type ──────────────────────
            return task switch
            {
                // Calculator: instruction-following and capability matter most
                AiTaskType.Calculator => instructionScore * 0.50 + capability * 0.35 + speedScore * 0.15,

                // Help: capability for explanations + speed for responsiveness
                AiTaskType.Help => capability * 0.40 + instructionScore * 0.30 + speedScore * 0.30,

                // General: balanced
                AiTaskType.General or _ => capability * 0.40 + instructionScore * 0.35 + speedScore * 0.25,
            };
        }
    }
}
