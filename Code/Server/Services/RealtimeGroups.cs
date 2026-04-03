namespace Server.Services;

public static class RealtimeGroups
{
    public static string County(string county) => $"county:{county}";

    public static string CountyConstituency(string county, string constituency) =>
        $"county:{county}:constituency:{constituency}";

    public static string Official(string officialId) => $"official:{officialId}";

    public static string Voter(string voterId) => $"voter:{voterId}";

    public static string VoterDevice(string voterId, string deviceId) =>
        $"voter:{voterId}:device:{deviceId}";
}
