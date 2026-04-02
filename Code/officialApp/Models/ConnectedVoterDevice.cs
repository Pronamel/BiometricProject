using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace officialApp.Models;

public partial class ConnectedVoterDevice : ObservableObject
{
    [ObservableProperty]
    private int deviceNumber;

    [ObservableProperty]
    private string status = "Idle";

    [ObservableProperty]
    private DateTime connectedAtTime;

    [ObservableProperty]
    private string deviceIdentifier = ""; // MAC address, IP, or device name

    [ObservableProperty]
    private DateTime lastStatusTime; // Track when device last sent status update
}
