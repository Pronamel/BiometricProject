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
    void Logout();
    
    // Connection & Device Management
    Task<bool> TestConnectionAsync();
    Task<bool> SendDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    
    // Long Polling Methods
    Task<OfficialRequestsResponse?> WaitForVoterRequestsAsync();
    Task<bool> GenerateAccessCodeAsync(string voterId);
    Task<bool> SetAccessCodeAsync(string accessCode);
    
    // Vote Management
    Task<VoteNotificationResponse?> CheckForVotesAsync();
    
    // Database Queries
    Task<List<dynamic>?> GetAllVotersAsync();
    
    // Fingerprint Verification
    Task<FingerprintComparisonResponse?> CompareFingerpringsAsync(byte[] fingerprint1, byte[] fingerprint2);
    Task<FingerprintComparisonResponse?> VerifyFingerprintAsync(string username, string password, byte[] scannedFingerprint);
    
    // Fingerprint Management
    Task<bool> UploadOfficialFingerprintAsync(string username, string password, byte[] fingerprintData);
    Task<bool> CreateOfficialWithFingerprintAsync(string username, string password, byte[] fingerprintData);
    Task<bool> CreateVoterWithFingerprintAsync(
        string firstName,
        string lastName,
        string dateOfBirth,
        string addressLine1,
        string addressLine2,
        string postCode,
        string county,
        string constituency,
        byte[] fingerprintData);
}