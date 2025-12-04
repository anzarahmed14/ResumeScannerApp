using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ResumeScannerApp.Helpers
{
    public static class JsonExtractor
    {
        // Find the first JSON object substring and validate it
        public static string? ExtractFirstJsonSubstring(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var start = s.IndexOf('{');
            if (start < 0) return null;
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var candidate = s.Substring(start, i - start + 1);
                        try { using var doc = JsonDocument.Parse(candidate); if (doc.RootElement.ValueKind == JsonValueKind.Object) return candidate; }
                        catch { /* not valid - continue scanning */ }
                    }
                }
            }
            return null;
        }
    }
}
