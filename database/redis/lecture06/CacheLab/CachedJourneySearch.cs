
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

public sealed class CachedJourneySearch
{
    private readonly MongoJourneySearch source;
    private readonly IDatabase cache;
    private readonly TimeSpan expiry;

    private static readonly JsonSerializerOptions jsonOptions =
        new(JsonSerializerDefaults.Web);

    public CachedJourneySearch(
        MongoJourneySearch source,
        IDatabase cache,
        TimeSpan expiry)
    {
        this.source = source;
        this.cache = cache;
        this.expiry = expiry;
    }

    public static string BuildCacheKey(SearchRequest q)
    {
        if (string.IsNullOrWhiteSpace(q.CityId) ||
            string.IsNullOrWhiteSpace(q.FromStopId) ||
            string.IsNullOrWhiteSpace(q.ToStopId))
        {
            throw new ArgumentException("IDs cannot be empty");
        }

        if (q.End <= q.Start)
        {
            throw new ArgumentException("End must be after start");
        }

        static string Part(string value) =>
            Uri.EscapeDataString(value);

        static string Instant(DateTimeOffset value) =>
            value.UtcDateTime.ToString(
                "O", CultureInfo.InvariantCulture);

        return $"search:LAB06:v2:{Part(q.CityId)}:" +
               $"{Part(q.FromStopId)}:{Part(q.ToStopId)}:" +
               $"{Part(Instant(q.Start))}:{Part(Instant(q.End))}";
    }

    public async Task<List<Journey>> Search(SearchRequest request)
    {
        // 1. Validate and create cache key
        string key = BuildCacheKey(request);

        Console.WriteLine($"Cache Key: {key}");

        // 2. Try Redis first
        try
        {
            RedisValue cached = await cache.StringGetAsync(key);

            if (cached.HasValue)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<CacheEntry>(
                        cached.ToString(), jsonOptions);

                    if (IsValid(entry))
                    {
                        Console.WriteLine("Cache HIT");
                        return entry!.Items;
                    }
                }
                catch (JsonException)
                {
                    // Invalid JSON = cache miss
                }

                Console.WriteLine("Cache INVALID");
            }
        }
        catch (RedisException)
        {
            Console.WriteLine("Cache unavailable");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Cache unavailable");
        }

        Console.WriteLine("Cache MISS");

        // 3. Fetch real data from MongoDB
        // MongoDB errors must not be hidden.
        List<Journey> result = await source.Fetch(request);

        // 4. Store result in Redis
        var newEntry = new CacheEntry
        {
            SchemaVersion = 2,
            CachedAtUtc = DateTimeOffset.UtcNow,
            Items = result
        };

        string json = JsonSerializer.Serialize(newEntry, jsonOptions);

        try
        {
            await cache.StringSetAsync(key, json, expiry);
        }
        catch (RedisException)
        {
            Console.WriteLine("Cache write unavailable");
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Cache write unavailable");
        }

        // 5. Return MongoDB result
        return result;
    }

    private static bool IsValid(CacheEntry? entry)
    {
        if (entry == null ||
            entry.SchemaVersion != 2 ||
            entry.CachedAtUtc == default ||
            entry.CachedAtUtc > DateTimeOffset.UtcNow ||
            entry.Items == null)
        {
            return false;
        }

        return entry.Items.All(j =>
            j != null &&
            !string.IsNullOrWhiteSpace(j.CityId) &&
            !string.IsNullOrWhiteSpace(j.RouteId) &&
            !string.IsNullOrWhiteSpace(j.FromStopId) &&
            !string.IsNullOrWhiteSpace(j.ToStopId) &&
            !string.IsNullOrWhiteSpace(j.TripId) &&
            j.DepartureUtc != default &&
            j.ArrivalUtc >= j.DepartureUtc &&
            j.AvailableSeats >= 0 &&
            j.Price >= 0 &&
            !string.IsNullOrWhiteSpace(j.Currency));
    }

    public sealed class CacheEntry
    {
        [JsonRequired]
        public int SchemaVersion { get; set; }

        [JsonRequired]
        public DateTimeOffset CachedAtUtc { get; set; }

        [JsonRequired]
        public List<Journey> Items { get; set; } = new();
    }
}
