
using System.Diagnostics;
using System.Text.Json;
using StackExchange.Redis;

// =====================================
// Lecture 6 - Redis Journey Search
// =====================================

var start = DateTimeOffset.Parse("2026-10-02T06:00:00Z");
var end = DateTimeOffset.Parse("2026-10-02T07:00:00Z");

var morning = new SearchRequest(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    start,
    end
);

var evening = morning with
{
    Start = DateTimeOffset.Parse("2026-10-02T18:00:00Z"),
    End = DateTimeOffset.Parse("2026-10-02T19:00:00Z")
};

// =====================================
// Connect Redis and MongoDB
// =====================================

var options = ConfigurationOptions.Parse("127.0.0.1:6379");

options.AbortOnConnectFail = false;
options.ConnectTimeout = 500;
options.AsyncTimeout = 500;
options.ConnectRetry = 1;

using var redis =
    await ConnectionMultiplexer.ConnectAsync(options);

var cache = redis.GetDatabase();
var source = new MongoJourneySearch();

var cachedSearch = new CachedJourneySearch(
    source,
    cache,
    TimeSpan.FromSeconds(20)
);

// =====================================
// PART 2: Cache Key Tests
// COMPLETED - COMMENTED OUT
// =====================================

/*
Console.WriteLine("===== Cache Key Tests =====");

var reversed = morning with
{
    FromStopId = "STOP-AIRPORT",
    ToStopId = "STOP-NORREPORT"
};

var sameInstant = morning with
{
    Start = DateTimeOffset.Parse("2026-10-02T08:00:00+02:00"),
    End = DateTimeOffset.Parse("2026-10-02T09:00:00+02:00")
};

var pairA = morning with
{
    FromStopId = "A:B",
    ToStopId = "C"
};

var pairB = morning with
{
    FromStopId = "A",
    ToStopId = "B:C"
};

Console.WriteLine($"Different times: {Key(morning) != Key(evening)}");
Console.WriteLine($"Reversed stops: {Key(morning) != Key(reversed)}");
Console.WriteLine($"Same instant: {Key(morning) == Key(sameInstant)}");
Console.WriteLine($"Special characters: {Key(pairA) != Key(pairB)}");
*/

// =====================================
// PART 4: Cache Tests A, B, C, D
// COMPLETED - COMMENTED OUT
// =====================================

/*

// Test A: Cache HIT / MISS
await cache.KeyDeleteAsync(Key(morning));

var a1 = await cachedSearch.Search(morning);
int callsA1 = source.Calls;

var a2 = await cachedSearch.Search(morning);
int callsA2 = source.Calls;

bool testA =
    a1.Count == 1 &&
    a1[0].TripId == "LAB05-T-OK" &&
    a1.SequenceEqual(a2) &&
    callsA2 == callsA1;

Console.WriteLine($"Test A passed: {testA}");


// Test B: Cache Empty Result
await cache.KeyDeleteAsync(Key(evening));

var b1 = await cachedSearch.Search(evening);
int callsB1 = source.Calls;

var b2 = await cachedSearch.Search(evening);
int callsB2 = source.Calls;

bool testB =
    b1.Count == 0 &&
    b2.Count == 0 &&
    callsB2 == callsB1 &&
    await cache.KeyExistsAsync(Key(evening));

Console.WriteLine($"Test B passed: {testB}");


// Test C: Cache Expiry
await cache.KeyDeleteAsync(Key(morning));

var c1 = await cachedSearch.Search(morning);
int callsC1 = source.Calls;

bool expireSet = await cache.KeyExpireAsync(
    Key(morning), TimeSpan.FromSeconds(1));

await Task.Delay(1500);

bool expired = !await cache.KeyExistsAsync(Key(morning));

var c2 = await cachedSearch.Search(morning);
int callsC2 = source.Calls;

var newTtl = await cache.KeyTimeToLiveAsync(Key(morning));

bool testC =
    expireSet && expired &&
    c1.SequenceEqual(c2) &&
    callsC2 == callsC1 + 1 &&
    newTtl.HasValue &&
    newTtl.Value > TimeSpan.Zero;

Console.WriteLine($"Test C passed: {testC}");


// Test D: Invalid Cache
await TestInvalidCache("D1 - Not JSON", "not-json");

var wrongVersion = JsonSerializer.Serialize(new
{
    schemaVersion = 1,
    cachedAtUtc = DateTimeOffset.UtcNow,
    items = Array.Empty<object>()
});

await TestInvalidCache("D2 - Wrong Version", wrongVersion);

var missingItems = JsonSerializer.Serialize(new
{
    schemaVersion = 2,
    cachedAtUtc = DateTimeOffset.UtcNow
});

await TestInvalidCache("D3 - Missing Items", missingItems);

*/

