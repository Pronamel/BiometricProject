using System;

namespace Server.Models.Entities
{
    public class Official
    {
        public Guid OfficialId { get; set; }
        public string? Username { get; set; }
        public string? PasswordHash { get; set; }
        public DateTime? LastLogin { get; set; }
        public Guid? AssignedPollingStationId { get; set; }
        public byte[]? FingerPrintScan { get; set; }
        public string? KeyId { get; set; }
        public byte[]? WrappedDek { get; set; }

        public PollingStation? AssignedPollingStation { get; set; }
    }
}
