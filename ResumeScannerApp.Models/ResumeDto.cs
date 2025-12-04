using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Models
{
    public class ResumeDto
    {
        public string FileName { get; set; } = "";
        public string FullText { get; set; } = "";
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public List<string> Skills { get; set; } = new();
        public int? TotalYearsExperience { get; set; }
        public string? Summary { get; set; }

        public string? Location { get; set; }

        public string? Designation { get; set; }
    }
}
