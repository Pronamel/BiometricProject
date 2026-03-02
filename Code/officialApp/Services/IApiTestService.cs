using System;
using System.Threading.Tasks;

namespace officialApp.Services;

public interface IApiTestService
{
    Task<string> RunConnectionTestAsync();
    Task<string> RunWeatherTestAsync();
    Task<string> RunCandidateTestAsync();
    Task<string> RunVoteTestAsync();
    Task<string> RunAllTestsAsync();
}