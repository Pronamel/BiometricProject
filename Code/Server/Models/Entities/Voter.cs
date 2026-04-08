using System;

namespace Server.Models.Entities
{
    public class Voter
    {
        public Guid VoterId { get; set; }
        public string? Sdi { get; set; }
        public string? ProxySdi { get; set; }
        public byte[] NationalId { get; set; } = Array.Empty<byte>();
        public byte[] ElectoralRollNumber { get; set; } = Array.Empty<byte>();
        public Guid? ConstituencyId { get; set; }
        public string? WardId { get; set; }
        public byte[] FirstName { get; set; } = Array.Empty<byte>();
        public byte[] LastName { get; set; } = Array.Empty<byte>();
        public byte[] DateOfBirth { get; set; } = Array.Empty<byte>();
        public byte[]? TownOfBirth { get; set; }
        public byte[] AddressLine1 { get; set; } = Array.Empty<byte>();
        public byte[] PreviousAddress { get; set; } = Array.Empty<byte>();
        public byte[]? Postcode { get; set; }
        public string County { get; set; } = string.Empty;
        public byte[]? FingerprintScan { get; set; }
        public bool HasVoted { get; set; }
        public byte[] RegisteredDate { get; set; } = Array.Empty<byte>();
        public string? KeyId { get; set; }
        public byte[]? WrappedDek { get; set; }

        public Constituency? Constituency { get; set; }
    }
}
