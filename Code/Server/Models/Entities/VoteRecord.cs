using System;
using System.ComponentModel.DataAnnotations;

namespace Server.Models.Entities
{
    public class VoteRecord
    {
        [Key]
        public Guid RecordId { get; set; }
        public Guid ElectionId { get; set; }
        public DateTime VotedAt { get; set; } = DateTime.UtcNow;

        public Election? Election { get; set; }
    }
}
