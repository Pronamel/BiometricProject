using System;
using System.Collections.Generic;

namespace officialApp.Models;

public class ElectionStatistics
{
    public bool Success { get; set; }
    public Guid ElectionId { get; set; }
    public Guid PollingStationId { get; set; }
    public int TotalVotes { get; set; }
    public int RegisteredVoters { get; set; }
    public decimal TurnoutRate { get; set; }
    public int PollingStationsConnected { get; set; }
    public int TotalPollingStations { get; set; }
    public decimal PollingStationConnectionRate { get; set; }
    public int CountryPopulation { get; set; }
    public decimal PopulationParticipationRate { get; set; }
    public string? ConstituencyName { get; set; }
    public List<CandidateVoteInfo> CandidateVotes { get; set; } = new();
    public List<ConstituencyTurnoutInfo> TopConstituencies { get; set; } = new();
    public ConstituencyTurnoutInfo? BestConstituency { get; set; }
}

public class CandidateVoteInfo
{
    public Guid CandidateId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public int VoteCount { get; set; }

    public string FullName => $"{FirstName} {LastName}";
    public decimal VotePercentage { get; set; }
    public string VotePercentageText => $"{VotePercentage:F1}%";
}

public class ConstituencyTurnoutInfo
{
    public Guid ConstituencyId { get; set; }
    public string ConstituencyName { get; set; } = string.Empty;
    public int TotalVotes { get; set; }
    public int ExpectedVotes { get; set; }
    public decimal TurnoutRate { get; set; }
    public string TurnoutRateText => $"{TurnoutRate:F1}%";
}
