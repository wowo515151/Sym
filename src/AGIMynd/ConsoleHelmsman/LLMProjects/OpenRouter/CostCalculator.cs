// Copyright Warren Harding 2026
// Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization; // For CultureInfo.InvariantCulture
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers; // ** Use System.Timers namespace **

namespace OpenRouter
{
    /// <summary>
    /// Calculates the cost of LLM API calls based on model pricing and token usage.
    /// Fetches pricing data from the OpenRouter API and supports auto-refreshing using System.Timers.Timer.
    /// </summary>
    public static class CostCalculator
    {
        private static readonly Dictionary<string, ModelPricing> _pricingData = new();
        private static volatile bool _isInitialized = false;
        private static readonly object _lockObject = new object(); // Lock for thread safety

        // --- Auto-Refresh Fields ---
        private static System.Timers.Timer? _refreshTimer; // ** Use System.Timers.Timer **
        private static TimeSpan _refreshInterval = TimeSpan.Zero;
        private static long _isProcessingElapsed = 0; // 0 = false, 1 = true (for Interlocked)

        /// <summary>
        /// Represents the pricing details for a specific model. Costs are stored per million tokens.
        /// </summary>
        private record ModelPricing(double CostPerMillionInputTokens, double CostPerMillionOutputTokens);

        /// <summary>
        /// Asynchronously initializes the CostCalculator by fetching initial model pricing data.
        /// This should be called once during application startup before any cost calculations.
        /// </summary>
        /// <returns>True if initialization was successful, false otherwise.</returns>
        public static async Task<bool> InitializeAsync()
        {
            lock (_lockObject)
            {
                if (_isInitialized)
                {
                    Debug.WriteLine("CostCalculator already initialized.");
                    return true;
                }
            }

            //Debug.WriteLine("Initializing CostCalculator: Fetching initial model pricing data...");
            bool success = await FetchAndApplyPricingAsync();

            if (success)
            {
                lock (_lockObject)
                {
                    _isInitialized = true;
                }
                Debug.WriteLine("CostCalculator initialized successfully.");
            }
            else
            {
                Debug.WriteLine("CostCalculator initial fetch failed.");
            }
            return success;
        }

        /// <summary>
        /// Starts a background timer that periodically refreshes the pricing data.
        /// Call InitializeAsync first. Uses System.Timers.Timer.
        /// </summary>
        /// <param name="interval">How often to refresh the pricing data.</param>
        public static void StartAutoRefresh(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                Debug.WriteLine("Auto-refresh interval must be positive. Auto-refresh not started.");
                return;
            }

            if (!_isInitialized)
            {
                Debug.WriteLine("Warning: CostCalculator must be initialized with InitializeAsync() before starting auto-refresh.");
                return;
            }

            lock (_lockObject) // Lock to prevent race conditions starting/stopping
            {
                if (_refreshTimer != null)
                {
                    Debug.WriteLine($"Auto-refresh timer is already running with interval {_refreshInterval}. Stop it first to change interval.");
                    return;
                }

                _refreshInterval = interval;
                Debug.WriteLine($"Starting CostCalculator auto-refresh timer with interval: {interval}");

                _refreshTimer = new System.Timers.Timer(interval.TotalMilliseconds);
                _refreshTimer.Elapsed += RefreshTimer_Elapsed; // Hook up the event handler
                _refreshTimer.AutoReset = false; // Important: Fire only once per interval, handler will restart it
                _refreshTimer.Enabled = true;  // Start the timer
            }
        }

        /// <summary>
        /// Stops the background timer that refreshes pricing data.
        /// </summary>
        public static void StopAutoRefresh() // No longer async needed just for stopping timer
        {
            System.Timers.Timer? timerToDispose = null;

            lock (_lockObject) // Lock to prevent race conditions starting/stopping
            {
                if (_refreshTimer == null)
                {
                    Debug.WriteLine("Auto-refresh timer is not running.");
                    return;
                }

                Debug.WriteLine("Stopping CostCalculator auto-refresh timer...");
                timerToDispose = _refreshTimer;
                _refreshTimer = null; // Clear the reference

                timerToDispose.Stop(); // Stop the timer
                timerToDispose.Elapsed -= RefreshTimer_Elapsed; // Unsubscribe event handler
                timerToDispose.Dispose(); // Dispose the timer resource

                _refreshInterval = TimeSpan.Zero;
                // Reset processing flag just in case it was stuck (though unlikely with AutoReset=false)
                Interlocked.Exchange(ref _isProcessingElapsed, 0);
            }
            Debug.WriteLine("CostCalculator auto-refresh timer stopped and disposed.");
            // Note: If the Elapsed handler was executing *exactly* when Stop was called,
            // it might still run to completion after this method returns, but the timer won't restart.
        }

        /// <summary>
        /// Event handler for the refresh timer's Elapsed event.
        /// WARNING: This is async void, exceptions MUST be caught internally.
        /// </summary>
        private static async void RefreshTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            // 1. Prevent re-entrancy if handler takes longer than expected (safety measure)
            //    CompareExchange returns the *original* value. If it was 0, it sets it to 1 and returns 0.
            if (Interlocked.CompareExchange(ref _isProcessingElapsed, 1, 0) == 1)
            {
                Debug.WriteLine("RefreshTimer_Elapsed skipped: Previous execution still in progress.");
                return; // Already processing, skip this tick
            }

