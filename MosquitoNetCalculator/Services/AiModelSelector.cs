using System;
using System.Collections.Generic;
using System.Linq;
using MosquitoNetCalculator.Models;

namespace MosquitoNetCalculator.Services
{
    /// <summary>
    /// Automatic model selector that picks the best free models for a given task type.
    /// Scoring is heuristic-based: larger instruction-tuned models score higher for
    /// Calculator tasks; vision-capable models score highest for Vision tasks;
    /// smaller fast models are preferred for Help.
    /// Models currently in cooldown (due to 429 / 5xx / timeout) are deprioritized.
    /// </summary>
    public static class AiModelSelector
    {
        private static readonly object CooldownLock = new();
        private static readonly Dictionary<string, DateTime> ModelCooldowns = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Puts a model into temporary cooldown after a rate-limit (429), server error (5xx), or timeout.
        /// </summary>
        public static void ReportModelFailure(string modelId, int statusCode = 0)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;
            if (statusCode == 0 || statusCode == 429 || statusCode >= 500)
            {
                lock (CooldownLock)
                {
                    ModelCooldowns[modelId] = DateTime.UtcNow.Add(DefaultCooldown);
                }
            }
        }

        /// <summary>
        /// Clears cooldown for a model once it responds successfully.
        /// </summary>
        public static void ReportModelSuccess(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;
            lock (CooldownLock)
            {
                ModelCooldowns.Remove(modelId);
            }
        }

        /// <summary>
        /// Checks whether a model is currently in temporary cooldown.
        /// </summary>
        public static bool IsCooledDown(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return false;
            lock (CooldownLock)
            {
                if (ModelCooldowns.TryGetValue(modelId, out var expires))
                {
                    if (DateTime.UtcNow < expires) return true;
                    ModelCooldowns.Remove(modelId);
                }
                return false;
            }
        }

