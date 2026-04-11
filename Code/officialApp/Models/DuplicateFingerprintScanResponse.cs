using System.Collections.Generic;

namespace officialApp.Models;

public class DuplicateFingerprintScanResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int TotalVotersConsidered { get; set; }
    public int ComparableVoters { get; set; }
    public int ComparisonsPerformed { get; set; }
    public int MatchedGroupCount { get; set; }
    public int SuspiciousRecordCount { get; set; }
    public int FailedDecryptions { get; set; }
    public List<string> DuplicateSdiGroups { get; set; } = new List<string>();
    public List<List<string>> DuplicateIdentityGroups { get; set; } = new List<List<string>>();
}
