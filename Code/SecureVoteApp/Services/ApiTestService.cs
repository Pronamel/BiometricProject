using System;
using System.Text;
using System.Threading.Tasks;

namespace SecureVoteApp.Services;

public class ApiTestService : IApiTestService
{
    private readonly IApiService _apiService;

    public ApiTestService(IApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<string> RunConnectionTestAsync()
    {
        try
        {
            var isConnected = await _apiService.TestConnectionAsync();
            return isConnected 
                ? "✅ Connection test: SUCCESS" 
                : "❌ Connection test: FAILED - Server not reachable";
        }
        catch (Exception ex)
        {
            return $"❌ Connection test: ERROR - {ex.Message}";
        }
    }

    public async Task<string> RunWeatherTestAsync()
    {
        try
        {
            var weatherData = await _apiService.GetWeatherDataAsync();
            return weatherData != null && weatherData.Count > 0
                ? $"✅ Weather test: SUCCESS - Received {weatherData.Count} records"
                : "⚠️ Weather test: No data received";
        }
        catch (Exception ex)
        {
            return $"❌ Weather test: ERROR - {ex.Message}";
        }
    }

    public async Task<string> RunCandidateTestAsync()
    {
        try
        {
            var candidates = await _apiService.GetCandidatesAsync();
            return candidates != null && candidates.Count > 0
                ? $"✅ Candidates test: SUCCESS - Received {candidates.Count} candidates"
                : "⚠️ Candidates test: No data received";
        }
        catch (Exception ex)
        {
            return $"❌ Candidates test: ERROR - {ex.Message}";
        }
    }

    public async Task<string> RunVoteTestAsync()
    {
        try
        {
            var success = await _apiService.SubmitVoteAsync("Test Candidate", "Test Party");
            return success
                ? "✅ Vote submission test: SUCCESS"
                : "❌ Vote submission test: FAILED";
        }
        catch (Exception ex)
        {
            return $"❌ Vote submission test: ERROR - {ex.Message}";
        }
    }

    public async Task<string> RunAllTestsAsync()
    {
        var results = new StringBuilder();
        results.AppendLine("🔍 Running API Service Tests...\n");

        var connectionResult = await RunConnectionTestAsync();
        results.AppendLine(connectionResult);

        if (connectionResult.Contains("SUCCESS"))
        {
            var weatherResult = await RunWeatherTestAsync();
            results.AppendLine(weatherResult);

            var candidateResult = await RunCandidateTestAsync();
            results.AppendLine(candidateResult);

            var voteResult = await RunVoteTestAsync();
            results.AppendLine(voteResult);
        }
        else
        {
            results.AppendLine("⚠️ Skipping other tests due to connection failure");
        }

        results.AppendLine("\n🏁 Tests completed!");
        return results.ToString();
    }
}