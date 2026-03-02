using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IApiService
{
    Task<List<ServerResponse>?> GetWeatherDataAsync();
    Task<bool> SubmitVoteAsync(string candidateName, string party);
    Task<List<string>?> GetCandidatesAsync();
    Task<bool> TestConnectionAsync();
}