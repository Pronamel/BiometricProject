

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using officialApp.Models;

namespace officialApp.Services;

public class ServerHandler : IServerHandler
{
    private readonly IApiService _apiService;
    
    // Events for real-time updates
    public event Action<DeviceManagementInfo>? DeviceConnected;
    public event Action<DeviceManagementInfo>? DeviceDisconnected;
    public event Action<DeviceManagementInfo>? DeviceInfoUpdated;
    public event Action<List<string>>? VoterRequestsReceived;
    public event Action<string>? AccessCodeGenerated;
    
    public ServerHandler(IApiService apiService)
    {
        _apiService = apiService;
    }

    // ==========================================
    // AUTHENTICATION HELPER
    // ==========================================
    
    private bool IsAuthenticated()
    {
        return _apiService.IsAuthenticated;
    }
    
    private void ThrowIfNotAuthenticated()
    {
        if (!IsAuthenticated())
        {
            throw new InvalidOperationException("Not authenticated. Please login first.");
        }
    }
    
    // ==========================================
    // SERVER COMMUNICATION (calls ApiService)
    // ==========================================
    
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            return await _apiService.TestConnectionAsync();
        }
        catch
        {
            return false;
        }
    }
    
    // ==========================================
    // DEVICE MANAGEMENT (with data processing)
    // ==========================================
    
    public async Task<DeviceManagementInfo?> GetDeviceManagementInfoAsync()
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            // Call real server endpoint
            var deviceInfo = await _apiService.GetDeviceManagementInfoAsync();
            
            if (deviceInfo != null)
            {
                // Process and validate data before returning
                return ProcessDeviceInfo(deviceInfo);
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    public async Task<bool> UpdateDeviceManagementInfoAsync(DeviceManagementInfo deviceInfo)
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            // Process and validate the data first
            var processedInfo = ProcessDeviceInfo(deviceInfo);
            if (processedInfo == null)
                return false;
            
            // Send to real server endpoint
            bool success = await _apiService.SendDeviceManagementInfoAsync(processedInfo);
            
            if (success)
            {
                DeviceInfoUpdated?.Invoke(processedInfo);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }
    
    public async Task<List<DeviceManagementInfo>?> GetAllConnectedDevicesAsync()
    {
        // TODO: Server needs endpoint for getting list of connected devices
        // For now return empty list until server endpoint exists
        await Task.Delay(100);
        return new List<DeviceManagementInfo>();
    }
    
    public async Task<bool> AddConnectedDeviceAsync(string deviceName, string pollingStationId)
    {
        // TODO: Server needs endpoint for adding connected devices
        // For now simulate success after validation
        if (ValidateDeviceInput(deviceName, pollingStationId))
        {
            await Task.Delay(100);
            var deviceInfo = new DeviceManagementInfo
            {
                PollingStationID = pollingStationId,
                DeviceNames = new List<string> { deviceName },
                No_ConnectedDevices = 1
            };
            
            DeviceConnected?.Invoke(deviceInfo);
            return true;
        }
        
        return false;
    }
    
    public async Task<bool> RemoveConnectedDeviceAsync(string deviceName, string pollingStationId)
    {
        // TODO: Server needs endpoint for removing connected devices
        // For now simulate success after validation
        if (ValidateDeviceInput(deviceName, pollingStationId))
        {
            await Task.Delay(100);
            var deviceInfo = new DeviceManagementInfo
            {
                PollingStationID = pollingStationId,
                DeviceNames = new List<string> { deviceName }
            };
            
            DeviceDisconnected?.Invoke(deviceInfo);
            return true;
        }
        
        return false;
    }
    
    // ==========================================
    // DATA PROCESSING METHODS
    // ==========================================
    
    private DeviceManagementInfo? ProcessDeviceInfo(DeviceManagementInfo deviceInfo)
    {
        // Add validation and processing logic here
        if (string.IsNullOrEmpty(deviceInfo.PollingStationID))
            return null;
            
        // Update device count based on actual device names
        deviceInfo.No_ConnectedDevices = deviceInfo.DeviceNames?.Count ?? 0;
        
        return deviceInfo;
    }
    
    private List<DeviceManagementInfo> ProcessDeviceList(List<DeviceManagementInfo> devices)
    {
        // Filter, sort, or process the device list
        var processedDevices = new List<DeviceManagementInfo>();
        
        foreach (var device in devices)
        {
            var processed = ProcessDeviceInfo(device);
            if (processed != null)
                processedDevices.Add(processed);
        }
        
        return processedDevices;
    }
    
    private bool ValidateDeviceInput(string deviceName, string pollingStationId)
    {
        return !string.IsNullOrWhiteSpace(deviceName) && 
               !string.IsNullOrWhiteSpace(pollingStationId);
    }
    
    // ==========================================
    // LONG POLLING METHODS  
    // ==========================================
    
    public async Task<List<string>?> WaitForVoterRequestsAsync()
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            var response = await _apiService.WaitForVoterRequestsAsync();
            if (response?.Requests?.Any() == true)
            {
                // Trigger event for UI updates
                VoterRequestsReceived?.Invoke(response.Requests);
                return response.Requests;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error waiting for voter requests: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> GenerateAccessCodeForVoterAsync(string voterNIN)
    {
        try
        {
            ThrowIfNotAuthenticated();
            
            var success = await _apiService.GenerateAccessCodeAsync(voterNIN);
            if (success)
            {
                // Since we don't get the actual code back from the API,
                // we'll just notify that a code was generated for this voter
                AccessCodeGenerated?.Invoke($"Code generated for {voterNIN}");
            }
            return success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating access code: {ex.Message}");
            return false;
        }
    }
}