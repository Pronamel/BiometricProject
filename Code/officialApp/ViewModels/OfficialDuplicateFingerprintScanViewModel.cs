using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using officialApp.Services;

namespace officialApp.ViewModels;

public partial class OfficialDuplicateFingerprintScanViewModel : ViewModelBase
{
    [ObservableProperty]
    private string statusMessage = "Press Start scan to run duplicate fingerprint checks across all voters.";

    [ObservableProperty]
    private string statusColor = "#555555";

    [ObservableProperty]
    private bool isScanning;

    [ObservableProperty]
    private int comparisonsPerformed;

    [ObservableProperty]
    private int suspiciousRecordCount;

    [ObservableProperty]
    private int matchedGroupCount;

    public ObservableCollection<string> DuplicateGroups { get; } = new ObservableCollection<string>();

    private readonly IServerHandler _serverHandler;
    private readonly INavigationService _navigationService;

    public OfficialDuplicateFingerprintScanViewModel(IServerHandler serverHandler, INavigationService navigationService)
    {
        _serverHandler = serverHandler;
        _navigationService = navigationService;
    }

    [RelayCommand]
    private async Task StartScan()
    {
        if (IsScanning)
        {
            return;
        }

        try
        {
            IsScanning = true;
            StatusMessage = "Running duplicate fingerprint scan...";
            StatusColor = "#3498db";
            DuplicateGroups.Clear();
            ComparisonsPerformed = 0;
            SuspiciousRecordCount = 0;
            MatchedGroupCount = 0;

            var response = await _serverHandler.ScanDuplicateVoterFingerprintsAsync();
            if (response == null)
            {
                StatusMessage = "No response from server.";
                StatusColor = "#e74c3c";
                return;
            }

            ComparisonsPerformed = response.ComparisonsPerformed;
            SuspiciousRecordCount = response.SuspiciousRecordCount;
            MatchedGroupCount = response.MatchedGroupCount;

            if (response.DuplicateIdentityGroups != null && response.DuplicateIdentityGroups.Count > 0)
            {
                int groupNumber = 1;
                foreach (var identityGroup in response.DuplicateIdentityGroups)
                {
                    if (identityGroup == null || identityGroup.Count == 0)
                    {
                        continue;
                    }

                    var lines = identityGroup
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToList();

                    if (lines.Count == 0)
                    {
                        continue;
                    }

                    DuplicateGroups.Add($"Group {groupNumber}\n" + string.Join("\n", lines));
                    groupNumber++;
                }
            }
            else if (response.DuplicateSdiGroups != null)
            {
                foreach (var group in response.DuplicateSdiGroups
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .OrderBy(g => g, StringComparer.OrdinalIgnoreCase))
                {
                    DuplicateGroups.Add(group);
                }
            }

            if (response.Success)
            {
                StatusMessage = response.Message;
                StatusColor = "#27ae60";
            }
            else
            {
                StatusMessage = response.Message;
                StatusColor = "#e74c3c";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan failed: {ex.Message}";
            StatusColor = "#e74c3c";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        _navigationService.NavigateToOfficialMenu();
    }
}
