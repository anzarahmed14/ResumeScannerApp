using ResumeScannerApp.Config;
using ResumeScannerApp.Helpers;
using ResumeScannerApp.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ResumeScannerApp.Services
{
    public class AzureOpenAiService : IOpenAiClient
    {
        private readonly HttpClient _http;
        private readonly AzureOpenAiOptions _options;

        public AzureOpenAiService(HttpClient http, AzureOpenAiOptions options)
        {
            _http = http;
            _options = options;
        }

        public async Task<string?> GetStructuredJsonAsync(string apiKey, string prompt, int maxPromptLength = 50000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            if (prompt.Length > maxPromptLength) prompt = prompt.Substring(0, maxPromptLength);

            var endpoint = _options.Endpoint?.TrimEnd('/') ?? throw new InvalidOperationException("Endpoint not configured.");
            var deployment = _options.DeploymentName ?? throw new InvalidOperationException("DeploymentName not configured.");
            var apiVersion = _options.ApiVersion ?? "2024-02-01";

            // Use chat completions (same pattern as your working TestConnection)
            var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

            // Build an instruction as the assistant/system message (model returns content under choices[0].message.content)
            var instruction = $@"
You are a precise resume parser. Input is raw resume text. Return ONLY a single valid JSON object (no explanation).
Schema:
{{ ""file_name"": ""string or null"", ""name"": ""string or null"", ""email"": ""string or null"", ""phone"": ""string or null"", ""skills"": [""string""], ""total_years_experience"": integer or null, ""summary"": ""string or null"" }}
Now parse the resume below and output only the JSON object.

----RESUME----
{prompt}
----END----
";

            // Chat completions payload
            var payload = new
            {
                model = "", // leave empty or omit if your deployment maps to the model already; Azure often ignores model here when using deployment path
                messages = new[]
                {
            new { role = "system", content = "You are a helpful assistant that responds exactly with the requested JSON." },
            new { role = "user", content = instruction }
        },
                // you can tune these if you want deterministic output
                max_tokens = 1024,
                temperature = 0.0
            };

            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            req.Headers.Add("api-key", apiKey);

            using var resp = await _http.SendAsync(req, cancellationToken);
            // If you prefer not to throw, remove EnsureSuccessStatusCode and handle non-success manually
            resp.EnsureSuccessStatusCode();

            var respText = await resp.Content.ReadAsStringAsync(cancellationToken);

            // Try to parse JSON from the assistant message if response is in the standard chat-completions shape:
            // { "choices":[ { "message": { "role":"assistant", "content": "..." }, ... } ], ... }
            try
            {
                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var messageElement) &&
                        messageElement.TryGetProperty("content", out var contentElement))
                    {
                        var assistantText = contentElement.GetString();
                        if (!string.IsNullOrWhiteSpace(assistantText))
                        {
                            // Extract first JSON substring from the assistant text (your JsonExtractor helper)
                            var extracted = JsonExtractor.ExtractFirstJsonSubstring(assistantText);
                            if (!string.IsNullOrWhiteSpace(extracted)) return extracted.Trim();
                            // fallback: if entire assistantText looks like JSON, return it
                            return assistantText.Trim();
                        }
                    }
                    // Some deployments return "text" instead of message.content (older / custom proxies). Try choices[0].text
                    if (firstChoice.TryGetProperty("text", out var textElem))
                    {
                        var assistantText = textElem.GetString();
                        var extracted = JsonExtractor.ExtractFirstJsonSubstring(assistantText ?? "");
                        if (!string.IsNullOrWhiteSpace(extracted)) return extracted.Trim();
                        return assistantText?.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // ignore parsing error and fall back to raw text extraction below
            }

            // If the response wasn't the standard chat-completions JSON (or parsing failed),
            // attempt to extract the first JSON object from the full response text.
            var fallbackExtract = JsonExtractor.ExtractFirstJsonSubstring(respText ?? "");
            return string.IsNullOrWhiteSpace(fallbackExtract) ? null : fallbackExtract.Trim();
        }


        public async Task<string?> GetStructuredJsonAsync4(string apiKey, string prompt, int maxPromptLength = 50000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            if (prompt.Length > maxPromptLength) prompt = prompt.Substring(0, maxPromptLength);

            // Normalize endpoint setting
            var endpoint = (_options.Endpoint ?? "").TrimEnd('/');
            if (string.IsNullOrEmpty(endpoint)) throw new InvalidOperationException("Endpoint is not configured in _options.");

            // Build instruction for model
            var instruction = $@"
You are a precise resume parser. Input is raw resume text. Return ONLY a single valid JSON object (no explanation).
Schema:
{{ ""file_name"": ""string or null"", ""name"": ""string or null"", ""email"": ""string or null"", ""phone"": ""string or null"", ""skills"": [""string""], ""total_years_experience"": integer or null, ""summary"": ""string or null"" }}
Now parse the resume below and output only the JSON object.

----RESUME----
{prompt}
----END----
".Trim();

            // Heuristic: treat endpoint as proxy if path contains '/api/azureai' OR host is not *.openai.azure.com
            bool LooksLikeProxy()
            {
                try
                {
                    var uri = new Uri(endpoint);
                    // common proxy shape contains '/api/azureai' or is hosted on azurewebsites / custom hostname
                    if (endpoint.Contains("/api/azureai", StringComparison.OrdinalIgnoreCase)) return true;
                    if (!uri.Host.EndsWith(".openai.azure.com", StringComparison.OrdinalIgnoreCase)) return true;
                    return false;
                }
                catch
                {
                    // If parsing fails, assume proxy to be safe
                    return true;
                }
            }

            // HTTP helper with small retry for transient conditions
            async Task<(HttpResponseMessage resp, string body)> SendWithRetriesAsync(HttpRequestMessage req)
            {
                const int maxAttempts = 3;
                for (int attempt = 1; ; attempt++)
                {
                    try
                    {
                        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                        var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        return (resp, body);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception) when (attempt < maxAttempts)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken).ConfigureAwait(false);
                        // loop to retry
                    }
                }
            }

            // Try proxy-style (Bearer + messages) if endpoint looks like proxy
            if (LooksLikeProxy())
            {
                var proxyUrl = endpoint;
                // If endpoint is resource root but you still want to call /api/azureai, ensure path
                if (!proxyUrl.EndsWith("/api/azureai", StringComparison.OrdinalIgnoreCase) && proxyUrl.IndexOf("/api/azureai", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Append if root provided (many configs store base URL)
                    proxyUrl = proxyUrl.TrimEnd('/') + "/api/azureai";
                }

                var proxyPayload = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                new { role = "system", content = "You are a precise resume parser. Return ONLY a single JSON object following the schema." },
                new { role = "user", content = instruction }
            },
                    max_tokens = 1000
                };

                var proxyJson = JsonSerializer.Serialize(proxyPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                using var req = new HttpRequestMessage(HttpMethod.Post, proxyUrl)
                {
                    Content = new StringContent(proxyJson, Encoding.UTF8, "application/json")
                };
                // Proxy expects Bearer token per company guide
                if (!string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.UserAgent.ParseAdd("ResumeScannerClient/1.0");

                var (resp, bodyText) = await SendWithRetriesAsync(req).ConfigureAwait(false);

                // If 405 on proxy, surface helpful error
                if (resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    var allow = resp.Headers.TryGetValues("Allow", out var v) ? string.Join(",", v) : "N/A";
                    throw new HttpRequestException($"Proxy endpoint returned 405 Method Not Allowed. Allow: {allow}. Body: {bodyText}");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    // transient codes already retried; give diagnostic
                    throw new HttpRequestException($"Proxy endpoint returned {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {bodyText}");
                }

                // Extract JSON from proxy response
                var extracted = JsonExtractor.ExtractFirstJsonSubstring(bodyText);
                return string.IsNullOrEmpty(extracted) ? bodyText : extracted;
            }
            else
            {
                // Direct Azure OpenAI resource path (responses) — use api-key header
                var url = $"{endpoint}/openai/deployments/{_options.DeploymentName}/responses?api-version={_options.ApiVersion}";

                var body = new { input = instruction };
                var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrWhiteSpace(apiKey))
                    req.Headers.Add("api-key", apiKey);

                req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                req.Headers.UserAgent.ParseAdd("ResumeScannerClient/1.0");

                var (resp, bodyText) = await SendWithRetriesAsync(req).ConfigureAwait(false);

                if (resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    var allow = resp.Headers.TryGetValues("Allow", out var v) ? string.Join(",", v) : "N/A";
                    // Helpful message: suggest calling proxy-style instead
                    throw new HttpRequestException($"Azure OpenAI returned 405 Method Not Allowed. Allow: {allow}. Body: {bodyText}");
                }

                if (!resp.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Azure OpenAI returned {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {bodyText}");
                }

                // Try to parse JSON-rich shapes first (some responses are nested), else extract substring
                try
                {
                    using var doc = JsonDocument.Parse(bodyText);
                    // depth-first search for a string node that itself looks like JSON object
                    var stack = new Stack<JsonElement>();
                    stack.Push(doc.RootElement);
                    string? candidate = null;
                    while (stack.Count > 0)
                    {
                        var el = stack.Pop();
                        switch (el.ValueKind)
                        {
                            case JsonValueKind.String:
                                var s = el.GetString();
                                if (!string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("{") && s.TrimEnd().EndsWith("}"))
                                {
                                    candidate = s.Trim();
                                    break;
                                }
                                break;
                            case JsonValueKind.Object:
                                foreach (var prop in el.EnumerateObject()) stack.Push(prop.Value);
                                break;
                            case JsonValueKind.Array:
                                foreach (var item in el.EnumerateArray()) stack.Push(item);
                                break;
                        }
                        if (candidate != null) break;
                    }
                    if (!string.IsNullOrEmpty(candidate))
                    {
                        // validate it's parseable
                        using var _ = JsonDocument.Parse(candidate);
                        return candidate;
                    }
                }
                catch (JsonException)
                {
                    // not JSON — fall back
                }

                var extracted = JsonExtractor.ExtractFirstJsonSubstring(bodyText);
                if (!string.IsNullOrEmpty(extracted)) return extracted;

                // fallback: return raw body (keeps original behavior)
                return bodyText;
            }
        }

        public async Task<string?> GetStructuredJsonAsync3(string apiKey, string prompt, int maxPromptLength = 50000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            if (prompt.Length > maxPromptLength) prompt = prompt.Substring(0, maxPromptLength);

            var url = $"{_options.Endpoint.TrimEnd('/')}/openai/deployments/{_options.DeploymentName}/responses?api-version={_options.ApiVersion}";

            var instruction = $@"
You are a precise resume parser. Input is raw resume text. Return ONLY a single valid JSON object (no explanation).
Schema:
{{ ""file_name"": ""string or null"", ""name"": ""string or null"", ""email"": ""string or null"", ""phone"": ""string or null"", ""skills"": [""string""], ""total_years_experience"": integer or null, ""summary"": ""string or null"" }}
Now parse the resume below and output only the JSON object.

----RESUME----
{prompt}
----END----
";

            // Prepare request body - Responses API accepts 'input' as a string or array.
            var body = new { input = instruction };

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // Simple retry for transient errors (429, 503)
            const int maxAttempts = 3;
            int attempt = 0;
            TimeSpan GetBackoff(int a) => TimeSpan.FromSeconds(Math.Pow(2, a)); // 1s, 2s, 4s...

            while (true)
            {
                attempt++;
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                // Required headers
                req.Headers.Add("api-key", apiKey);
                req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                // Add a useful User-Agent for server logs
                req.Headers.UserAgent.ParseAdd("ResumeScannerClient/1.0");

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // honor caller cancellation
                }
                catch (Exception ex)
                {
                    // Non-HTTP network error: retry if attempts left
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(GetBackoff(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    throw new HttpRequestException("Network error while calling Azure OpenAI endpoint.", ex);
                }

                // If 405, return a richer error so caller can inspect
                if (resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    var bodyText = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    // Try to include Allow header if present
                    var allow = resp.Headers.TryGetValues("Allow", out var allowVals) ? string.Join(",", allowVals) : null;
                    var message = $"Azure endpoint returned 405 Method Not Allowed. Allow: {allow ?? "N/A"}. Response body: {bodyText}";
                    throw new HttpRequestException(message);
                }

                // Retry on transient codes
                if (resp.StatusCode == (System.Net.HttpStatusCode)429 || resp.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt < maxAttempts)
                    {
                        // honor Retry-After if provided
                        if (resp.Headers.RetryAfter?.Delta is TimeSpan delta)
                        {
                            await Task.Delay(delta, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(GetBackoff(attempt), cancellationToken).ConfigureAwait(false);
                        }

                        continue; // retry
                    }

                    var bodyText = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new HttpRequestException($"Azure endpoint returned {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {bodyText}");
                }

                // If not success, try to surface helpful message (but don't always throw raw)
                if (!resp.IsSuccessStatusCode)
                {
                    var bodyText = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new HttpRequestException($"Azure endpoint returned {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {bodyText}");
                }

                // Success - read content
                var respText = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                // Try 1: If response is JSON and contains a top-level text/result, attempt to extract it
                try
                {
                    using var doc = JsonDocument.Parse(respText);
                    // Common Azure Responses API shapes can vary; try a few sensible spots:
                    // - responses (array) -> content -> text
                    // - output / choices / data
                    // We'll search the JSON for the first string value containing a JSON object (starts with { and ends with })
                    string? candidate = null;

                    // Depth-first search for string nodes that look like a JSON object
                    var stack = new Stack<JsonElement>();
                    stack.Push(doc.RootElement);
                    while (stack.Count > 0)
                    {
                        var el = stack.Pop();
                        switch (el.ValueKind)
                        {
                            case JsonValueKind.String:
                                var s = el.GetString();
                                if (!string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("{") && s.TrimEnd().EndsWith("}"))
                                {
                                    candidate = s.Trim();
                                    break;
                                }
                                break;
                            case JsonValueKind.Object:
                                foreach (var prop in el.EnumerateObject())
                                    stack.Push(prop.Value);
                                break;
                            case JsonValueKind.Array:
                                foreach (var item in el.EnumerateArray())
                                    stack.Push(item);
                                break;
                        }
                        if (candidate != null) break;
                    }

                    if (!string.IsNullOrEmpty(candidate))
                    {
                        // Validate it's real JSON object
                        try
                        {
                            using var _ = JsonDocument.Parse(candidate);
                            return candidate;
                        }
                        catch
                        {
                            // Not valid JSON despite braces - fall through to extractor
                        }
                    }
                }
                catch (JsonException)
                {
                    // not JSON — continue to extraction below
                }

                // Fallback: robust substring extractor (your existing helper)
                var extracted = JsonExtractor.ExtractFirstJsonSubstring(respText);
                if (!string.IsNullOrEmpty(extracted))
                {
                    return extracted;
                }

                // If we get here: success status but couldn't find JSON. Return raw text as last resort (or null).
                // Returning null may be better to signal "not found" to callers — but preserve original behavior and return raw text.
                return respText;
            }
        }

        public async Task<string?> GetStructuredJsonAsync2(string apiKey, string prompt, int maxPromptLength = 50000, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return null;
            if (prompt.Length > maxPromptLength) prompt = prompt.Substring(0, maxPromptLength);

            var url = $"{_options.Endpoint.TrimEnd('/')}/openai/deployments/{_options.DeploymentName}/responses?api-version={_options.ApiVersion}";

            var instruction = $@"
You are a precise resume parser. Input is raw resume text. Return ONLY a single valid JSON object (no explanation).
Schema:
{{ ""file_name"": ""string or null"", ""name"": ""string or null"", ""email"": ""string or null"", ""phone"": ""string or null"", ""skills"": [""string""], ""total_years_experience"": integer or null, ""summary"": ""string or null"" }}
Now parse the resume below and output only the JSON object.

----RESUME----
{prompt}
----END----
";

            var body = new { input = instruction };
            var json = JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("api-key", apiKey);

            using var resp = await _http.SendAsync(req, cancellationToken);
            resp.EnsureSuccessStatusCode();
            var respText = await resp.Content.ReadAsStringAsync(cancellationToken);

            // Extract first JSON object substring (robust)
            var extracted = JsonExtractor.ExtractFirstJsonSubstring(respText);
            return extracted;
        }
    
    
    }
}
