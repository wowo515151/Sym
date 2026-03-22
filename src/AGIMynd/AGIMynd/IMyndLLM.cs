//Copyright Warren Harding 2025.
using System.Threading;
using System.Threading.Tasks;

namespace AGIMynd
{
    public interface IMyndLLM
    {
        Task<string> QueryAsync(string prompt, CancellationToken ct = default);
    }
}
