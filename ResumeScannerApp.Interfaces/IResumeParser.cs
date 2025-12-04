using ResumeScannerApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Interfaces
{
    /// <summary>High level parser that returns structured ResumeDto from a file path.</summary>
    public interface IResumeParser
    {
        Task<ParsedResumeResult> ParseFromFileAsync(string filePath, CancellationToken cancellationToken = default);
        Task<List<ParsedResumeResult>> ParseFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    }
}
