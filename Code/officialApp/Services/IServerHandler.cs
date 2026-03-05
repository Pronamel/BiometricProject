using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public interface IServerHandler
{
    // Basic Server Communication
    Task<bool> TestConnectionAsync();
    
    // Device Management
    Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync();
    Task<bool> UpdateDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo);
    
    // Long Polling - Official Side
    Task<List<string>?> WaitForVoterRequestsAsync();
    Task<bool> GenerateAccessCodeForVoterAsync(string voterId);
    
    // Legacy Device Management (placeholder)
    Task<List<DeviceManagementInfo>?> GetAllConnectedDevicesAsync();
    Task<bool> AddConnectedDeviceAsync(string deviceName, string pollingStationId);
    Task<bool> RemoveConnectedDeviceAsync(string deviceName, string pollingStationId);
    
    // Events for real-time updates
    event Action<DeviceManagementInfo>? DeviceConnected;
    event Action<DeviceManagementInfo>? DeviceDisconnected;
    event Action<DeviceManagementInfo>? DeviceInfoUpdated;
    event Action<List<string>>? VoterRequestsReceived;
    event Action<string>? AccessCodeGenerated;
}