using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IApiService
{
    // Authentication
    Task<OfficialLoginResponse?> LoginAsync(string username, string password);
    bool IsAuthenticated { get; }
    string? CurrentOfficialId { get; }
    Task<bool> LogoutAsync();
    string? GetAuthToken();
    string GetRealtimeHubUrl();
    Task<bool> SendDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    
    // Long Polling Methods
    Task<bool> GenerateAccessCodeAsync(string voterId);
    Task<bool> SetAccessCodeAsync(string accessCode);
    
    // Vote Management
    
    // Device Status Management
    Task<bool> SendDeviceCommandAsync(SendDeviceCommandRequest request);
    
    // Database Queries
    Task<List<dynamic>?> GetAllVotersAsync();
    
    // Fingerprint Verification
    Task<FingerprintComparisonResponse?> CompareFingerpringsAsync(byte[] fingerprint1, byte[] fingerprint2);
    Task<FingerprintComparisonResponse?> VerifyFingerprintAsync(string username, string password, byte[] scannedFingerprint);
    
    // Fingerprint Management
    Task<bool> UploadOfficialFingerprintAsync(string username, string password, byte[] fingerprintData);
    Task<(bool Success, string Message)> CreateVoterWithFingerprintAsync(
        string nationalInsuranceNumber,
        string firstName,
        string lastName,
        string dateOfBirth,
        string townOfBirth,
        string addressLine1,
        string addressLine2,
        string postCode,
        string county,
        string constituency,
        byte[] fingerprintData);
    Task<(bool Success, string Message)> CreateOfficialWithFingerprintAsync(
        string username,
        string password,
        string assignedPollingStationId,
        string assignedCountyId,
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
    
    // Polling Stations
    Task<List<PollingStationOption>?> GetAllPollingStationsAsync();
    Task<PollingStationVoteCountResponse?> GetPollingStationVoteCountAsync();
}