// ============================================================
//  SourceAFIS Fingerprint Benchmark
//  Measures: FAR  (False Acceptance Rate)
//            FRR  (False Rejection Rate)
//            EER  (Equal Error Rate)
//
//  Uses SourceAFIS 3.14.0 – identical library to the server.
//  Server operating threshold: 40.0
// ============================================================

using SourceAFIS;

// ── Configuration ────────────────────────────────────────────────────────────
const double SERVER_THRESHOLD     = 40.0;  // same value as in Server/Program.cs
const int    EER_STEPS            = 500;   // threshold sweep resolution
// 500 fingers → 124,750 impostor pairs – more than enough for a reliable EER.
const int    MAX_IMPOSTOR_FINGERS = 500;

var cwd = Directory.GetCurrentDirectory();

Console.WriteLine();
Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║       SourceAFIS Fingerprint Benchmark                        ║");
Console.WriteLine("║       FAR / FRR / EER  |  SourceAFIS 3.14.0                  ║");
Console.WriteLine("║       Server threshold: 40.0                                  ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// ── SOCOFing ──────────────────────────────────────────────────────────────────
var socoRealDir = Path.Combine(cwd, "data", "SOCOFing", "Real");
if (Directory.Exists(socoRealDir))
{
    Console.WriteLine("════════════════════════════════════════════════════════════════════════");
    Console.WriteLine("  DATASET: SOCOFing  (Real vs Altered – stress test with damaged prints)");
    Console.WriteLine("════════════════════════════════════════════════════════════════════════");
    Console.WriteLine();

    var socoDir = Path.Combine(cwd, "data", "SOCOFing");
    var altDirs = new[]
    {
        Path.Combine(socoDir, "Altered", "Altered-Easy"),
        Path.Combine(socoDir, "Altered", "Altered-Medium"),
        Path.Combine(socoDir, "Altered", "Altered-Hard"),
    };

    Console.WriteLine("Scanning Real images...");
    var fingerData = Directory
        .GetFiles(socoRealDir, "*.*")
        .Where(IsImageFile)
        .ToDictionary(
            f => Path.GetFileNameWithoutExtension(f),
            f => (RealPath: f, AlteredPaths: new List<string>()));
    Console.WriteLine($"  Real images:      {fingerData.Count}");

    Console.WriteLine("Scanning Altered images...");
    int altCount = 0;
    foreach (var altDir in altDirs)
    {
        if (!Directory.Exists(altDir)) continue;
        foreach (var f in Directory.GetFiles(altDir, "*.*").Where(IsImageFile))
        {
            var name   = Path.GetFileNameWithoutExtension(f);
            var lastUs = name.LastIndexOf('_');
            if (lastUs < 0) continue;
            var key = name[..lastUs];
            if (fingerData.TryGetValue(key, out var entry)) { entry.AlteredPaths.Add(f); altCount++; }
        }
    }
    Console.WriteLine($"  Altered images:   {altCount}");

    var validSoco = fingerData
        .Where(kv => kv.Value.AlteredPaths.Count > 0)
        .ToDictionary(kv => kv.Key, kv => kv.Value);
    Console.WriteLine($"  Valid fingers:    {validSoco.Count}");
    Console.WriteLine();

    if (validSoco.Count >= 2)
        await RunBenchmarkAsync("SOCOFing", validSoco, SERVER_THRESHOLD, EER_STEPS, MAX_IMPOSTOR_FINGERS, cwd);
    else
        Console.Error.WriteLine("SKIPPED: Need at least 2 valid fingers.");
    Console.WriteLine();
}
else
{
    Console.WriteLine($"SOCOFing not found at {socoRealDir} – skipping.");
    Console.WriteLine();
}

// ── FVC2002 (all DBs combined into one benchmark) ────────────────────────────
var fvcRoots = new[]
{
    Path.Combine(cwd, "data", "archive", "fingerprints", "DB1_B"),
    Path.Combine(cwd, "data", "archive", "fingerprints", "DB2_B"),
    Path.Combine(cwd, "data", "archive", "fingerprints", "DB3_B"),
    Path.Combine(cwd, "data", "archive", "fingerprints", "DB4_B"),
};

var presentFvcRoots = fvcRoots.Where(Directory.Exists).ToList();

if (presentFvcRoots.Count > 0)
{
    Console.WriteLine("════════════════════════════════════════════════════════════════════════");
    Console.WriteLine($"  DATASET: FVC2002  (combined: {string.Join(", ", presentFvcRoots.Select(Path.GetFileName))})");
    Console.WriteLine("════════════════════════════════════════════════════════════════════════");
    Console.WriteLine();

    // Collect fingers from every DB; prefix the key with the DB name so there
    // are no collisions between e.g. DB1_B/101_1.tif and DB2_B/101_1.tif
    var allFvcFingers = new Dictionary<string, (string RealPath, List<string> AlteredPaths)>();

    foreach (var fvcDir in presentFvcRoots)
    {
        var dbName = Path.GetFileName(fvcDir);
        Console.WriteLine($"  Scanning {dbName}...");

        var groups = Directory
            .GetFiles(fvcDir, "*.*", SearchOption.AllDirectories)
            .Where(IsImageFile)
            .Where(f =>
            {
                var parts = Path.GetFileNameWithoutExtension(f).Split('_');
                return parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _);
            })
            .GroupBy(f => Path.GetFileNameWithoutExtension(f).Split('_')[0])
            .Where(g => g.Count() >= 2);

        foreach (var g in groups)
        {
            var sorted = g.OrderBy(f =>
            {
                int.TryParse(Path.GetFileNameWithoutExtension(f).Split('_')[1], out int n);
                return n;
            }).ToList();
            // Prefix key with DB name to avoid collisions
            allFvcFingers[$"{dbName}_{g.Key}"] = (sorted[0], sorted.Skip(1).ToList());
        }

        Console.WriteLine($"    Fingers from {dbName}: {allFvcFingers.Count(kv => kv.Key.StartsWith(dbName))}");
    }

    Console.WriteLine($"  Total fingers combined: {allFvcFingers.Count}");
    Console.WriteLine($"  Total images combined:  {allFvcFingers.Values.Sum(v => 1 + v.AlteredPaths.Count)}");
    Console.WriteLine();

    if (allFvcFingers.Count >= 2)
        await RunBenchmarkAsync("FVC2002_Combined", allFvcFingers, SERVER_THRESHOLD, EER_STEPS, MAX_IMPOSTOR_FINGERS, cwd);
    else
        Console.Error.WriteLine("SKIPPED: Not enough finger groups found.");
    Console.WriteLine();
}

