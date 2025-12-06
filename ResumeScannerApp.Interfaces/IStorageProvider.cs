using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Interfaces
{
    /// <summary>Abstract storage for uploaded/reading files. Local or cloud implementations.</summary>
    public interface IStorageProvider
    {
        Task EnsureFolderExistsAsync(string folderPath);
        Task SaveFileAsync(string folderPath, string fileName, Stream content, CancellationToken cancellationToken = default);
        IEnumerable<string> ListFilesAsync(string folderPath);
        IEnumerable<string> ListFileNames(string folderPath);
        Task<bool> DeleteFileIfExistsAsync(string folderPath, string fileName);
    }
}
