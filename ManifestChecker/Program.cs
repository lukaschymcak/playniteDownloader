using SteamKit2;
using Newtonsoft.Json;

// 1. Parse command-line args — each arg is a string AppID like "730"
var rawArgs = ArgsToAppIds(args);
if (rawArgs.Count == 0)
{
    Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = "No AppIDs provided" }));
    Environment.Exit(1);
    return;
}

// 2. Validate and convert to uint
var appIds = new List<uint>();
foreach (var arg in rawArgs)
{
    if (!uint.TryParse(arg, out var id))
    {
        Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = $"Invalid AppID: {arg}" }));
        Environment.Exit(1);
        return;
    }
    appIds.Add(id);
}

var appIdsStr = rawArgs; // Keep string versions for output

// 3. SteamKit2 connection
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
var steamClient = new SteamClient();
var manager = new CallbackManager(steamClient);
var steamUserHandler = steamClient.GetHandler<SteamUser>();
var steamAppsHandler = steamClient.GetHandler<SteamApps>();
if (steamUserHandler is null || steamAppsHandler is null)
{
    Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = "SteamKit handlers not available" }));
    Environment.Exit(1);
    return;
}

var loggedOn = false;
var failed = false;
string? failReason = null;
var results = new List<object>();

// Register callbacks
manager.Subscribe<SteamClient.ConnectedCallback>(callback =>
{
    steamUserHandler.LogOnAnonymous();
});

manager.Subscribe<SteamClient.DisconnectedCallback>(callback =>
{
    if (!loggedOn)
    {
        failed = true;
        failReason = "Disconnected before login";
    }
});

manager.Subscribe<SteamUser.LoggedOnCallback>(callback =>
{
    if (callback.Result != EResult.OK)
    {
        failed = true;
        failReason = $"Failed to login to Steam anonymously: {callback.Result}";
        return;
    }
    loggedOn = true;
});

manager.Subscribe<SteamUser.LoggedOffCallback>(callback =>
{
    failed = true;
    failReason = $"Logged off: {callback.Result}";
});

// Connect
steamClient.Connect();

// Wait for login with timeout
var startTime = DateTime.UtcNow;
while (!loggedOn && !failed)
{
    if (DateTime.UtcNow - startTime > TimeSpan.FromSeconds(15))
    {
        Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = "Timed out waiting for Steam login" }));
        Environment.Exit(1);
        return;
    }
    manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
}

if (failed)
{
    Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = failReason ?? "Failed to login to Steam anonymously" }));
    Environment.Exit(1);
    return;
}

// 4. Fetch product info in batches of 20
var batchSize = 20;
SteamApps.PICSProductInfoCallback? picsResult = null;
manager.Subscribe<SteamApps.PICSProductInfoCallback>(callback =>
{
    picsResult = callback;
});

for (int i = 0; i < appIds.Count; i += batchSize)
{
    if (cts.Token.IsCancellationRequested) break;

    var batch = appIds.Skip(i).Take(batchSize).ToList();
    var batchStr = appIdsStr.Skip(i).Take(batchSize).ToList();

    var picRequests = batch.Select(id => new SteamApps.PICSRequest(id)).ToList();

    picsResult = null;
    var job = steamAppsHandler.PICSGetProductInfo(picRequests, Enumerable.Empty<SteamApps.PICSRequest>());
    job.Timeout = TimeSpan.FromSeconds(10);

    // Wait for response
    var fetchStart = DateTime.UtcNow;
    while (picsResult == null && !failed)
    {
        if (DateTime.UtcNow - fetchStart > TimeSpan.FromSeconds(10))
        {
            Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = "Timed out waiting for Steam response" }));
            Environment.Exit(1);
            return;
        }
        manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
    }

    if (picsResult == null)
    {
        Console.Error.WriteLine(JsonConvert.SerializeObject(new { error = "Failed to get product info from Steam" }));
        Environment.Exit(1);
        return;
    }

    // Process results — match apps by AppID rather than index
    foreach (var app in picsResult.Apps)
    {
        var appIdStr = app.Key.ToString();
        var kv = app.Value.KeyValues;

        var depots = kv["depots"];
        if (depots == KeyValue.Invalid) continue;
        var buildId = ResolveBuildId(kv, depots);

        foreach (var depot in depots.Children)
        {
            // Skip non-numeric depot keys (e.g. "branches", "overrideversionid")
            if (!uint.TryParse(depot.Name, out _)) continue;

            var manifests = depot["manifests"];
            if (manifests == KeyValue.Invalid) continue;

            var pub = manifests["public"];
            if (pub == KeyValue.Invalid) continue;

            var gid = pub["gid"]?.Value;
            if (!string.IsNullOrEmpty(gid) && gid != "0")
            {
                var depotBuildId = pub["buildid"]?.Value;
                results.Add(new { appId = appIdStr, depotId = depot.Name, manifestGid = gid, buildId = buildId, depotBuildId = depotBuildId });
            }
        }
    }

    // Rate limiting between batches
    if (i + batchSize < appIds.Count)
    {
        Thread.Sleep(300);
    }
}

// 5. Log off and disconnect
steamUserHandler.LogOff();

// Wait for disconnect
var disconnectStart = DateTime.UtcNow;
manager.Subscribe<SteamClient.DisconnectedCallback>(callback => { });
while (DateTime.UtcNow - disconnectStart < TimeSpan.FromSeconds(2))
{
    manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
}

steamClient.Disconnect();

// 6. Output results
Console.WriteLine(JsonConvert.SerializeObject(results, Formatting.None));
Environment.Exit(0);

static List<string> ArgsToAppIds(string[] args)
{
    return args.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
}

static string? ResolveBuildId(KeyValue appKv, KeyValue depotsKv)
{
    // Primary path used by most apps.
    var buildId = depotsKv["branches"]?["public"]?["buildid"]?.Value;
    if (!string.IsNullOrWhiteSpace(buildId) && buildId != "0")
        return buildId;

    // Fallback: first non-zero buildid from any named branch.
    var branches = depotsKv["branches"];
    if (branches != KeyValue.Invalid)
    {
        foreach (var branch in branches.Children)
        {
            var candidate = branch["buildid"]?.Value;
            if (!string.IsNullOrWhiteSpace(candidate) && candidate != "0")
                return candidate;
        }
    }

    // Fallback sometimes exposed in common.
    var commonBuild = appKv["common"]?["buildid"]?.Value;
    if (!string.IsNullOrWhiteSpace(commonBuild) && commonBuild != "0")
        return commonBuild;

    return null;
}
