using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Config
{
    public class AzureOpenAiOptions
    {
        public string Endpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string DeploymentName { get; set; } = "gpt-4o-mini";
        public string ApiVersion { get; set; } = "2024-02-01";
        public int MaxPromptLength { get; set; } = 50000;
    }
}
