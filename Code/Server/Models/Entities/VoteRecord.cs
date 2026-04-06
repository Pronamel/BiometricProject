using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Models.Entities
{
    public class VoteRecord
    {
        [Key]
        public Guid RecordId { get; set; }
        public Guid ElectionId { get; set; }
        public Guid? CandidateId { get; set; }
        public Guid? ConstituencyId { get; set; }
        public Guid? PollingStationId { get; set; }
        public DateTime VotedAt { get; set; } = DateTime.UtcNow;

        public Election? Election { get; set; }
        public Candidate? Candidate { get; set; }
        public Constituency? Constituency { get; set; }
        public PollingStation? PollingStation { get; set; }
    }
}
