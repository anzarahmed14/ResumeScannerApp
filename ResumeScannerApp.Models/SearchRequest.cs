using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResumeScannerApp.Models
{
    public enum LocationMatchMode
    {
        None = 0,
        Exact = 1,
        Contains = 2,
        StartsWith = 3
    }

    public enum LocationMatchStrategy
    {
        Any = 0, // pass if any of the requested locations match
        All = 1  // require all requested locations to match (rare)
    }

    public enum DesignationMatchMode
    {
        None = 0,
        Exact = 1,
        Contains = 2,
        StartsWith = 3
    }

    public enum DesignationMatchStrategy
    {
        Any = 0, // pass if any requested designation matches
        All = 1  // require all requested designations to match
    }
    public class SkillQuery
    {
        public string Name { get; set; } = "";
        public int? Years { get; set; } = null;
    }

    public class SearchRequest
    {
        public List<SkillQuery> Skills { get; set; } = new();
        public int? MinTotalExperience { get; set; } = null;
        public bool RequireTeamLeadExperience { get; set; } = false;
        public int MinScore { get; set; } = 0; // 0-100


        // NEW: multiple locations support
        public List<string> Locations { get; set; } = new(); // e.g. ["Pune", "Mumbai"]
        public LocationMatchMode LocationMode { get; set; } = LocationMatchMode.Contains;
        public LocationMatchStrategy LocationStrategy { get; set; } = LocationMatchStrategy.Any;
        public bool LocationRequired { get; set; } = false; // if true, fail when no location matches

        // NEW: Designation support
        public List<string> Designations { get; set; } = new(); // e.g. ["Team Lead", "Senior Developer"]
        public DesignationMatchMode DesignationMode { get; set; } = DesignationMatchMode.Contains;
        public DesignationMatchStrategy DesignationStrategy { get; set; } = DesignationMatchStrategy.Any;
        public bool DesignationRequired { get; set; } = false; // fail if none match when true
    }
}
