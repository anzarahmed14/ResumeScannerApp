using ResumeScannerApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Utilities
{
    public static class ResumeScorer
    {
        /// <summary>
        /// Score weights (tunable):
        /// - Skills match weight: 40
        /// - Skill-years match weight: 30
        /// - Total years experience weight: 20
        /// - Team lead presence weight: 10
        /// </summary>
        private const double SkillsWeight = 30;
        private const double SkillYearsWeight = 15;
        private const double TotalExpWeight = 15;
        private const double TeamLeadWeight = 10;
        private const double LocationWeight = 15; // total weight distributed across locations
        private const double DesignationWeight = 15;

        /// <summary>
        /// Score a resume against the search query. Returns score 0..100 and a reason detail.
        /// </summary>
        public static (int Score, string Explanation) Score(ResumeDto resume, SearchRequest query)
        {
            if (resume == null) return (0, "No resume");

            double score = 0;
            var explanations = new List<string>();

            // normalize resume skills
            var resumeSkills = (resume.Skills ?? new List<string>())
                                .Select(s => s?.Trim().ToLowerInvariant() ?? "")
                                .Where(s => s != "")
                                .ToList();

            // 1) Skill presence: distribute SkillsWeight across requested skills
            if (query.Skills != null && query.Skills.Count > 0)
            {
                double perSkill = SkillsWeight / query.Skills.Count;
                double perSkillYears = SkillYearsWeight / query.Skills.Count;

                for (int i = 0; i < query.Skills.Count; i++)
                {
                    var sq = query.Skills[i];
                    var skillLower = (sq.Name ?? "").Trim().ToLowerInvariant();
                    bool hasSkill = resumeSkills.Any(rs => rs.Contains(skillLower) || skillLower.Contains(rs));
                    if (hasSkill)
                    {
                        score += perSkill;
                        explanations.Add($"Skill '{sq.Name}' matched (+{perSkill:F1})");

                        // 2) Skill years: compare resume.TotalYearsExperience as proxy
                        // You may replace this with a per-skill years extraction if available.
                        if (sq.Years.HasValue)
                        {
                            // If resume.TotalYearsExperience >= requested years => full points
                            if (resume.TotalYearsExperience.HasValue && resume.TotalYearsExperience.Value >= sq.Years.Value)
                            {
                                score += perSkillYears;
                                explanations.Add($"Skill-years for '{sq.Name}' met (+{perSkillYears:F1})");
                            }
                            else
                            {
                                // partial credit proportionally (cap at perSkillYears)
                                if (resume.TotalYearsExperience.HasValue && resume.TotalYearsExperience.Value > 0)
                                {
                                    double proportion = Math.Min(1.0, (double)resume.TotalYearsExperience.Value / sq.Years.Value);
                                    double partial = perSkillYears * proportion;
                                    score += partial;
                                    explanations.Add($"Skill-years for '{sq.Name}' partial (+{partial:F1})");
                                }
                            }
                        }
                    }
                    else
                    {
                        explanations.Add($"Skill '{sq.Name}' not found (+0)");
                    }
                }
            }

            // 3) Total years experience weight
            if (query.MinTotalExperience.HasValue)
            {
                if (resume.TotalYearsExperience.HasValue && resume.TotalYearsExperience.Value >= query.MinTotalExperience.Value)
                {
                    score += TotalExpWeight;
                    explanations.Add($"Total experience {resume.TotalYearsExperience} >= {query.MinTotalExperience} (+{TotalExpWeight})");
                }
                else
                {
                    // partial credit proportionally based on resume.TotalYearsExperience / min required
                    if (resume.TotalYearsExperience.HasValue && resume.TotalYearsExperience.Value > 0)
                    {
                        var proportion = Math.Min(1.0, (double)resume.TotalYearsExperience.Value / query.MinTotalExperience.Value);
                        var partial = TotalExpWeight * proportion;
                        score += partial;
                        explanations.Add($"Total experience partial credit (+{partial:F1})");
                    }
                    else
                    {
                        explanations.Add($"Total experience missing or 0 (+0)");
                    }
                }
            }
            else
            {
                // no min required — give some credit based on total experience (half of weight)
                if (resume.TotalYearsExperience.HasValue)
                {
                    var partial = TotalExpWeight * Math.Min(1.0, resume.TotalYearsExperience.Value / 20.0); // 20 years -> full
                    score += partial;
                    explanations.Add($"Total experience baseline (+{partial:F1})");
                }
            }




            // 4) Team lead / leadership presence
            if (query.RequireTeamLeadExperience)
            {
                bool hasLead = ContainsLeadership(resume.FullText);
                if (hasLead)
                {
                    score += TeamLeadWeight;
                    explanations.Add($"Leadership found (+{TeamLeadWeight})");
                }
                else
                {
                    explanations.Add("Leadership not found (+0)");
                }
            }
            else
            {
                // even if not required, small credit if they have leadership
                if (ContainsLeadership(resume.FullText))
                {
                    var small = TeamLeadWeight * 0.5;
                    score += small;
                    explanations.Add($"Leadership found (bonus +{small:F1})");
                }
            }


            // 5) Location matching / filtering (supports multiple locations)
            if (query.Locations != null && query.Locations.Count > 0)
            {
                var resumeLoc = (resume.Location ?? "").Trim().ToLowerInvariant();
                int totalRequested = query.Locations.Count;
                double perLocationWeight = LocationWeight / totalRequested;
                int matchedCount = 0;
                var locMatches = new List<string>();

                foreach (var loc in query.Locations)
                {
                    if (string.IsNullOrWhiteSpace(loc)) continue;
                    var desired = loc.Trim().ToLowerInvariant();
                    bool match = false;
                    switch (query.LocationMode)
                    {
                        case LocationMatchMode.Exact:
                            match = resumeLoc == desired;
                            break;
                        case LocationMatchMode.StartsWith:
                            match = resumeLoc.StartsWith(desired);
                            break;
                        case LocationMatchMode.Contains:
                        default:
                            match = resumeLoc.Contains(desired);
                            break;
                    }

                    if (match)
                    {
                        matchedCount++;
                        locMatches.Add(loc);
                        score += perLocationWeight;
                        explanations.Add($"Location '{loc}' matched (+{perLocationWeight:F1})");
                    }
                    else
                    {
                        explanations.Add($"Location '{loc}' not matched (+0)");
                    }
                }

                // If strategy == All and not all matched => treat as fail (or heavy penalty)
                if (query.LocationStrategy == LocationMatchStrategy.All && matchedCount < totalRequested)
                {
                    if (query.LocationRequired)
                    {
                        return (0, $"Location requirement: all locations not matched. Matched: {matchedCount}/{totalRequested}");
                    }
                    else
                    {
                        // penalize heavily by not awarding any location weight
                        explanations.Add($"Not all locations matched ({matchedCount}/{totalRequested}), no location bonus awarded.");
                    }
                }


            }




            // 6) Designation matching / filtering (supports multiple designations)
            if (query.Designations != null && query.Designations.Count > 0)
            {
                var resumeDes = (resume.Designation ?? "").Trim().ToLowerInvariant();
                int totalRequested = query.Designations.Count;
                double perDesignationWeight = DesignationWeight / totalRequested;
                int matchedCount = 0;
                var desMatches = new List<string>();

                foreach (var des in query.Designations)
                {
                    if (string.IsNullOrWhiteSpace(des)) continue;
                    var desired = des.Trim().ToLowerInvariant();
                    bool match = false;
                    switch (query.DesignationMode)
                    {
                        case DesignationMatchMode.Exact:
                            match = resumeDes == desired;
                            break;
                        case DesignationMatchMode.StartsWith:
                            match = resumeDes.StartsWith(desired);
                            break;
                        case DesignationMatchMode.Contains:
                        default:
                            match = resumeDes.Contains(desired);
                            break;
                    }

                    if (match)
                    {
                        matchedCount++;
                        desMatches.Add(des);
                        score += perDesignationWeight;
                        explanations.Add($"Designation '{des}' matched (+{perDesignationWeight:F1})");
                    }
                    else
                    {
                        explanations.Add($"Designation '{des}' not matched (+0)");
                    }
                }

                if (query.DesignationStrategy == DesignationMatchStrategy.All && matchedCount < totalRequested)
                {
                    if (query.DesignationRequired)
                    {
                        return (0, $"Designation requirement: all designations not matched. Matched: {matchedCount}/{totalRequested}");
                    }
                    else
                    {
                        explanations.Add($"Not all designations matched ({matchedCount}/{totalRequested}), no designation bonus awarded.");
                    }
                }

                if (query.DesignationRequired && matchedCount == 0)
                {
                    return (0, $"Designation required but no requested designations matched (resume designation: '{resume.Designation ?? "unknown"}').");
                }
            }

                // clamp score to 0..100
                var final = (int)Math.Round(Math.Max(0, Math.Min(100, score)));
            var explanation = string.Join("; ", explanations);
            return (final, explanation);
        }

        private static bool ContainsLeadership(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.ToLowerInvariant();
            // simple patterns for team lead / lead / manager / technical lead etc.
            var patterns = new[] { "team lead", "technical lead", "tech lead", "lead developer", "lead engineer", "senior lead", "manager", "people manager", "leadership", "headed team", "led a team", "managed a team" };
            return patterns.Any(p => s.Contains(p));
        }
    }
}
