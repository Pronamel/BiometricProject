using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IServerHandler
{
    // Basic Server Communication
    Task<bool> TestConnectionAsync();
    bool IsAuthenticated { get; }

    Task<OfficialLoginResponse?> LoginAsync(string username, string password);
    Task<bool> LogoutAsync();
    
    // Device Management
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    Task<bool> UpdateDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    Task<List<dynamic>?> GetAllVotersAsync();
    Task<List<PollingStationOption>?> GetAllPollingStationsAsync();
    Task<bool> CreateVoterWithFingerprintAsync(
        string nin,
        string firstName,
        string lastName,
        string dateOfBirth,
        string addressLine1,
        string addressLine2,
        string postCode,
        string county,
        string constituency,
        byte[] fingerprintData);
    Task<bool> CreateOfficialWithFingerprintAsync(
        string username,
        string password,
        string pollingStationId,
        string county,
        byte[] fingerprintData);
    Task<FingerprintComparisonResponse?> VerifyFingerprintAsync(string username, string password, byte[] scannedFingerprint);
    Task<bool> SetAccessCodeAsync(string accessCode);
    Task<bool> SendDeviceCommandAsync(SendDeviceCommandRequest request);
    Task<PollingStationVoteCountResponse?> GetPollingStationVoteCountAsync();
    
    Task<bool> GenerateAccessCodeForVoterAsync(string voterId);
    
    // Legacy Device Management (placeholder)
    Task<List<DeviceManagementInfo>?> GetAllConnectedDevicesAsync();
    Task<bool> AddConnectedDeviceAsync(string deviceName, string pollingStationId);
    Task<bool> RemoveConnectedDeviceAsync(string deviceName, string pollingStationId);
    
    // Events for real-time updates
    event Action<DeviceManagementInfo>? DeviceConnected;
    event Action<DeviceManagementInfo>? DeviceDisconnected;
    event Action<DeviceManagementInfo>? DeviceInfoUpdated;
    event Action<string>? AccessCodeGenerated;
}