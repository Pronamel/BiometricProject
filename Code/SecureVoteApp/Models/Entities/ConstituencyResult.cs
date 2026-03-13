using System;

using System.ComponentModel.DataAnnotations;

namespace SecureVoteApp.Models.Entities
{
    public class ConstituencyResult
    {
        [Key]
        public Guid ResultId { get; set; }
        public Guid ConstituencyId { get; set; }
        public Guid CandidateId { get; set; }
        public Guid ElectionId { get; set; }
        public int TotalVotes { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public Constituency? Constituency { get; set; }
        public Candidate? Candidate { get; set; }
        public Election? Election { get; set; }
    }
}
