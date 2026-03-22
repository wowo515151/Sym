//Copyright Warren Harding 2025.
// Copyright Warren Harding 2025.
using System;
using System.Diagnostics; // For logging and Stopwatch
using System.IO;         // Explicitly added for File
using System.Linq;
using System.Net.Http;   // Explicitly added for HttpClient etc.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;  // Added for CancellationToken
using System.Threading.Tasks;

namespace OpenRouter
{
    public static class LLM // Made static as all members were static
    {
        public static string KeyPath { get; set; } = "";
        private static HttpClient? _httpClient;
        private static string _apiKey = ""; // Store the key after reading once

        // Buffer to add to HttpClient.Timeout for our internal watchdog
        private static readonly TimeSpan _internalWatchdogBuffer = TimeSpan.FromSeconds(5);

        public static void Initialize()
        {
            try
            {
                _apiKey = File.ReadAllText(KeyPath).Trim();
                if (string.IsNullOrWhiteSpace(_apiKey))
                {
                    Debug.WriteLine("WARNING: API Key loaded from file is empty.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FATAL ERROR: Could not read API Key from {KeyPath}. Exception: {ex}");
                _apiKey = ""; // Ensure key is empty if load failed
            }

            _httpClient = new HttpClient()
            {
                // This is the primary timeout for HTTP operations
                Timeout = TimeSpan.FromSeconds(300)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            Debug.WriteLine($"LLM Initialized. HttpClient Timeout set to: {_httpClient.Timeout.TotalSeconds}s. Internal watchdog buffer: {_internalWatchdogBuffer.TotalSeconds}s.");
        }

        public async static Task<(bool Success, string ResponseText, double Cost)> Query(
            string prompt,
            string modelKey,
            string provider,
            CancellationToken externalCancellationToken = default)
        {
            if (_httpClient == null)
            {
                Debug.WriteLine("Error: LLM not initialized. Call LLM.Initialize() first.");
                return (false, "LLM not initialized.", double.NaN);
            }
            if (string.IsNullOrWhiteSpace(_apiKey))
            {
                Debug.WriteLine("Error: API Key is not loaded or is empty. Please check KeyPath and Initialize().");
                return (false, "API Key not loaded. Check initial configuration and log files.", double.NaN);
            }

            var operationId = Guid.NewGuid().ToString("N").Substring(0, 8); // For correlating log messages

            // Calculate the duration for our internal watchdog timer
            // It will be HttpClient.Timeout + a small buffer.
            // This assumes _httpClient.Timeout is a finite, positive TimeSpan (which it is from Initialize).
            TimeSpan internalWatchdogDuration = _httpClient.Timeout + _internalWatchdogBuffer;

            Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Starting Query. Model: {modelKey}. ExternalTokenCanBeCancelled: {externalCancellationToken.CanBeCanceled}. InternalWatchdogTimeout: {internalWatchdogDuration.TotalSeconds}s (HttpClientTimeout: {_httpClient.Timeout.TotalSeconds}s)");

            // This CancellationTokenSource is for our internal watchdog.
            using var internalWatchdogCts = new CancellationTokenSource(internalWatchdogDuration);

            // Link the external token (if any) with our internal watchdog token.
            // The operation will be cancelled if EITHER the external token is cancelled OR our watchdog timeout is reached.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                                      externalCancellationToken,
                                      internalWatchdogCts.Token);

            var requestStopwatch = Stopwatch.StartNew(); // Time the entire operation
            try
            {
                var url = "https://openrouter.ai/api/v1/chat/completions";
                var requestBody = new
                {
                    model = modelKey,
                    reasoning = new { effort = "high", enabled = true },
                    messages = new[]
                    {
                        //new { role = "system", content = GetPrompt("SystemPrompt") }, // Can throw if file not found
                        new { role = "user", content = prompt }
                    },
                };
                var json = JsonSerializer.Serialize(requestBody);

                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Sending PostAsync to {url}.");
                    var postStopwatch = Stopwatch.StartNew();

                    // Use the linkedCts.Token for the HTTP call.
                    // HttpClient.Timeout will still apply. If it's shorter than internalWatchdogDuration's remaining time,
                    // it will likely trigger the TaskCanceledException first.
                    var response = await _httpClient.PostAsync(url, content, linkedCts.Token).ConfigureAwait(false);

                    postStopwatch.Stop();
                    Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] PostAsync completed in {postStopwatch.ElapsedMilliseconds}ms. Status: {response.StatusCode}.");

                    Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Reading response content.");
                    var readStopwatch = Stopwatch.StartNew();

                    // Also use linkedCts.Token for reading the content if the PostAsync completed.
                    // (Requires .NET 7+ for ReadAsStringAsync(CancellationToken), your original code used it)
                    string responseContent = await response.Content.ReadAsStringAsync(linkedCts.Token).ConfigureAwait(false);

                    readStopwatch.Stop();
                    Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] ReadAsStringAsync completed in {readStopwatch.ElapsedMilliseconds}ms. Content length: {responseContent.Length}.");

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Error: OpenRouter API returned status code {response.StatusCode}. Response: {responseContent}");
                        return (false, $"API Error: {response.StatusCode}. Details: {responseContent}", double.NaN);
                    }