return 0;

// ═══════════════════════════════════════════════════════════════════════════════
// BENCHMARK RUNNER  (shared by SOCOFing and FVC2002)
// ═══════════════════════════════════════════════════════════════════════════════

static async Task RunBenchmarkAsync(
    string label,
    Dictionary<string, (string RealPath, List<string> AlteredPaths)> fingers,
    double serverThreshold, int eerSteps, int maxImpostorFingers, string cwd)
{
    int totalImages = fingers.Values.Sum(v => 1 + v.AlteredPaths.Count);
    Console.WriteLine($"Extracting {totalImages} templates...");

    var realTemplates    = new Dictionary<string, FingerprintTemplate>(fingers.Count);
    var alteredTemplates = new Dictionary<string, List<FingerprintTemplate>>(fingers.Count);
    int processed = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    foreach (var (key, (realPath, altPaths)) in fingers)
    {
        realTemplates[key] = new FingerprintTemplate(new FingerprintImage(File.ReadAllBytes(realPath)));
        processed++;

        var altList = new List<FingerprintTemplate>(altPaths.Count);
        foreach (var ap in altPaths)
        {
            altList.Add(new FingerprintTemplate(new FingerprintImage(File.ReadAllBytes(ap))));
            processed++;
        }
        alteredTemplates[key] = altList;

        if (processed % 200 == 0 || processed == totalImages)
        {
            double rate = sw.Elapsed.TotalSeconds > 0 ? processed / sw.Elapsed.TotalSeconds : 1;
            double eta  = (totalImages - processed) / rate;
            Console.Write($"\r  {processed}/{totalImages} ({processed * 100 / totalImages}%)  ETA {eta:F0}s   ");
        }
    }
    sw.Stop();
    Console.WriteLine($"\r  Done in {sw.Elapsed.TotalSeconds:F1}s.                              ");
    Console.WriteLine();

    Console.WriteLine("Computing genuine match scores...");
    var genuineScores = new List<double>(fingers.Count * 3);
    foreach (var (key, realTmpl) in realTemplates)
    {
        var matcher = new FingerprintMatcher(realTmpl);
        foreach (var altTmpl in alteredTemplates[key])
            genuineScores.Add(matcher.Match(altTmpl));
    }
    Console.WriteLine($"  Genuine pairs:    {genuineScores.Count}");

    Console.WriteLine($"Computing impostor match scores (first {maxImpostorFingers} fingers)...");
    var impostorKeys   = realTemplates.Keys.OrderBy(k => k).Take(maxImpostorFingers).ToList();
    var impostorScores = new List<double>(impostorKeys.Count * (impostorKeys.Count - 1) / 2);
    for (int i = 0; i < impostorKeys.Count; i++)
    {
        var matcher = new FingerprintMatcher(realTemplates[impostorKeys[i]]);
        for (int j = i + 1; j < impostorKeys.Count; j++)
            impostorScores.Add(matcher.Match(realTemplates[impostorKeys[j]]));
        if (i % 50 == 0 || i == impostorKeys.Count - 1)
            Console.Write($"\r  Progress: {i + 1}/{impostorKeys.Count} probes   ");
    }
    Console.WriteLine($"\r  Impostor pairs:   {impostorScores.Count}                ");
    Console.WriteLine();

    Console.WriteLine("Building FAR/FRR curve...");
    double minScore = Math.Min(genuineScores.Min(), impostorScores.Min());
    double maxScore = Math.Max(genuineScores.Max(), impostorScores.Max());
    double stepSize = (maxScore - minScore) / eerSteps;

    var curve = new List<(double threshold, double far, double frr)>(eerSteps + 1);
    for (int k = 0; k <= eerSteps; k++)
    {
        double t   = minScore + k * stepSize;
        double far = impostorScores.Count(s => s >= t) / (double)impostorScores.Count;
        double frr = genuineScores.Count(s => s <  t) / (double)genuineScores.Count;
        curve.Add((t, far, frr));
    }

    var eerPoint       = curve.OrderBy(p => Math.Abs(p.far - p.frr)).First();
    double eer         = (eerPoint.far + eerPoint.frr) / 2.0;
    double farAtServer = impostorScores.Count(s => s >= serverThreshold) / (double)impostorScores.Count;
    double frrAtServer = genuineScores.Count(s  => s <  serverThreshold) / (double)genuineScores.Count;

    Console.WriteLine();
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════════╗");
    Console.WriteLine($"║  RESULTS: {label,-60}║");
    Console.WriteLine("╠══════════════════════════════════════════════════════════════════════╣");
    Console.WriteLine($"║  Fingers: {fingers.Count,-6}  Images: {totalImages,-6}  Genuine pairs: {genuineScores.Count,-6}  Impostor pairs: {impostorScores.Count,-6}║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════╝");

    Console.WriteLine();
    Console.WriteLine("  ┌──────────────┬───────────┬───────────┬──────────────────┐");
    Console.WriteLine("  │ Type         │    Min    │    Max    │    Average       │");
    Console.WriteLine("  ├──────────────┼───────────┼───────────┼──────────────────┤");
    Console.WriteLine($"  │ Genuine      │ {genuineScores.Min(),9:F2} │ {genuineScores.Max(),9:F2} │ {genuineScores.Average(),16:F4} │");
    Console.WriteLine($"  │ Impostor     │ {impostorScores.Min(),9:F2} │ {impostorScores.Max(),9:F2} │ {impostorScores.Average(),16:F4} │");
    Console.WriteLine("  └──────────────┴───────────┴───────────┴──────────────────┘");

    Console.WriteLine();
    Console.WriteLine("  ┌──────────────────────────────────┬──────────────────────┐");
    Console.WriteLine("  │ Metric                           │ Value                │");
    Console.WriteLine("  ├──────────────────────────────────┼──────────────────────┤");
    Console.WriteLine($"  │ Equal Error Rate (EER)           │ {eer * 100,8:F4} %          │");
    Console.WriteLine($"  │ EER Threshold                    │ {eerPoint.threshold,8:F2}             │");
    Console.WriteLine($"  │ FAR at EER                       │ {eerPoint.far * 100,8:F4} %          │");
    Console.WriteLine($"  │ FRR at EER                       │ {eerPoint.frr * 100,8:F4} %          │");
    Console.WriteLine("  ├──────────────────────────────────┼──────────────────────┤");
    Console.WriteLine($"  │ Server Threshold                 │ {serverThreshold,8:F1}             │");
    Console.WriteLine($"  │ FAR at server threshold          │ {farAtServer * 100,8:F4} %          │");
    Console.WriteLine($"  │ FRR at server threshold          │ {frrAtServer * 100,8:F4} %          │");
    Console.WriteLine("  └──────────────────────────────────┴──────────────────────┘");

    Console.WriteLine();
    Console.WriteLine("  ┌─────────────┬────────────┬────────────┬──────────────────────────┐");
    Console.WriteLine("  │  Threshold  │   FAR (%)  │   FRR (%)  │  Note                    │");
    Console.WriteLine("  ├─────────────┼────────────┼────────────┼──────────────────────────┤");

    int printStep = Math.Max(1, eerSteps / 25);
    var printIndices = Enumerable.Range(0, eerSteps + 1).Where(k => k % printStep == 0).ToHashSet();
    printIndices.Add(curve.Select((p, i) => (i, Math.Abs(p.threshold - eerPoint.threshold))).MinBy(x => x.Item2).i);
    printIndices.Add(curve.Select((p, i) => (i, Math.Abs(p.threshold - serverThreshold))).MinBy(x => x.Item2).i);

    foreach (int k in printIndices.OrderBy(x => x))
    {
        var (t, far, frr) = curve[k];
        string note = "";
        if (Math.Abs(t - eerPoint.threshold) < stepSize * 0.6)        note = "◄ EER point";
        else if (Math.Abs(t - serverThreshold) < stepSize * 0.6)      note = "◄ Server threshold";
        Console.WriteLine($"  │ {t,11:F2} │ {far * 100,10:F4} │ {frr * 100,10:F4} │  {note,-24}│");
    }
    Console.WriteLine("  └─────────────┴────────────┴────────────┴──────────────────────────┘");
    Console.WriteLine();

    var csvPath = Path.Combine(cwd, $"benchmark_{label}.csv");
    await SaveCsvAsync(csvPath, curve, genuineScores, impostorScores, eer, eerPoint.threshold, serverThreshold, farAtServer, frrAtServer);
    Console.WriteLine($"  Results saved to: {csvPath}");
}

