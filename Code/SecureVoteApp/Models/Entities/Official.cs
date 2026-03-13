using System;

namespace SecureVoteApp.Models.Entities
{
    public class Official
    {
        public Guid OfficialId { get; set; }
        public string? Username { get; set; }
        public string? PasswordHash { get; set; }
        public DateTime? LastLogin { get; set; }
        public Guid? AssignedCountyId { get; set; }
        public Guid? AssignedPollingStationId { get; set; }

        public PollingStation? AssignedPollingStation { get; set; }
    }
}