// =====================================
// PART 5: Cache Invalidation
// COMPLETED - COMMENTED OUT
// =====================================

/*

// Before prepare:
// docker compose exec -T mongo mongosh --quiet
//   --file /scripts/lecture05/reset.js

// Prepare: cache the scheduled departure
await cache.KeyDeleteAsync(Key(morning));

var longCache = new CachedJourneySearch(
    source,
    cache,
    TimeSpan.FromMinutes(5)
);

var prepared = await longCache.Search(morning);
PrintResult(prepared);

// Then run cancel_lab06.js in MongoDB.
// Verify after cancellation:

var mongoResult = await source.Fetch(morning);
var beforeDelete = await longCache.Search(morning);

bool staleCache =
    mongoResult.Count == 0 &&
    beforeDelete.Any(j => j.TripId == "LAB05-T-OK");

Console.WriteLine($"Stale cache: {staleCache}");

await cache.KeyDeleteAsync(Key(morning));

var afterDelete = await longCache.Search(morning);

Console.WriteLine($"After invalidation: {afterDelete.Count}");

*/

// =====================================
// PART 6: Failure Handling
// COMPLETED - COMMENTED OUT
// =====================================

/*

// Stop Redis: docker compose stop redis
// Then run the failure test.

var failureWatch = Stopwatch.StartNew();

try
{
    var result = await cachedSearch.Search(morning);
    PrintResult(result);

    Console.WriteLine("Search successful: True");
}
catch (Exception ex)
{
    Console.WriteLine($"Search error: {ex.Message}");
}
finally
{
    failureWatch.Stop();
    Console.WriteLine(
        $"Elapsed: {failureWatch.ElapsedMilliseconds} ms");
}

// Restore services:
// docker compose start mongo redis

*/

// =====================================
// PART 7: Concurrent Searches
// CURRENT EXPERIMENT
// =====================================

Console.WriteLine();
Console.WriteLine("===== PART 7: CONCURRENT SEARCHES =====");

string morningKey = Key(morning);

