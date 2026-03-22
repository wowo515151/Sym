// Copyright Warren Harding 2026
// Copyright Warren Harding 2024.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace OpenAILLM
{
    public class LLM
    {
        public enum ReasoningEffortLevel
        {
            Low,
            Medium,
            High
        }

        public static double AccumulatedCost = 0;

        /// <summary>
        /// The PID of the process used for the last LLM call attempt.
        /// </summary>
        public static int? LastPid { get; private set; } = null;

        /// <summary>
        /// If set to true, will kill the process on timeout. Default is false.
        /// </summary>
        public static bool KillOnTimeout { get; set; } = false;

        /// <summary>
        /// Timeout for LLM calls. Default is 30 seconds.
        /// </summary>
        public static TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);


        public static string OpenAiKeyPath { get; set; } = string.Empty;

        /// <summary>
        /// Calls the LLM API, ensuring a timeout and robust error reporting.
        /// </summary>
        public static async Task<string> Query(
            string query,
            string modelKey,
            ReasoningEffortLevel? level,
            CancellationToken ct = default
        )
        {
            string apiKey = File.ReadAllText(OpenAiKeyPath).Trim();
            int? pid = null;
            try
            {
                pid = Process.GetCurrentProcess().Id;
                LastPid = pid;
            }
            catch { /* ignore */ }

            try
            {
                string combo = Common.WrapInTags(await Common.GetPrompt("SystemPrompt"), "SystemPrompt") + Environment.NewLine + Environment.NewLine + query;

                return await QueryResponsesApiAsync(apiKey, modelKey, combo, level, enableWebSearch: false, pid, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return $"Error calling OpenAI LLM (PID={pid}): {ex.Message}";
            }
        }

        /// <summary>
        /// Calls the LLM API with web search, ensuring a timeout and robust error reporting.
        /// </summary>
        public static async Task<string> SearchAsync(
            string query,
            string modelKey,
            CancellationToken ct = default
        )
        {
            string apiKey = File.ReadAllText(OpenAiKeyPath).Trim();
            int? pid = null;
            try
            {
                pid = Process.GetCurrentProcess().Id;
                LastPid = pid;
            }
            catch { /* ignore */ }

            try
            {
                return await QueryResponsesApiAsync(apiKey, modelKey, query, level: null, enableWebSearch: true, pid, ct);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return $"Error calling OpenAI LLM (PID={pid}): {ex.Message}";
            }
        }

        private static async Task<string> QueryResponsesApiAsync(
            string apiKey,
            string modelKey,
            string input,
            ReasoningEffortLevel? level,
            bool enableWebSearch,
            int? pid,
            CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var requestBody = new
            {
                model = modelKey,
                input,
                reasoning = level is null ? null : new { effort = level.Value.ToString().ToLowerInvariant() },
                tools = enableWebSearch ? new object[] { new { type = "web_search_preview" } } : null
            };

            var json = JsonSerializer.Serialize(
                requestBody,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var llmTask = http.PostAsync("https://api.openai.com/v1/responses", content, ct);

            var completedTask = await Task.WhenAny(llmTask, Task.Delay(Timeout, ct));
            if (completedTask != llmTask)
            {
                HandleTimeout(pid);
                return $"Error: LLM call timed out after {Timeout.TotalSeconds} seconds. PID={pid}.";
            }

            using var response = await llmTask;
            var responseJson = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                return $"Error calling OpenAI Responses API (PID={pid}): HTTP {(int)response.StatusCode} {response.ReasonPhrase}. {responseJson}";
            }

            return ExtractTextFromResponsesApiJson(responseJson);
        }

        private static string ExtractTextFromResponsesApiJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
                {
                    return outputText.GetString() ?? string.Empty;
                }

                if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
                {
                    return json;
                }

                var sb = new StringBuilder();
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        {
                            sb.Append(text.GetString());
                        }
                    }
                }

                var result = sb.ToString();
                return string.IsNullOrWhiteSpace(result) ? json : result;
            }
            catch
            {
                return json;
            }
        }

        private static void HandleTimeout(int? pid)
        {
            if (KillOnTimeout && pid.HasValue)
            {
                try
                {
                    var proc = Process.GetProcessById(pid.Value);
                    proc.Kill();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not kill process {pid}: {ex}");
                }
            }
        }

        public static double Cost(double inputTokenCount, double outputTokenCount, string modelKey)
        {
            // Prices are in USD per million tokens
            (double promptPerM, double completionPerM) = modelKey switch
            {
                // — GPT‑4 (8K context)
                "gpt-4.1" or "gpt-4.1-2025-04-14"
                    => (2.00, 8.00),            // :contentReference[oaicite:0]{index=0}

                // — GPT‑4.1 mini (everyday tasks)
                "gpt-4.1-mini" or "gpt-4-mini" or "gpt-4.1-mini-2025-04-14"
                    => (0.40, 1.60),            // :contentReference[oaicite:1]{index=1}

                // — GPT‑4.1 nano (low‑latency)
                "gpt-4.1-nano" or "gpt-4-nano" or "gpt-4.1-nano-2025-04-14"
                    => (0.10, 0.40),            // :contentReference[oaicite:2]{index=2}

                // — OpenAI o3 (powerful reasoning)
                "o3"
                    => (10.00, 40.00),          // :contentReference[oaicite:3]{index=3}

                // — OpenAI o4‑mini (fast, cost‑efficient reasoning)
                "o4-mini" or "o4-mini-2025-04-16"
                    => (1.10, 4.40),            // :contentReference[oaicite:4]{index=4}

                // — GPT‑4o (multimodal omni)
                "gpt-4o" or "gpt-4o-2024-05-13"
                    => (5.00, 20.00),           // :contentReference[oaicite:5]{index=5}

                // — GPT‑4o mini (multimodal preview)
                "gpt-4o-mini" or "gpt-4o-mini-2024-05-13"
                    => (0.60, 2.40),            // :contentReference[oaicite:6]{index=6}

                // — GPT‑3.5‑Turbo (dialogue‑optimized; new model gpt‑3.5‑turbo‑0125)
                "gpt-3.5-turbo" or "gpt-3.5-turbo-0125"
                    => (0.50, 1.50),            // :contentReference[oaicite:7]{index=7}

                // “Search preview” multimodal research preview
                "gpt-4o-search-preview"
                    => (2.50, 10.00),           // :contentReference[oaicite:5]{index=8}

                // “Computer preview” (code‑interpreter preview)
                "gpt-4o-computer-preview"
                    => (3.00, 12.00),           // :contentReference[oaicite:6]{index=9}

                _ => throw new NotSupportedException(
                         $"No pricing data for model '{modelKey}'.")
            };

            const double oneMillion = 1_000_000d;
            double cost =
                  (inputTokenCount * promptPerM) / oneMillion
                + (outputTokenCount * completionPerM) / oneMillion;

            return Math.Round(cost, 6);
        }
    }
}