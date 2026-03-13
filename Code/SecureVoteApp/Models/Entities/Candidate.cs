using System;

namespace SecureVoteApp.Models.Entities
{
    public class Candidate
    {
        public Guid CandidateId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? Party { get; set; }
        public string? Bio { get; set; }
        public Guid ConstituencyId { get; set; }
        public Guid ElectionId { get; set; }

        public Constituency? Constituency { get; set; }
        public Election? Election { get; set; }
    }
}