                    ChatCompletionResponse? chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseContent);
                    if (chatResponse?.Choices?.Any() == true && chatResponse.Choices[0].Message?.Content != null)
                    {
                        double calculatedCost = 0.0;
                        if (chatResponse.Usage != null)
                        {
                            calculatedCost = CostCalculator.CalculateCost(modelKey, chatResponse.Usage);
                        }
                        else
                        {
                            Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Warning: Usage data not present in response for {modelKey}. Cannot calculate cost.");
                        }
                        requestStopwatch.Stop();
                        Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Query successful in {requestStopwatch.ElapsedMilliseconds}ms.");
                        return (true, chatResponse.Choices[0].Message.Content, calculatedCost);
                    }
                    else
                    {
                        Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] Error: Failed to deserialize response or response structure unexpected. Content: {responseContent}");
                        return (false, "Failed to deserialize response or response structure was unexpected. Content:" + responseContent, double.NaN);
                    }
                }
            }
            catch (TaskCanceledException cancelEx) // Catches cancellations from external token, internal watchdog, OR HttpClient.Timeout
            {
                requestStopwatch.Stop();
                string reason;
                if (externalCancellationToken.IsCancellationRequested)
                {
                    reason = "cancelled by caller";
                }
                else if (internalWatchdogCts.IsCancellationRequested) // Our internal watchdog was hit
                {
                    reason = $"timed out by internal query watchdog ({internalWatchdogDuration.TotalSeconds}s)";
                }
                else // Implies HttpClient.Timeout was hit before our (slightly longer) internal watchdog,
                     // or another TaskCanceledException scenario not directly from our primary controlled tokens.
                {
                    reason = $"timed out by HttpClient.Timeout ({_httpClient.Timeout.TotalSeconds}s) or other cancellation";
                }
                Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] TaskCanceledException after {requestStopwatch.ElapsedMilliseconds}ms. Reason: {reason}. Exception Message: {cancelEx.Message}");
                Debug.WriteLine($"Stack Trace: {cancelEx.StackTrace}");
                return (false, $"Operation {reason}. Details: {cancelEx.Message}", double.NaN);
            }
            catch (HttpRequestException httpEx)
            {
                requestStopwatch.Stop();
                Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] HttpRequestException after {requestStopwatch.ElapsedMilliseconds}ms: {httpEx.Message}. Status Code (if available): {httpEx.StatusCode}");
                Debug.WriteLine($"Stack Trace: {httpEx.StackTrace}");
                return (false, $"Network or HTTP error: {httpEx.Message}", double.NaN);
            }
            catch (JsonException jsonEx)
            {
                requestStopwatch.Stop();
                Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] JsonException after {requestStopwatch.ElapsedMilliseconds}ms during response processing: {jsonEx.Message}");
                Debug.WriteLine($"Path: {jsonEx.Path}, Line: {jsonEx.LineNumber}, Position: {jsonEx.BytePositionInLine}");
                Debug.WriteLine($"Stack Trace: {jsonEx.StackTrace}");
                return (false, $"Failed to parse JSON response: {jsonEx.Message}", double.NaN);
            }
            catch (FileNotFoundException fileEx) // Catching specific exception from GetPrompt
            {
                requestStopwatch.Stop();
                Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] FileNotFoundException after {requestStopwatch.ElapsedMilliseconds}ms: {fileEx.Message}");
                Debug.WriteLine($"Stack Trace: {fileEx.StackTrace}");
                return (false, $"Failed to load prompt file: {fileEx.Message}", double.NaN);
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                requestStopwatch.Stop();
                Debug.WriteLine($"[{DateTime.UtcNow:O}] [ID:{operationId}] An unexpected error occurred after {requestStopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                return (false, $"An unexpected error occurred: {ex.Message}", double.NaN);
            }
        }

        // --- Helper Classes and Methods (ChatCompletionResponse, Choice, Message, Usage) ---
        // (These would be the same as in your original code)
        public class ChatCompletionResponse
        { /* ... as before ... */
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("provider")] public string Provider { get; set; } = "";
            [JsonPropertyName("model")] public string Model { get; set; } = "";
            [JsonPropertyName("object")] public string ObjectType { get; set; } = "";
            [JsonPropertyName("created")] public long Created { get; set; }
            [JsonPropertyName("choices")] public Choice[] Choices { get; set; } = Array.Empty<Choice>();
            [JsonPropertyName("usage")] public Usage Usage { get; set; } = new Usage();
        }
        public class Choice
        { /* ... as before ... */
            [JsonPropertyName("logprobs")] public object? Logprobs { get; set; }
            [JsonPropertyName("finish_reason")] public string FinishReason { get; set; } = "";
            [JsonPropertyName("native_finish_reason")] public string NativeFinishReason { get; set; } = "";
            [JsonPropertyName("index")] public int Index { get; set; }
            [JsonPropertyName("message")] public Message Message { get; set; } = new Message();
        }
        public class Message
        { /* ... as before ... */
            [JsonPropertyName("role")] public string Role { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
            [JsonPropertyName("refusal")] public object? Refusal { get; set; }
        }
        public class Usage
        { /* ... as before ... */
            [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
            [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
            [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
        }

        public static string GetPrompt(string name)
        {
            return GetPrompt("Prompts", name);
        }

        public static string GetPrompt(string folderName, string name)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName, name + ".txt");
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
            // Throw a more specific exception if prompt file is not found
            Debug.WriteLine($"Error: Prompt file not found at {path} for prompt name '{name}' in folder '{folderName}'.");
            throw new FileNotFoundException($"Prompt file '{name}.txt' not found in folder '{folderName}'. Full path: {path}", path);
        }
    }
}
