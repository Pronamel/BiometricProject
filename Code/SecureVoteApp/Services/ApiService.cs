using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SecureVoteApp.Models;

namespace SecureVoteApp.Services;

public class ApiService : IApiService
{
    public static ApiService Instance { get; private set; }
    
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    static ApiService()
    {
        Instance = new ApiService(new HttpClient());
    }

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        
        // TODO: Move this to configuration or environment variable
        _baseUrl = "http://10.5.0.2:5165/weatherforecast"; // Server IP here
        
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Testing connection to: {_baseUrl}");
            
            // Try multiple endpoints to test connection
            var endpoints = new[] { "/api/health", "/api/weather", "/" };
            
            foreach (var endpoint in endpoints)
            {
                try
                {
                    var response = await _httpClient.GetAsync($"{_baseUrl}{endpoint}");
                    if (response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"Connection successful via endpoint: {endpoint}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed endpoint {endpoint}: {ex.Message}");
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Connection test failed: {ex.Message}");
            return false;
        }
    }

    public async Task<List<ServerResponse>?> GetWeatherDataAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Requesting weather data from: {_baseUrl}/api/weather");
            
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/weather");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Received response: {jsonString}");
                
                var data = JsonSerializer.Deserialize<List<ServerResponse>>(jsonString, _jsonOptions);
                System.Diagnostics.Debug.WriteLine($"Deserialized {data?.Count ?? 0} weather records");
                
                return data;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Weather API request failed: {response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON Deserialization error: {ex.Message}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP request error: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Request timeout: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unexpected API error: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> SubmitVoteAsync(string candidateName, string party)
    {
        try
        {
            var voteData = new { candidateName, party, timestamp = DateTime.UtcNow };
            var json = JsonSerializer.Serialize(voteData, _jsonOptions);
            
            System.Diagnostics.Debug.WriteLine($"Submitting vote: {json}");
            
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync($"{_baseUrl}/api/vote", content);
            
            if (response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine("Vote submitted successfully");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Vote submission failed: {response.StatusCode} - {errorContent}");
                return false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Vote submission error: {ex.Message}");
            return false;
        }
    }

    public async Task<List<string>?> GetCandidatesAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Requesting candidates from: {_baseUrl}/api/candidates");
            
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/candidates");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Received candidates response: {jsonString}");
                
                var data = JsonSerializer.Deserialize<List<string>>(jsonString, _jsonOptions);
                System.Diagnostics.Debug.WriteLine($"Deserialized {data?.Count ?? 0} candidates");
                
                return data;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Candidates API request failed: {response.StatusCode} - {response.ReasonPhrase}");
                return null;
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"JSON Deserialization error: {ex.Message}");
            return null;
        }
        catch (HttpRequestException ex)
        {
            System.Diagnostics.Debug.WriteLine($"HTTP request error: {ex.Message}");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Request timeout: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Unexpected API error: {ex.Message}");
            return null;
        }
    }
}