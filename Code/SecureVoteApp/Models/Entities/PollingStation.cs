using System;

namespace SecureVoteApp.Models.Entities
{
    public class PollingStation
    {
        public Guid PollingStationId { get; set; }
        public Guid PollingStationCode { get; set; }
        public Guid ConstituencyId { get; set; }
        public string? County { get; set; }
        public int InvalidVotes { get; set; }
        public int TotalVotes { get; set; }
        public int ExpectedVotes { get; set; }

        public Constituency? Constituency { get; set; }
    }
}
