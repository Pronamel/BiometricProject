using System;
using System.Collections.Generic;

namespace Server.Models.Entities
{
    public class Constituency
    {
        public Guid ConstituencyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalVoters { get; set; }

        public ICollection<Voter>? Voters { get; set; } = new List<Voter>();
        public ICollection<PollingStation>? PollingStations { get; set; } = new List<PollingStation>();
        public ICollection<ConstituencyResult>? Results { get; set; } = new List<ConstituencyResult>();
    }
}
