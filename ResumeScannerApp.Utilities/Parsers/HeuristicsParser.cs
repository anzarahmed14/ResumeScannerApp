using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ResumeScannerApp.Utilities.Parsers
{
    public static class HeuristicsParser
    {
        public static string? ExtractEmail(string text)
        {
            var m = Regex.Match(text ?? "", @"[a-zA-Z0-9\.\-_]+@[a-zA-Z0-9\.\-_]+\.[a-zA-Z]{2,}", RegexOptions.IgnoreCase);
            return m.Success ? m.Value : null;
        }

        public static string? ExtractPhone(string text)
        {
            var m = Regex.Match(text ?? "", @"(\+?\d{1,3}[\s\-\.])?(\(?\d{2,4}\)?[\s\-\.])?\d{6,10}", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.Trim() : null;
        }

        public static List<string> ExtractSkills(string text, string[] skillKeywords)
        {
            text = (text ?? "").ToLowerInvariant();
            var found = new HashSet<string>();
            foreach (var skill in skillKeywords)
            {
                if (text.Contains(skill.ToLowerInvariant()))
                    found.Add(skill);
            }
            return found.ToList();
        }

        public static string? ExtractName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim()).Where(l => l.Length > 2).Take(12);
            foreach (var line in lines)
            {
                var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var capCount = tokens.Count(t => char.IsUpper(t[0]));
                if (capCount >= Math.Min(2, tokens.Length))
                {
                    return Regex.Replace(line, @"[^\w\s\-]", "").Trim();
                }
            }
            return null;
        }

        public static int? ExtractYearsExperience(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var m = Regex.Match(text, @"(\d{1,2})\s+years?", RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int yrs)) return yrs;
            var years = Regex.Matches(text, @"(19|20)\d{2}").Select(s => int.Parse(s.Value)).ToList();
            if (years.Count >= 2)
            {
                var diff = years.Max() - years.Min();
                if (diff > 0 && diff < 50) return diff;
            }
            return null;
        }
        public static string? ExtractLocation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Look for explicit "Location:" or "City:" labels
            var labelMatch = Regex.Match(text, @"(?:Location|City|Address|Lives in)[:\s]+\s*(.+)", RegexOptions.IgnoreCase);
            if (labelMatch.Success)
            {
                var loc = labelMatch.Groups[1].Value.Trim();
                // strip trailing punctuation and limit length
                return loc.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimEnd(',', '.');
            }

            // Fallback: check the first 6 lines for a city/state-like line (short line with letters & spaces)
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 1 && l.Length < 60)
                            .Take(8);
            foreach (var line in lines)
            {
                // skip lines containing email/phone (not locations)
                if (Regex.IsMatch(line, @"\S+@\S+") || Regex.IsMatch(line, @"\d{6,}")) continue;

                // crude heuristic: contains a comma and contains letters (e.g., "Pune, Maharashtra" or "Bengaluru, India")
                if (line.Contains(",") && Regex.IsMatch(line, @"[A-Za-z]"))
                    return line;

                // single-word city names (e.g., "Mumbai") - check against a small city token list if desired
            }

            return null;
        }

        public static string? ExtractDesignation(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // 1) Look for explicit labels at top like "Designation:", "Title:", "Role:"
            var labelMatch = Regex.Match(text, @"(?:Designation|Title|Role|Current Title|Current Role)[:\s]+\s*(.+)", RegexOptions.IgnoreCase);
            if (labelMatch.Success)
            {
                var des = labelMatch.Groups[1].Value.Trim();
                return des.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimEnd(',', '.');
            }

            // 2) Check first few lines for a role-like line (short, contains Developer/Engineer/Lead/Manager)
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 1 && l.Length < 80)
                            .Take(12);

            foreach (var line in lines)
            {
                // skip contact lines
                if (Regex.IsMatch(line, @"\S+@\S+") || Regex.IsMatch(line, @"\d{6,}")) continue;

                // common role keywords
                var keywords = new[] { "developer", "engineer", "lead", "manager", "architect", "principal", "consultant", "analyst", "director" };
                var lower = line.ToLowerInvariant();
                if (keywords.Any(k => lower.Contains(k)))
                {
                    // return the line as a candidate designation (clean punctuation)
                    return Regex.Replace(line, @"[^\w\s\-\/\.]", "").Trim();
                }
            }

            // 3) fallback: try to find "Senior" / "Junior" + role patterns globally
            var m = Regex.Match(text, @"(Senior|Sr\.?|Junior|Jr\.?)\s+([A-Za-z\/\s]{2,40}(Developer|Engineer|Manager|Lead|Architect|Analyst))", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Value.Trim();
            }

            return null;
        }
    }
}
