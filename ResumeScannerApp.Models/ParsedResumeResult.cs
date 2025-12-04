using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Models
{
    public class ParsedResumeResult
    {
        public string FilePath { get; set; } = "";
        //dsads
        public ResumeDto Resume { get; set; } = new ResumeDto();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