            try
            {
                Debug.WriteLine("Auto-refresh: Timer elapsed, starting pricing fetch...");
                // 2. Perform the actual asynchronous work
                await FetchAndApplyPricingAsync();
                Debug.WriteLine("Auto-refresh: Periodic pricing fetch completed.");
            }
            catch (Exception ex)
            {
                // 3. CRITICAL: Catch ALL exceptions to prevent the process from crashing
                Debug.WriteLine($"CRITICAL ERROR in RefreshTimer_Elapsed: {ex}. Auto-refresh might be impacted.");
                // Log exception details thoroughly here
            }
            finally
            {
                // 4. Reset the processing flag
                Interlocked.Exchange(ref _isProcessingElapsed, 0);

                // 5. Re-enable the timer for the next interval (if it hasn't been stopped/disposed)
                try
                {
                    // Use the sender or check the static field, ensuring it's not null after StopAutoRefresh might have run
                    // Checking the static field requires care due to potential race conditions if Stop is called right here.
                    // Using sender is slightly safer if available and correct type.
                    // However, accessing the static field after checking for null under lock is common.
                    var timer = _refreshTimer; // Read volatile field reference
                    if (timer != null)
                    {
                        timer.Start(); // Re-enable the timer for the next cycle
                    }
                    else
                    {
                        Debug.WriteLine("RefreshTimer_Elapsed: Timer was stopped/disposed, not restarting.");
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected if StopAutoRefresh was called concurrently
                    Debug.WriteLine("RefreshTimer_Elapsed: Timer was disposed, cannot restart.");
                }
                catch (Exception ex)
                {
                    // Catch potential errors during timer start
                    Debug.WriteLine($"Error restarting refresh timer: {ex.Message}");
                }
            }
        }


        /// <summary>
        /// Fetches pricing data from the OpenRouter API and updates the internal dictionary.
        /// (Keep this method as it was in the previous version)
        /// </summary>
        /// <returns>True if fetching and updating were successful, false otherwise.</returns>
        private static async Task<bool> FetchAndApplyPricingAsync()
        {
            // ... (Implementation remains the same as the Task.Delay version) ...
            // Fetches via Models.GetModels, Deserializes, Updates _pricingData under lock
            string jsonResponse;
            try
            {
                jsonResponse = await Models.GetModels();
                if (string.IsNullOrWhiteSpace(jsonResponse) || jsonResponse.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"Error: Failed to get models from OpenRouter API. Response: {jsonResponse}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: Could not fetch model data for CostCalculator update. Exception: {ex}");
                return false;
            }

            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var modelListResponse = JsonSerializer.Deserialize<OpenRouterModelListResponse>(jsonResponse, options);

                if (modelListResponse?.Data == null)
                {
                    Debug.WriteLine("Error: Failed to deserialize model list response or data is null during update.");
                    return false;
                }

                var newPricingData = new Dictionary<string, ModelPricing>();
                int loadedCount = 0;
                foreach (var modelInfo in modelListResponse.Data)
                {
                    // ... (parsing logic same as before) ...
                    if (modelInfo.Id != null && modelInfo.Pricing != null &&
                       !string.IsNullOrWhiteSpace(modelInfo.Pricing.Prompt) &&
                       !string.IsNullOrWhiteSpace(modelInfo.Pricing.Completion))
                    {
                        if (double.TryParse(modelInfo.Pricing.Prompt, NumberStyles.Any, CultureInfo.InvariantCulture, out double promptCost) &&
                            double.TryParse(modelInfo.Pricing.Completion, NumberStyles.Any, CultureInfo.InvariantCulture, out double completionCost))
                        {
                            newPricingData[modelInfo.Id] = new ModelPricing(promptCost * 1000000, completionCost * 1000000);
                            loadedCount++;
                        }
                        else { /* Log warning */ }
                    }
                }

                lock (_lockObject)
                {
                    _pricingData.Clear();
                    foreach (var kvp in newPricingData) { _pricingData.Add(kvp.Key, kvp.Value); }
                }

                Debug.WriteLine($"CostCalculator pricing updated. Loaded pricing for {loadedCount} models.");
                return true;
            }
            catch (JsonException jsonEx)
            {
                Debug.WriteLine($"Error deserializing model pricing data during update: {jsonEx.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An unexpected error occurred during CostCalculator pricing update: {ex}");
                return false;
            }
        }


        /// <summary>
        /// Calculates the estimated cost for an API call based on currently loaded pricing data.
        /// (Keep this method as it was)
        /// </summary>
        // ... CalculateCost method remains the same ...
        public static double CalculateCost(string modelKey, LLM.Usage usage)
        {
            if (!_isInitialized)
            {
                Debug.WriteLine("Warning: CostCalculator.CalculateCost called before successful initialization. Returning 0 cost.");
                return 0.0;
            }

            if (usage == null) { return 0.0; }

            if (_pricingData.TryGetValue(modelKey, out var pricing))
            {
                double inputCost = (usage.PromptTokens / 1_000_000.0) * pricing.CostPerMillionInputTokens;
                double outputCost = (usage.CompletionTokens / 1_000_000.0) * pricing.CostPerMillionOutputTokens;
                return inputCost + outputCost;
            }
            else
            {
                Debug.WriteLine($"Warning: Pricing data not found for model '{modelKey}'. Returning 0 cost.");
                return 0.0;
            }
        }


        // --- Helper classes for deserializing the /v1/models response ---
        // (Keep these as they were before)
        // ... OpenRouterModelListResponse, OpenRouterModelInfo, OpenRouterPricingInfo ...
        private class OpenRouterModelListResponse { [JsonPropertyName("data")] public List<OpenRouterModelInfo>? Data { get; set; } }
        private class OpenRouterModelInfo { [JsonPropertyName("id")] public string? Id { get; set; } [JsonPropertyName("pricing")] public OpenRouterPricingInfo? Pricing { get; set; } }
        private class OpenRouterPricingInfo { [JsonPropertyName("prompt")] public string? Prompt { get; set; } [JsonPropertyName("completion")] public string? Completion { get; set; } }
    }
}