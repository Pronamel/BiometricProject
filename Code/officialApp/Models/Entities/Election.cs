using System;
using System.Collections.Generic;

namespace officialApp.Models.Entities
{
    public class Election
    {
        public Guid ElectionId { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime ElectionDate { get; set; }
        public string ElectionType { get; set; } = "general";
        public string Status { get; set; } = "pending";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Candidate>? Candidates { get; set; } = new List<Candidate>();
        public ICollection<VoteRecord>? VoteRecords { get; set; } = new List<VoteRecord>();
        public ICollection<ConstituencyResult>? Results { get; set; } = new List<ConstituencyResult>();
    }
}
