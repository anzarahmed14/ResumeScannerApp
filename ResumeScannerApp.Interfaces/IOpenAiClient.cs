using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Interfaces
{
    /// <summary>
    /// Minimal abstraction to call an LLM service and return model-generated JSON string.
    /// </summary>
    public interface IOpenAiClient
    {
        Task<string?> GetStructuredJsonAsync(string apiKey, string prompt, int maxPromptLength = 50000, CancellationToken cancellationToken = default);
    }
}
