using ResumeScannerApp.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Services
{
    public class LocalStorageProvider : IStorageProvider
    {
        public Task EnsureFolderExistsAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            return Task.CompletedTask;
        }

        public async Task SaveFileAsync(string folderPath, string fileName, Stream content, CancellationToken cancellationToken = default)
        {
            await EnsureFolderExistsAsync(folderPath);
            var path = Path.Combine(folderPath, Path.GetFileName(fileName));
            using var fs = File.Create(path);
            await content.CopyToAsync(fs, cancellationToken);
            await fs.FlushAsync(cancellationToken);
        }

        public Task<IEnumerable<string>> ListFilesAsync(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return Task.FromResult(Enumerable.Empty<string>());
            var files = Directory.GetFiles(folderPath);
            return Task.FromResult((IEnumerable<string>)files);
        }
    }
}