try
{
    // Step 1: Remove the previous cache
    await cache.KeyDeleteAsync(morningKey);

    Console.WriteLine("Morning cache deleted.");

    // Step 2: Simulate slow MongoDB
    source.DelayMilliseconds = 200;

    Console.WriteLine("MongoDB artificial delay: 200 ms");
    Console.WriteLine();

    // =====================================
    // Batch 1: 10 Concurrent Requests
    // =====================================

    Console.WriteLine("===== BATCH 1: COLD CACHE =====");

    int beforeBatch1 = source.Calls;

    var watch1 = Stopwatch.StartNew();

    var batch1 = await Task.WhenAll(
        Enumerable.Range(0, 10)
            .Select(_ => cachedSearch.Search(morning))
    );

    watch1.Stop();

    int callsBatch1 = source.Calls - beforeBatch1;

    Console.WriteLine();
    Console.WriteLine($"Requests: {batch1.Length}");
    Console.WriteLine($"MongoDB calls: {callsBatch1}");
    Console.WriteLine($"Elapsed: {watch1.ElapsedMilliseconds} ms");

    Console.WriteLine(
        $"All results valid: {batch1.All(
            r => r.Count == 1 &&
                 r[0].TripId == "LAB05-T-OK")}");

    // =====================================
    // Check Redis after Batch 1
    // =====================================

    var storedValue = await cache.StringGetAsync(morningKey);
    var ttl = await cache.KeyTimeToLiveAsync(morningKey);

    Console.WriteLine();
    Console.WriteLine($"Redis cache exists: {storedValue.HasValue}");
    Console.WriteLine($"Remaining TTL: {ttl?.TotalSeconds:F1} seconds");

    // =====================================
    // Batch 2: 10 Concurrent Requests
    // =====================================

    Console.WriteLine();
    Console.WriteLine("===== BATCH 2: WARM CACHE =====");

    int beforeBatch2 = source.Calls;

    var watch2 = Stopwatch.StartNew();

    var batch2 = await Task.WhenAll(
        Enumerable.Range(0, 10)
            .Select(_ => cachedSearch.Search(morning))
    );

    watch2.Stop();

    int callsBatch2 = source.Calls - beforeBatch2;

    Console.WriteLine();
    Console.WriteLine($"Requests: {batch2.Length}");
    Console.WriteLine($"MongoDB calls: {callsBatch2}");
    Console.WriteLine($"Elapsed: {watch2.ElapsedMilliseconds} ms");

    Console.WriteLine(
        $"All results valid: {batch2.All(
            r => r.Count == 1 &&
                 r[0].TripId == "LAB05-T-OK")}");

    // =====================================
    // Final Summary
    // =====================================

    Console.WriteLine();
    Console.WriteLine("===== PART 7 SUMMARY =====");

    Console.WriteLine($"Batch 1 MongoDB calls: {callsBatch1}");
    Console.WriteLine($"Batch 2 MongoDB calls: {callsBatch2}");
    Console.WriteLine($"Total MongoDB calls: {source.Calls}");

    bool testPassed =
        batch1.All(r => r.Count == 1 &&
                        r[0].TripId == "LAB05-T-OK") &&
        batch2.All(r => r.Count == 1 &&
                        r[0].TripId == "LAB05-T-OK") &&
        callsBatch1 >= 1 &&
        callsBatch2 == 0 &&
        storedValue.HasValue;

    Console.WriteLine($"Concurrent cache test passed: {testPassed}");

    if (!testPassed)
        Environment.ExitCode = 1;
}
catch (Exception ex)
{
    Console.WriteLine($"Part 7 failed: {ex.Message}");
    Environment.ExitCode = 1;
}
finally
{
    // Remove artificial database delay
    source.DelayMilliseconds = 0;

    Console.WriteLine();
    Console.WriteLine("MongoDB artificial delay removed.");
}

// =====================================
// HELPER FUNCTIONS
// =====================================

static string Key(SearchRequest request)
{
    return CachedJourneySearch.BuildCacheKey(request);
}

static void PrintResult(List<Journey> journeys)
{
    Console.WriteLine(JsonSerializer.Serialize(
        journeys,
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }
    ));
}

// Previous Test D helper (retained)
async Task TestInvalidCache(
    string testName,
    string invalidJson)
{
    Console.WriteLine($"===== {testName} =====");

    string key = Key(morning);

    await cache.StringSetAsync(
        key,
        invalidJson,
        TimeSpan.FromSeconds(20)
    );

    int before = source.Calls;

    var result = await cachedSearch.Search(morning);

    int after = source.Calls;

    var repairedValue = await cache.StringGetAsync(key);

    bool validReplacement = false;

    if (repairedValue.HasValue)
    {
        try
        {
            using var doc =
                JsonDocument.Parse(repairedValue.ToString());

            var root = doc.RootElement;

            validReplacement =
                root.GetProperty("schemaVersion").GetInt32() == 2 &&
                root.TryGetProperty("cachedAtUtc", out _) &&
                root.GetProperty("items").ValueKind ==
                    JsonValueKind.Array &&
                root.GetProperty("items").GetArrayLength() == 1;
        }
        catch (Exception)
        {
            validReplacement = false;
        }
    }

    bool passed =
        after == before + 1 &&
        result.Count == 1 &&
        result[0].TripId == "LAB05-T-OK" &&
        validReplacement;

    Console.WriteLine($"Cache repaired: {validReplacement}");
    Console.WriteLine($"{testName} passed: {passed}");
}
