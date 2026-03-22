//Copyright Warren Harding 2025.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;

namespace OpenRouter
{
    public class Models
    {
        public async static Task<string> GetModels()
        {
            var apiUrl = "https://openrouter.ai/api/v1/models";

            using var client = new HttpClient();

            try
            {
                var response = await client.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                string json = await response.Content.ReadAsStringAsync();

                return json;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public static async Task<string> GetModelEndpoints(
            string author,
            string slug)
        {
            // Build the request URL
            string path = $"https://openrouter.ai/api/v1/models/{author}/{slug}/endpoints";  // :contentReference[oaicite:8]{index=8}

            using var client = new HttpClient();

            // Send the GET request
            using HttpResponseMessage response = await client.GetAsync(path);  // :contentReference[oaicite:10]{index=10}
            response.EnsureSuccessStatusCode();

            // Read the JSON payload
            string json = await response.Content.ReadAsStringAsync();  // :contentReference[oaicite:11]{index=11}

            return json;
        }
    }
}
