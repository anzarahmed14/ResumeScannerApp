using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Interfaces
{
    /// <summary>
    /// Extract textual content from a file (pdf/docx/txt).
    /// Implementations: TikaTextExtractor, OcrTextExtractor, AzureFormRecognizerExtractor...
    /// </summary>
    public interface ITextExtractor
    {
        /// <summary>Return extracted plain text or throw on fatal extraction error.</summary>
        Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