// ═══════════════════════════════════════════════════════════════════════════════
// HELPERS
// ═══════════════════════════════════════════════════════════════════════════════

static bool IsImageFile(string path)
{
    var ext = Path.GetExtension(path).ToLowerInvariant();
    return ext is ".png" or ".bmp" or ".tif" or ".tiff";
}

static async Task SaveCsvAsync(
    string path,
    List<(double threshold, double far, double frr)> curve,
    List<double> genuineScores,
    List<double> impostorScores,
    double eer,
    double eerThreshold,
    double serverThreshold,
    double farAtServer,
    double frrAtServer)
{
    await using var w = new StreamWriter(path);

    // Summary header
    await w.WriteLineAsync("# SourceAFIS Benchmark Results");
    await w.WriteLineAsync($"# EER,{eer * 100:F6},%");
    await w.WriteLineAsync($"# EER_threshold,{eerThreshold:F4}");
    await w.WriteLineAsync($"# Server_threshold,{serverThreshold:F4}");
    await w.WriteLineAsync($"# FAR_at_server_threshold,{farAtServer * 100:F6},%");
    await w.WriteLineAsync($"# FRR_at_server_threshold,{frrAtServer * 100:F6},%");
    await w.WriteLineAsync($"# Genuine_pairs,{genuineScores.Count}");
    await w.WriteLineAsync($"# Impostor_pairs,{impostorScores.Count}");
    await w.WriteLineAsync();

    // FAR/FRR curve
    await w.WriteLineAsync("Threshold,FAR,FRR");
    foreach (var (t, far, frr) in curve)
        await w.WriteLineAsync($"{t:F6},{far:F8},{frr:F8}");

    await w.WriteLineAsync();

    // Raw scores
    await w.WriteLineAsync("ScoreType,Score");
    foreach (var s in genuineScores)
        await w.WriteLineAsync($"Genuine,{s:F6}");
    foreach (var s in impostorScores)
        await w.WriteLineAsync($"Impostor,{s:F6}");
}
