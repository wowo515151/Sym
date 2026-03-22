// Copyright Warren Harding 2026
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AGIMynd.Tests
{
    public class MockLLM : IMyndLLM
    {
        public Func<string, string>? ResponseFunc { get; set; }

        public Task<string> QueryAsync(string prompt, CancellationToken ct = default)
        {
            if (ResponseFunc != null)
            {
                return Task.FromResult(ResponseFunc(prompt));
            }
            return Task.FromResult(AGIMynd.Common.ToXml(new AGIMynd.ToolCommandList()));
        }
    }
}
