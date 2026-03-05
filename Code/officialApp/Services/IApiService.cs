using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IApiService
{
    // Authentication
    Task<OfficialLoginResponse?> LoginAsync(string officialId, string stationId, string? password = null);
    bool IsAuthenticated { get; }
    string? CurrentOfficialId { get; }
    void Logout();
    
    // Connection & Device Management
    Task<bool> TestConnectionAsync();
    Task<bool> SendDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    
    // Long Polling Methods
    Task<OfficialRequestsResponse?> WaitForVoterRequestsAsync();
    Task<bool> GenerateAccessCodeAsync(string voterId);
}