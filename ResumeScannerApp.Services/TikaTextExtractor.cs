using ResumeScannerApp.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Services
{
    public class TikaTextExtractor : ITextExtractor
    {
        //private readonly TextExtractor _tika = new TextExtractor();

        //public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
        //{
        //    // Keep this synchronous call minimal — wrap in Task to satisfy async interface.
        //    try
        //    {
        //        var result = _tika.Parse(filePath);
        //        var txt = result.Text ?? "";
        //        return Task.FromResult(txt);
        //    }
        //    catch (Exception ex)
        //    {
        //        throw new InvalidOperationException($"Tika extraction failed for {filePath}: {ex.Message}", ex);
        //    }
        //}
        public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
