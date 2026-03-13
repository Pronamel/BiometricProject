using System;

namespace SecureVoteApp.Models.Entities
{
    public class Voter
    {
        public Guid VoterId { get; set; }
        public string NationalId { get; set; } = string.Empty;
        public Guid ElectoralRollNumber { get; set; }
        public Guid ConstituencyId { get; set; }
        public Guid? WardId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string AddressLine1 { get; set; } = string.Empty;
        public string PreviousAddress { get; set; } = string.Empty;
        public string Postcode { get; set; } = string.Empty;
        public byte[]? FingerprintScan { get; set; }
        public bool HasVoted { get; set; }
        public DateTime RegisteredDate { get; set; } = DateTime.UtcNow;

        public Constituency? Constituency { get; set; }
    }
}
