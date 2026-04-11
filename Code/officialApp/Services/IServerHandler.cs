using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IServerHandler
{
    bool IsAuthenticated { get; }

    Task<OfficialLoginResponse?> LoginAsync(string username, string password);
    Task<bool> LogoutAsync();
    
    // Device Management
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    Task<bool> UpdateDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    Task<List<dynamic>?> GetAllVotersAsync();
    Task<List<PollingStationOption>?> GetAllPollingStationsAsync();
    Task<(bool Success, string Message)> CreateVoterWithFingerprintAsync(
        string nin,
        string firstName,
        string lastName,
        string dateOfBirth,
        string townOfBirth,
        string postCode,
        string county,
        string constituency,
        byte[] fingerprintData);
    Task<(bool Success, string Message)> CreateOfficialWithFingerprintAsync(
        string username,
        string password,
        string pollingStationId,
        string county,
        byte[] fingerprintData);
    Task<ProxyAssignmentResponse?> AssignProxyVoterAsync(
        string representedFirstName,
        string representedLastName,
        string representedDateOfBirth,
        string representedPostCode,
        string representedTownOfBirth,
        string proxyFirstName,
        string proxyLastName,
        string proxyDateOfBirth,
        string proxyPostCode,
        string proxyTownOfBirth,
        byte[] scannedFingerprint);
    Task<FingerprintComparisonResponse?> VerifyFingerprintAsync(string username, string password, byte[] scannedFingerprint);
    Task<bool> SetAccessCodeAsync(string accessCode);
    Task<bool> SendDeviceCommandAsync(SendDeviceCommandRequest request);
    Task<PollingStationVoteCountResponse?> GetPollingStationVoteCountAsync();
    Task<ElectionStatistics?> GetElectionStatisticsAsync();
    Task<DuplicateFingerprintScanResponse?> ScanDuplicateVoterFingerprintsAsync();
    
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