        /// <summary>
        /// Returns models ordered from best to worst for the given task.
        /// If the user selected specific models, those models form the primary pool (ranked first).
        /// Remaining available models from the catalog act as a fallback safety net.
        /// Cooled-down models are shifted to the end.
        /// </summary>
        public static IReadOnlyList<string> SelectForTask(
            AiTaskType task,
            IReadOnlyList<AiModelOption> availableModels,
            IReadOnlyList<string>? userSelected = null,
            Func<AiProvider, bool>? isProviderActive = null)
        {
            if (availableModels == null || availableModels.Count == 0)
                return Array.Empty<string>();

            // Filter out models belonging to explicitly disabled/unconfigured providers if filter is given
            var eligible = availableModels;
            if (isProviderActive != null)
            {
                eligible = availableModels.Where(m => isProviderActive(m.Provider)).ToList();
                if (eligible.Count == 0)
                    eligible = availableModels; // Fallback if none matched
            }

            var userSelectedSet = new HashSet<string>(
                userSelected?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Separate into user's preferred pool and catalog fallback
            List<AiModelOption> userPool;
            List<AiModelOption> catalogPool;

            if (userSelectedSet.Count > 0)
            {
                userPool = eligible.Where(m => userSelectedSet.Contains(m.Id)).ToList();
                catalogPool = eligible.Where(m => !userSelectedSet.Contains(m.Id)).ToList();
            }
            else
            {
                userPool = eligible.ToList();
                catalogPool = new List<AiModelOption>();
            }

            // In Vision tasks: strictly exclude non-vision models from both user pool and catalog pool
            if (task == AiTaskType.Vision)
            {
                userPool = userPool.Where(m => m.SupportsVision == true).ToList();
                // If user pool had no vision models, allow vision models from catalog pool
                if (userPool.Count == 0)
                {
                    userPool = eligible.Where(m => m.SupportsVision == true).ToList();
                }
                catalogPool = catalogPool.Where(m => m.SupportsVision == true).ToList();
            }

            // Rank both pools
            var rankedUser = RankModels(userPool, task);
            var rankedCatalog = RankModels(catalogPool, task);

            var result = new List<string>(rankedUser.Count + rankedCatalog.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in rankedUser)
            {
                if (seen.Add(id)) result.Add(id);
            }
            foreach (var id in rankedCatalog)
            {
                if (seen.Add(id)) result.Add(id);
            }

            return result;
        }

        /// <summary>
        /// Backward-compatible overload without user-selected pool.
        /// </summary>
        public static IReadOnlyList<string> SelectForTask(
            AiTaskType task,
            IReadOnlyList<AiModelOption> availableModels)
            => SelectForTask(task, availableModels, null, null);

        private static List<string> RankModels(List<AiModelOption> models, AiTaskType task)
        {
            return models
                .Select(m => new
                {
                    Model = m,
                    Score = ScoreModel(m, task),
                    CooledDown = IsCooledDown(m.Id)
                })
                .OrderBy(x => x.CooledDown ? 1 : 0) // Cooled down models go to the tail
                .ThenByDescending(x => x.Score)
                .ThenBy(x => x.Model.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Model.Id)
                .ToList();
        }

        /// <summary>
        /// Merges the task-specific ordered list with the user's manually selected
        /// fallback models. Preserved for backward compatibility.
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
        /// </summary>
        internal static double ScoreModel(AiModelOption model, AiTaskType task)
        {
            var id = model.Id?.ToLowerInvariant() ?? "";
            var name = model.DisplayName?.ToLowerInvariant() ?? "";
            bool isVision = model.SupportsVision == true || id.Contains("vision") || id.Contains("-vl") || name.Contains("vision");

            // For Vision task: models without vision capability cannot be used
            if (task == AiTaskType.Vision && !isVision)
            {
                return 0;
            }

            bool has(string term) => id.Contains(term) || name.Contains(term);

            // ── Base capability by model family ─────────────────
            double capability = 45; // default

            // Routers:
            if (string.Equals(id, "openrouter/free", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(id, "auto", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("auto:") ||
                id == "fusion")
            {
                capability = (task == AiTaskType.Vision && !isVision) ? 10 : 92;
                if (id == "auto:vision" && task == AiTaskType.Vision) capability = 96;
            }
            // Flagships / Top Reasoning & JSON models:
            else if (has("deepseek-r1") || has("deepseek-v3") || has("deepseek v3") || has("deepseek-chat") || has("deepseek r1"))
                capability = 96;
            else if (has("qwen-2.5-72b") || has("qwen 2.5 72b") || has("qwen/qwen-2.5-72b"))
                capability = 95;
            else if (has("qwen-2.5-vl") || has("qwen-vl") || has("qwen vl") || (has("qwen") && isVision))
                capability = 96; // Excellent for vision and drawings
            else if (has("gpt-4o") || has("claude-3-5-sonnet") || has("claude 3.5") || has("claude-3.5") || has("claude-sonnet") || has("claude-opus"))
                capability = 96;
            else if (has("gemini") && (has("pro") || has("flash")))
                capability = 95; // Gemini Flash and Pro are top-tier vision and OCR models
            else if (has("qwen") && (has("vl") || has("vision")))
                capability = 95; // Qwen 2.5-VL / Qwen-VL top vision capability
            else if (has("nemotron-70b") || has("nemotron 70b"))
                capability = 90;
            else if (has("llama-3.3-70b") || has("llama 3.3"))
                capability = 90;
            else if ((has("llama-3.2-90b") || has("llama-3.2") && !has("11b") && !has("3b") && !has("1b")) && isVision)
                capability = 92; // Llama 3.2 90B Vision
            else if (has("llama-3.2-11b") || (has("llama-3.2") && has("11b")))
                capability = 75; // Llama 3.2 11B is smaller and prone to table hallucinations
            else if (has("qwen-2.5-32b") || has("qwen-2.5-14b") || has("qwen 2.5 32b") || has("qwen 2.5 14b"))
                capability = 85;
            else if (has("gemma-3-27b") || has("gemma-4-31b") || has("gemma-4-26b") || has("gemma 3") || has("gemma 4"))
                capability = 80;
            else if (has("deepseek-v4-pro") || has("deepseek v4 pro"))
                capability = 90;
            else if (has("deepseek-v4-flash") || has("deepseek-v4") || has("deepseek v4"))
                capability = 75;
            else if (has("mistral-large") || has("codestral"))
                capability = 88;
            else if (has("mistral-7b") || has("mistral 7b") || has("mistral"))
                capability = 60;
            else if (has("llama-3.2-3b") || has("llama-3.2-1b") || has("llama 3.2 3b"))
                capability = 55;
            else if (has("cohere") && has("north"))
                capability = 50;

            // ── Instruction-following (JSON compliance & exact parameters) ────────
            double instructionScore = 55;

            if (string.Equals(id, "openrouter/free", StringComparison.OrdinalIgnoreCase) || id.StartsWith("auto"))
                instructionScore = 90;
            else if (has("deepseek") || has("qwen"))
                instructionScore = 94;
            else if (has("gpt-4") || has("claude"))
                instructionScore = 96;
            else if (has("gemini") || has("gemma"))
                instructionScore = 88;
            else if (has("nemotron"))
                instructionScore = 82;
            else if (has("llama-3.3") || has("llama 3.3") || has("llama-3.1") || has("llama 3.1"))
                instructionScore = 80;
            else if (has("llama-3.2") || has("llama 3.2") || has("llama"))
                instructionScore = 72;

            // ── Speed proxy (smaller/flash = faster) ───────
            double speedScore = 50;
            if (id.Contains("flash") || id.Contains("lite") || id.Contains("mini") || id.Contains("nano") || id == "auto:fast")
                speedScore = 95;
            else if (id.StartsWith("auto") || id == "openrouter/free")
                speedScore = 75;
            else if (capability >= 92)
                speedScore = 35; // flagship models are slower
            else if (capability >= 80)
                speedScore = 55;
            else
                speedScore = 80;

            // ── Combine based on task type ──────────────────────
            return task switch
            {
                // Vision: capability on visual understanding is paramount
                AiTaskType.Vision => isVision
                    ? capability * 0.60 + instructionScore * 0.30 + speedScore * 0.10 + 50
                    : 0,

                // Calculator: instruction-following (JSON schema, numbers) is #1
                AiTaskType.Calculator => instructionScore * 0.50 + capability * 0.35 + speedScore * 0.15,

                // Help: capability for explanations + speed for responsiveness
                AiTaskType.Help => capability * 0.40 + instructionScore * 0.30 + speedScore * 0.30,

                // General: balanced
                AiTaskType.General or _ => capability * 0.40 + instructionScore * 0.35 + speedScore * 0.25,
            };
        }
    }
}
