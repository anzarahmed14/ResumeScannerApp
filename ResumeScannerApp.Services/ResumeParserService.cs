using ResumeScannerApp.Config;
using ResumeScannerApp.Interfaces;
using ResumeScannerApp.Models;
using ResumeScannerApp.Utilities.Parsers;
using ResumeScannerApp.Utilities.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ResumeScannerApp.Services
{
    public class ResumeParserService : IResumeParser
    {
        private readonly ITextExtractor _textExtractor;
        private readonly IOpenAiClient _aiClient;
        private readonly AzureOpenAiOptions _aiOptions;
        private readonly string[] _skillKeywords = new[]
        {
            "c#", ".net", "asp.net", "sql", "javascript", "react", "angular",
            "python", "java", "aws", "azure", "docker", "kubernetes", "html", "css",
            "node", "mongodb", "mysql", "postgres", "git", "rest"
        };

        public ResumeParserService(ITextExtractor textExtractor, IOpenAiClient aiClient, AzureOpenAiOptions aiOptions)
        {
            _textExtractor = textExtractor;
            _aiClient = aiClient;
            _aiOptions = aiOptions;
        }

        public async Task<ParsedResumeResult> ParseFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var result = new ParsedResumeResult { FilePath = filePath };
            try
            {
                var text = await _textExtractor.ExtractTextAsync(filePath, cancellationToken);
                var dto = new ResumeDto { FileName = Path.GetFileName(filePath), FullText = text };

                // Local heuristics (fast & cheap)
                dto.Email = HeuristicsParser.ExtractEmail(text);
                dto.Phone = HeuristicsParser.ExtractPhone(text);
                dto.Skills = HeuristicsParser.ExtractSkills(text, _skillKeywords);
                dto.Name = HeuristicsParser.ExtractName(text);
                dto.TotalYearsExperience = HeuristicsParser.ExtractYearsExperience(text);
                dto.Location = HeuristicsParser.ExtractLocation(text);

                dto.Designation = HeuristicsParser.ExtractDesignation(text);

                // Validate local values
                if (!ContactValidator.IsValidEmail(dto.Email)) dto.Email = null;
                if (!ContactValidator.IsValidPhone(dto.Phone)) dto.Phone = null;

                // Enrich via AI (dependency inversion: we depend on IOpenAiClient)
                if (!string.IsNullOrWhiteSpace(_aiOptions.ApiKey))
                {
                    var modelJson = await _aiClient.GetStructuredJsonAsync(_aiOptions.ApiKey, text, _aiOptions.MaxPromptLength, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(modelJson))
                    {
                        try
                        {
                            var remote = JsonSerializer.Deserialize<ResumeDto>(modelJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (remote != null)
                            {
                                dto.Name = string.IsNullOrWhiteSpace(remote.Name) ? dto.Name : remote.Name;
                                dto.Email = string.IsNullOrWhiteSpace(remote.Email) ? dto.Email : (ContactValidator.IsValidEmail(remote.Email) ? remote.Email : dto.Email);
                                dto.Phone = string.IsNullOrWhiteSpace(remote.Phone) ? dto.Phone : (ContactValidator.IsValidPhone(remote.Phone) ? remote.Phone : dto.Phone);
                                dto.Skills = (remote.Skills != null && remote.Skills.Count > 0) ? remote.Skills : dto.Skills;
                                dto.TotalYearsExperience = remote.TotalYearsExperience ?? dto.TotalYearsExperience;
                                dto.Summary = remote.Summary ?? dto.Summary;
                            }
                        }
                        catch { /* don't fail whole parsing if AI returns unexpected shape */ }
                    }
                }

                result.Resume = dto;
                result.Success = true;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        public async Task<List<ParsedResumeResult>> ParseFolderAsync(string folderPath, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(folderPath)) return new List<ParsedResumeResult>();
            var files = Directory.GetFiles(folderPath);
            var tasks = files.Select(f => ParseFromFileAsync(f, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
    }
}
