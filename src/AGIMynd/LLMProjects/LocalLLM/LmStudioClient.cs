//Copyright Warren Harding 2025.
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LocalLLM
{
    public class LmStudioClient
    {
        private readonly HttpClient _http;
        private readonly Uri _chatCompletions;
        private readonly string _model;

        // Default knobs
        private const int DefaultCallTimeoutSeconds = 300; // per-call timeout
        private const int MaxRetries = 0;                 // total attempts = 1 + MaxRetries
        private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);

        public LmStudioClient(
            string baseUrl = "http://localhost:1234/v1", // NOTE: include /v1 here
            string model = "openai/gpt-oss-20b",
            string? apiKey = null,
            TimeSpan? timeout = null)
        {
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            // We prefer controlling timeouts per-call, so keep HttpClient “infinite” unless caller overrides
            _http = new HttpClient
            {
                Timeout = timeout ?? Timeout.InfiniteTimeSpan
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            _chatCompletions = new Uri(new Uri(baseUrl), "chat/completions");
            _model = model;
        }

        /// <summary>
        /// Queries LM Studio's /v1/chat/completions. Adds per-call timeout, retries, and clear error strings.
        /// Returns either model text or a string starting with "API Error:" describing the problem.
        /// </summary>
        public async Task<string> QueryAsync(
            string prompt,
            CancellationToken ct = default,
            int? callTimeoutSeconds = null,
            double temperature = 0.7,
            int? maxTokens = 32000)
        {
            // Build request body (OpenAI-compatible)
            var reqObj = new
            {
                model = _model,
                messages = new object[]
                {
                    new { role = "system", content = "You are a helpful assistant." },
                    new { role = "user",   content = prompt }
                },
                temperature = temperature,
                max_tokens = maxTokens,
                stream = false
            };

            var json = JsonSerializer.Serialize(reqObj);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // Per-call timeout via linked CTS (separate from HttpClient.Timeout)
            var perCallTimeout = TimeSpan.FromSeconds(callTimeoutSeconds ?? DefaultCallTimeoutSeconds);

            int attempt = 0;
            while (true)
            {
                attempt++;

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(perCallTimeout);

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.PostAsync(_chatCompletions, content, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Caller requested cancellation
                    return "API Error: UserCanceled";
                }
                catch (TaskCanceledException)
                {
                    // Timeout or internal cancellation (not user cancel)
                    if (attempt <= MaxRetries + 1)
                    {
                        await Task.Delay(Jitter(attempt), CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    return "API Error: Timeout (LM Studio request exceeded per-call limit)";
                }
                catch (HttpRequestException ex)
                {
                    // Connection/DNS/refused, etc.
                    if (attempt <= MaxRetries + 1)
                    {
                        await Task.Delay(Jitter(attempt), CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    return $"API Error: ConnectFailed ({ex.Message})";
                }

                string raw;
                try
                {
                    raw = await resp.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return "API Error: UserCanceled";
                }
                catch (TaskCanceledException)
                {
                    if (attempt <= MaxRetries + 1)
                    {
                        await Task.Delay(Jitter(attempt), CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    return "API Error: Timeout (reading response)";
                }
                catch (Exception ex)
                {
                    if (attempt <= MaxRetries + 1)
                    {
                        await Task.Delay(Jitter(attempt), CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }
                    return $"API Error: ReadFailed ({ex.Message})";
                }

                // Retry on transient HTTP codes
                if (!resp.IsSuccessStatusCode)
                {
                    if (IsTransient(resp.StatusCode) && attempt <= MaxRetries + 1)
                    {
                        await Task.Delay(Jitter(attempt), CancellationToken.None).ConfigureAwait(false);
                        continue;
                    }

                    // Surface server payload if available (LM Studio often returns { "error": "..."} or detailed bodies)
                    return $"API Error: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {raw}";
                }

                // Parse OpenAI-ish response
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;

                    // Standard path: choices[0].message.content
                    if (root.TryGetProperty("choices", out var choices) &&
                        choices.ValueKind == JsonValueKind.Array &&
                        choices.GetArrayLength() > 0)
                    {
                        var c0 = choices[0];

                        if (c0.TryGetProperty("message", out var msg) &&
                            msg.TryGetProperty("content", out var contentEl) &&
                            contentEl.ValueKind == JsonValueKind.String)
                            return contentEl.GetString() ?? string.Empty;

                        // Some servers return 'text'
                        if (c0.TryGetProperty("text", out var textEl) &&
                            textEl.ValueKind == JsonValueKind.String)
                            return textEl.GetString() ?? string.Empty;
                    }

                    // Error object?
                    if (root.TryGetProperty("error", out var errObj))
                    {
                        if (errObj.ValueKind == JsonValueKind.Object &&
                            errObj.TryGetProperty("message", out var em) &&
                            em.ValueKind == JsonValueKind.String)
                            return "API Error: " + em.GetString();

                        return "API Error: " + errObj.ToString();
                    }

                    // Unexpected shape
                    return "API Error: Unexpected response shape from /v1/chat/completions: " + raw;
                }
                catch (JsonException jex)
                {
                    return "API Error: InvalidJson (" + jex.Message + ")";
                }
            }
        }

        private static bool IsTransient(HttpStatusCode code) =>
            code == HttpStatusCode.RequestTimeout        // 408
            || code == (HttpStatusCode)429               // 429 Too Many Requests
            || ((int)code >= 500 && (int)code <= 599);   // 5xx

        private static TimeSpan Jitter(int attempt)
        {
            // simple decorrelated jitter backoff
            var rand = new Random(unchecked(Environment.TickCount * 397) ^ attempt);
            var ms = BaseBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1);
            ms = Math.Min(ms, 8000); // cap at 8s
            ms = ms * (0.7 + 0.6 * rand.NextDouble()); // 70-130%
            return TimeSpan.FromMilliseconds(ms);
        }
    }
}
