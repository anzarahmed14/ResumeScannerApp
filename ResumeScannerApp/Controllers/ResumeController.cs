using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using ResumeScannerApp.Config;
using ResumeScannerApp.Interfaces;
using ResumeScannerApp.Models;
using ResumeScannerApp.Utilities;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResumeScannerApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ResumeController : ControllerBase
    {
        private readonly IResumeParser _parser;
        private readonly IStorageProvider _storage;
        private readonly string _resumeFolder;
        private readonly IOpenAiClient _openAiClient;
        private readonly AzureOpenAiOptions _aiOptions;
        public ResumeController(
    IResumeParser parser,
    IStorageProvider storage,
    IOpenAiClient openAiClient,
    AzureOpenAiOptions aiOptions,
    IConfiguration config)
        {
            _parser = parser;
            _storage = storage;
            _openAiClient = openAiClient;
            _aiOptions = aiOptions;
            _resumeFolder = config["ResumeFolder"] ?? Environment.GetEnvironmentVariable("RESUME_FOLDER") ?? @"C:\resume";

            _storage.EnsureFolderExistsAsync(_resumeFolder).GetAwaiter().GetResult();
        }

        [HttpGet("scan")]
        public async Task<IActionResult> Scan()
        {
            var parsed = await _parser.ParseFolderAsync(_resumeFolder);
            return Ok(parsed);
        }

        [HttpPost("upload")]
        public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            var uniqueName = MakeUniqueFileName(file.FileName);

            await _storage.SaveFileAsync(_resumeFolder, uniqueName, file.OpenReadStream(), cancellationToken);
            var path = Path.Combine(_resumeFolder, Path.GetFileName(uniqueName));
            var parsed = await _parser.ParseFromFileAsync(path, cancellationToken);
            if (!parsed.Success) return StatusCode(500, parsed.ErrorMessage);
            return CreatedAtAction(nameof(Upload), new { file = uniqueName }, parsed);
        }

        private string MakeUniqueFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLower();

            var name = CleanFileName(
                Path.GetFileNameWithoutExtension(fileName)
            );

            //var name = Path.GetFileNameWithoutExtension(fileName)
            //                .ToLower()
            //                .Replace(" ", "-");

            var uniqueId = Guid.NewGuid().ToString("N");

            return $"{name}-{uniqueId}{extension}";
        }

        string CleanFileName(string name)
        {
            name = Regex.Replace(name.ToLower(), @"[^a-z0-9\-]", "-");
            name = Regex.Replace(name, @"-+", "-");
            return name.Trim('-');
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest("Request body required.");

            // Parse all resumes (your existing method)
            var parsed = await _parser.ParseFolderAsync(_resumeFolder, cancellationToken);

            // Score each resume
            var scored = new List<object>();
            foreach (var p in parsed)
            {
                if (!p.Success || p.Resume == null) continue;

                var (score, explanation) = ResumeScorer.Score(p.Resume, request);

                // apply minScore filter if provided
                if (score < request.MinScore) continue;

                // requireTeamLeadExperience filter: enforced inside scorer but also allow strict filter
                if (request.RequireTeamLeadExperience)
                {
                    if (!ResumeScorer.Score(p.Resume, new SearchRequest { RequireTeamLeadExperience = true }).Score.Equals(score)
                        && !ResumeScorer.Score(p.Resume, new SearchRequest { RequireTeamLeadExperience = true }).Score.Equals(100))
                    {
                        // (we used scoring; this check is not necessary — kept for clarity)
                    }
                }

                scored.Add(new
                {
                    p.FilePath,
                    p.Success,
                    Score = score,
                    Explanation = explanation,
                    Resume = p.Resume
                });
            }

            // sort by score desc
            var sorted = scored.OrderByDescending(x => ((dynamic)x).Score).ToList();

            return Ok(sorted);
        }
        [HttpGet("{fileName}")]
        public async Task<IActionResult> ParseFile(string fileName)
        {
            var path = Path.Combine(_resumeFolder, fileName);
            if (!System.IO.File.Exists(path)) return NotFound();
            var parsed = await _parser.ParseFromFileAsync(path);
            if (!parsed.Success) return StatusCode(500, parsed.ErrorMessage);
            return Ok(parsed);
        }
        [HttpGet("test-connection2")]
        public async Task<IActionResult> TestConnection2(CancellationToken cancellationToken)
        {
            var message = "Hello! This is a connection test.";
            var openAiApiKey = _aiOptions.ApiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            // Replace with the correct endpoint used by your OpenAI client or wrapper
            var openAiUrl = "https://hexavarsity-secureapi.azurewebsites.net/api/azureai";

            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openAiApiKey);

                // Example request body for Chat Completions (adjust to your API)
                var requestPayload = new
                {
                    model = "gpt-4o-mini", // use the model you want / that your account supports
                    messages = new[]
                    {
                new { role = "system", content = "You are a helpful test assistant."},
                new { role = "user", content = $"Echo this JSON: {{\"status\":\"ok\",\"echo\":\"{message}\"}}" }
            },
                    max_tokens = 50
                };

                var json = System.Text.Json.JsonSerializer.Serialize(requestPayload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // POST to external API (important: must be POST for OpenAI style endpoints)
                var response = await http.PostAsync(openAiUrl, content, cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                // Return diagnostic data instead of throwing so you can debug
                return Ok(new
                {
                    api = "ResumeScanner API is running",
                    outgoingRequest = new
                    {
                        url = openAiUrl,
                        method = "POST",
                        requestBody = requestPayload
                    },
                    externalResponse = new
                    {
                        status = (int)response.StatusCode,
                        reason = response.ReasonPhrase,
                        body = responseBody,
                        isSuccess = response.IsSuccessStatusCode
                    }
                });
            }
            catch (HttpRequestException httpEx)
            {
                // network / http error with more info
                return StatusCode(502, new
                {
                    api = "ResumeScanner API is running",
                    openAiConnection = "FAILED",
                    error = httpEx.Message,
                    inner = httpEx.InnerException?.Message
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    api = "API is running",
                    openAiConnection = "FAILED",
                    error = ex.Message
                });
            }
        }


        [HttpGet("test-connection3")]
        public async Task<IActionResult> TestConnection3(CancellationToken cancellationToken)
        {
            var message = "Hello! This is a test message.";
            var secretKey = _aiOptions.ApiKey;     // Your SAPI_SECRET_KEY
            var baseUrl = _aiOptions.Endpoint;     // Should be: https://yourdomain/api/openai

            // Build the final ChatCompletions endpoint
            var url = $"{baseUrl.TrimEnd('/')}/chat/completions";

            // Same as the Python example
            var requestPayload = new
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
            new { role = "system", content = "You are a helpful assistant." },
            new { role = "user", content = $"Echo this JSON: {{\"status\":\"ok\",\"echo\":\"{message}\"}}" }
        },
                temperature = 0.7,
                max_tokens = 256,
                top_p = 0.6,
                frequency_penalty = 0.7
            };

            try
            {
                using var http = new HttpClient();

                // Company API uses Bearer token
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", secretKey
                    );

                var json = System.Text.Json.JsonSerializer.Serialize(requestPayload);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                // POST → /api/openai/chat/completions
                var response = await http.PostAsync(url, content, cancellationToken);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                return Ok(new
                {
                    api = "Test connection completed",
                    outgoing = new
                    {
                        url,
                        method = "POST",
                        body = requestPayload
                    },
                    externalResponse = new
                    {
                        status = (int)response.StatusCode,
                        reason = response.ReasonPhrase,
                        body = responseBody,
                        success = response.IsSuccessStatusCode
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    api = "Test connection failed",
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }


        [HttpGet("test-connection")]
        public async Task<IActionResult> TestConnection()
        {
            var secretKey = _aiOptions.ApiKey; // your api-key
            var endpoint = _aiOptions.Endpoint?.TrimEnd('/'); // e.g. https://hexavarsity-secureapi.azurewebsites.net or your azure resource
            var deployment = _aiOptions.DeploymentName ?? "gpt-4openai";
            var apiVersion = _aiOptions.ApiVersion ?? "2024-02-01"; // match your env

            // Azure deployment path:
            var url = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={apiVersion}";

            var payload = new
            {
                messages = new[] {
            new { role = "system", content = "You are a helpful assistant." },
            new { role = "user", content = "What is C#?" }
        },
                // optional parameters:
                max_tokens = 256,
                temperature = 0.7
            };

            using var http = new HttpClient();
            // Azure expects the API key in the "api-key" header for API-key auth:
            http.DefaultRequestHeaders.Add("api-key", secretKey);

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var res = await http.PostAsync(url, content);
            var body = await res.Content.ReadAsStringAsync();

            return Ok(new
            {
                url,
                status = (int)res.StatusCode,
                reason = res.ReasonPhrase,
                response = body
            });
        }

        [HttpGet("files")]
        public async Task<ActionResult> GetFiles()
        {
            var results = await _storage.ListFilesAsync(_resumeFolder);
            return Ok(results);
        }

        [HttpDelete("{fileName}")]
        public async Task<IActionResult> Delete(string fileName)
        {
            var deleted = await _storage.DeleteFileIfExistsAsync(_resumeFolder, fileName);

            if (!deleted)
                return NotFound("File not found.");

            return Ok(new
            {
                success = true,
                message = "File deleted successfully."
            });
        }

        [HttpGet("download/{fileName}")]
        public IActionResult Download(string fileName)
        {
            fileName = Path.GetFileName(fileName);   // SECURITY: prevent path traversal

            var filePath = Path.Combine(_resumeFolder, fileName);

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found.");

            var provider = new FileExtensionContentTypeProvider();

            if (!provider.TryGetContentType(filePath, out string? contentType))
            {
                contentType = "application/octet-stream"; // fallback
            }

            var bytes = System.IO.File.ReadAllBytes(filePath);

            return File(bytes, contentType, fileName);
        }

    }
}
