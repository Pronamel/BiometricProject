// ============================================================
//  SignalR Load Test
//  Tests concurrent connection capacity of the VotingHub.
//
//  HOW TO USE:
//  1. Start the server on EC2
//  2. Ensure AWS credentials are configured locally
//     (aws configure, or run on EC2 with an instance role)
//  3. Set SERVER_URL below if needed
//  4. Run:  dotnet run --configuration Release
// ============================================================

using Microsoft.AspNetCore.SignalR.Client;
using System.Diagnostics;
using System.Text;

// ── Configuration ────────────────────────────────────────────
const string SERVER_URL      = "https://34-238-14-248.nip.io/hubs/voting";
const string SECRET_NAME     = "jwt-secret";
const string AWS_REGION      = "us-east-1";

// Connection levels to test
int[] RAMP_LEVELS     = { 100, 250, 500, 1000, 2000 };
int   HOLD_SECONDS    = 30;   // how long to hold each level
int   CONNECT_TIMEOUT = 15;   // seconds before a connection attempt is considered failed
// ─────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║   SignalR Load Test  –  VotingHub                         ║");
Console.WriteLine($"║   Target: {SERVER_URL,-48}║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── Get JWT secret from args or prompt ───────────────────────────
string jwtSecret;
if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
{
    jwtSecret = args[0].Trim();
    Console.WriteLine("Secret received from argument.");
}
else
{
    Console.Write("Enter JWT secret: ");
    Console.ForegroundColor = ConsoleColor.Black;
    jwtSecret = Console.ReadLine()?.Trim() ?? string.Empty;
    Console.ResetColor();
}

if (string.IsNullOrWhiteSpace(jwtSecret))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine("ERROR: No secret entered. Aborting.");
    Console.ResetColor();
    return 1;
}
Console.WriteLine($"Secret length: {jwtSecret.Length} chars.");
Console.WriteLine("Secret received.");

// ── Fetch a single reusable test token from the server ───────
Console.Write("Fetching test token from server...");
string JWT_TOKEN;
{
    using var httpClient = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    var body = new System.Net.Http.StringContent(
        System.Text.Json.JsonSerializer.Serialize(new { key = jwtSecret }),
        Encoding.UTF8, "application/json");
    var response = await httpClient.PostAsync("https://34-238-14-248.nip.io/auth/load-test-token", body);
    if (!response.IsSuccessStatusCode)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\nERROR: Token endpoint returned {(int)response.StatusCode}. Check JWT secret and server deployment.");
        Console.ResetColor();
        return 1;
    }
    var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    JWT_TOKEN = json.RootElement.GetProperty("token").GetString()!;
}
Console.WriteLine(" OK");
Console.WriteLine($"Test token received from server (expires in 8h).");
Console.WriteLine();

// ── Preflight: check negotiate endpoint before running levels ─
Console.Write("Preflight: testing negotiate endpoint...");
{
    using var diagClient = new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    try
    {
        var negotiateUrl = $"https://34-238-14-248.nip.io/hubs/voting/negotiate?negotiateVersion=1&access_token={JWT_TOKEN}";
        var resp = await diagClient.PostAsync(negotiateUrl, null);
        var body = await resp.Content.ReadAsStringAsync();
        if (resp.IsSuccessStatusCode)
            Console.WriteLine($" OK ({(int)resp.StatusCode})");
        else
            Console.WriteLine($"\n  [PREFLIGHT FAIL] Negotiate returned {(int)resp.StatusCode}: {body}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n  [PREFLIGHT ERROR] {ex.Message}");
    }
}
Console.WriteLine();

var results = new List<(int target, int connected, int failed, double avgConnectMs, double holdMs)>();

foreach (var targetConnections in RAMP_LEVELS)
{
    Console.WriteLine($"─── Level: {targetConnections} connections ───────────────────────────");

    var connections = new List<HubConnection>(targetConnections);
    int connected   = 0;
    int failed      = 0;
    var connectTimes = new System.Collections.Concurrent.ConcurrentBag<double>();

    // Open connections in parallel
    var sw = Stopwatch.StartNew();
    await Parallel.ForEachAsync(
        Enumerable.Range(0, targetConnections),
        new ParallelOptions { MaxDegreeOfParallelism = 50 },
        async (_, ct) =>
        {
            var hub = new HubConnectionBuilder()
                .WithUrl($"{SERVER_URL}?access_token={JWT_TOKEN}", options =>
                {
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    // Accept self-signed certs
                    options.HttpMessageHandlerFactory = _ =>
                        new HttpClientHandler
                        {
                            ServerCertificateCustomValidationCallback =
                                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                        };
                })
                .Build();

            var connSw = Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECT_TIMEOUT));
                await hub.StartAsync(cts.Token);
                connSw.Stop();
                connectTimes.Add(connSw.Elapsed.TotalMilliseconds);
                lock (connections) connections.Add(hub);
                Interlocked.Increment(ref connected);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                if (failed <= 3) // only print first 3 errors to avoid spam
                    Console.WriteLine($"\n  [ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        });

    Console.WriteLine($"  Connected:  {connected} / {targetConnections}");
    Console.WriteLine($"  Failed:     {failed}");
    double avgMs = connectTimes.Count > 0 ? connectTimes.Average() : 0;
    Console.WriteLine($"  Avg connect time: {avgMs:F0} ms");
    Console.Write($"  Holding for {HOLD_SECONDS}s...");

    var holdSw = Stopwatch.StartNew();
    await Task.Delay(TimeSpan.FromSeconds(HOLD_SECONDS));
    holdSw.Stop();

    // Count how many are still connected after the hold
    int stillConnected = connections.Count(c => c.State == HubConnectionState.Connected);
    Console.WriteLine($" done. Still connected: {stillConnected} / {connected}");
    Console.WriteLine();

    results.Add((targetConnections, connected, failed, avgMs, holdSw.Elapsed.TotalMilliseconds));

    // Disconnect all before next level
    Console.Write("  Disconnecting...");
    await Parallel.ForEachAsync(connections, async (c, _) =>
    {
        try { await c.StopAsync(); await c.DisposeAsync(); } catch { }
    });
    Console.WriteLine(" done.");
    Console.WriteLine();
}

// ── Summary ──────────────────────────────────────────────────
Console.WriteLine("╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("║  RESULTS SUMMARY                                          ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
Console.WriteLine("║  Target  │ Connected │  Failed  │ Avg Connect │ Hold     ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════════╣");
foreach (var (target, conn, fail, avgMs, holdMs) in results)
{
    Console.WriteLine($"║  {target,6}  │  {conn,7}  │  {fail,6}  │  {avgMs,7:F0} ms  │ {holdMs / 1000,5:F1}s  ║");
}
Console.WriteLine("╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();
return 0;
