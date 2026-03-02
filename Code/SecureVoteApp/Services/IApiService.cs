using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public interface IApiService
{
    Task<List<ServerResponse>?> GetWeatherDataAsync();
    Task<bool> SubmitVoteAsync(string candidateName, string party);
    Task<List<string>?> GetCandidatesAsync();
    Task<bool> TestConnectionAsync();
}