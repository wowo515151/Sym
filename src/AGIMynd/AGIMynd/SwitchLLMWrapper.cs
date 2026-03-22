//Copyright Warren Harding 2025.
using System.Threading;
using System.Threading.Tasks;
using SwitchLLM;

namespace AGIMynd
{
    public class SwitchLLMWrapper : IMyndLLM
    {
        public async Task<string> QueryAsync(string prompt, CancellationToken ct = default)
        {
            var response = await LLM.Query(prompt, ct);
            if (response.Succeeded)
            {
                return response.Result;
            }
            throw new System.Exception($"LLM Query failed: {response.Result}");
        }
    }
}
