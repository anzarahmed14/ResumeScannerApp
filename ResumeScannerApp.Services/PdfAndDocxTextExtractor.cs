using DocumentFormat.OpenXml.Packaging;
using ResumeScannerApp.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace ResumeScannerApp.Services
{
    /// <summary>
    /// Extracts text from PDF (.pdf), Word (.docx), and plain text (.txt) files.
    /// Uses PdfPig for PDFs and Open XML SDK for .docx files.
    /// This is a modern, .NET 9 compatible replacement for Tika-based extractor.
    /// </summary>
    public class PdfAndDocxTextExtractor : ITextExtractor
    {
        public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath is null or empty", nameof(filePath));

            var ext = Path.GetExtension(filePath)?.ToLowerInvariant();

            try
            {
                switch (ext)
                {
                    case ".pdf":
                        return await Task.Run(() => ExtractTextFromPdf(filePath), cancellationToken);

                    case ".docx":
                        return await Task.Run(() => ExtractTextFromDocx(filePath), cancellationToken);

                    case ".txt":
                    case ".text":
                        return await File.ReadAllTextAsync(filePath, cancellationToken);

                    default:
                        // Try best-effort: if unknown extension, attempt reading as text
                        try
                        {
                            return await File.ReadAllTextAsync(filePath, cancellationToken);
                        }
                        catch
                        {
                            throw new NotSupportedException($"File format '{ext}' is not supported for automatic text extraction.");
                        }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Text extraction failed for '{filePath}': {ex.Message}", ex);
            }
        }

        private string ExtractTextFromPdf(string filePath)
        {
            var sb = new StringBuilder();
            // PdfPig reads page by page
            using var pdf = PdfDocument.Open(filePath);
            foreach (var page in pdf.GetPages())
            {
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    sb.AppendLine(text);
                }
            }
            return sb.ToString();
        }

        private string ExtractTextFromDocx(string filePath)
        {
            // Open the Word document for read-only access
            using var doc = WordprocessingDocument.Open(filePath, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return string.Empty;

            // Body.InnerText returns concatenation of text nodes with spaces/newlines — good for many cases
            var text = body.InnerText ?? string.Empty;
            return text;
        }
    }
}
