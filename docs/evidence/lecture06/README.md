# Lecture 6 — Redis: Caching Journey Search

## 1. Objective

The purpose of this lab is to implement Redis caching for the MobilityTicketing journey search, evaluate cache key design, test expiration and failure handling, and explore atomic seat counters.

The implementation uses:

- **MongoDB** for journey search data.
- **Redis** for temporary cached search results.
- **C# / .NET** for the Cache-Aside implementation.
- **Docker Compose** for running the database services.

The work continues from Lecture 5 in the existing MobilityTicketing repository.

## 2. Redis Setup and Basic Commands

Redis was added to the existing Docker Compose configuration using `redis:7-alpine`.

Persistence was disabled because Redis is used as a disposable cache in this exercise.

The Redis connection was verified using `PING`, which returned `PONG`.

The following commands were tested:

```redis
SET stop:LAB06:STOP-NORREPORT "Norreport" EX 20
GET stop:LAB06:STOP-NORREPORT
TTL stop:LAB06:STOP-NORREPORT
```

The `SET` operation stored the value with a 20-second expiration. `GET` returned the stop name, while `TTL` showed the remaining lifetime.

After expiration, `GET` returned `nil` and `TTL` returned `-2`.

**Observation:** Redis automatically expires cached values based on TTL.

## 3. Cache Key Design

### Incomplete Cache Key

The initial key did not contain the search date and time:

```text
search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT
```

The morning search for 2 October 2026, 06:00–07:00 UTC returned:

```text
LAB05-T-OK
```

The evening search for 18:00–19:00 UTC returned:

```json
[]
```

Both searches would use the same incomplete cache key.

This could cause the evening search to incorrectly receive the morning result.

### Complete Cache Key

The improved key includes the city, origin, destination, start time, end time, and format version.

Example:

```text
search:LAB06:v2:CPH:STOP-NORREPORT:STOP-AIRPORT:2026-10-02T06%3A00%3A00.0000000Z:2026-10-02T07%3A00%3A00.0000000Z
```

The C# implementation uses `DateTimeOffset`, UTC normalization, and `Uri.EscapeDataString()`.

### Cache Key Tests

| Test | Expected | Result |
|---|---|---|
| Morning vs evening | Different keys | Passed |
| Reversed origin/destination | Different keys | Passed |
| Same instant with different UTC offsets | Same key | Passed |
| IDs containing separators | Different keys | Passed |

The implementation also rejects empty IDs and invalid time ranges.

**Decision:** All search parameters that influence the result must be included in the cache key.

## 4. Cache-Aside Implementation

The C# implementation uses the Cache-Aside pattern.

The search procedure is:

1. Validate the search request.
2. Generate the complete cache key.
3. Attempt to read the result from Redis.
4. On a valid cache hit, return the cached result.
5. On a cache miss, execute the MongoDB search.
6. Serialize the result as JSON and save it in Redis.
7. Return the search result.

The JSON cache structure is:

```json
{
  "schemaVersion": 2,
  "cachedAtUtc": "2026-10-17T08:00:00Z",
  "items": []
}
```

The timestamp above is an example. The implementation generates the actual UTC timestamp when writing the cache.

The lab uses a configurable TTL of 20 seconds.

### Test A — Repeated Search

| Request | Source | MongoDB calls |
|---|---|---|
| First morning search | MongoDB / MISS | 1 |
| Second morning search | Redis / HIT | 1 |

The second search returned the same journey without another MongoDB query.

### Test B — Empty Result

The evening search returned an empty array.

The first request fetched the result from MongoDB and stored it in Redis.

The second request returned the cached empty array.

**Observation:** An empty result is valid data and should be cached. An empty result is different from a missing cache entry.

### Test C — Cache Expiration

The morning cache was created with a 20-second TTL.

Its remaining expiration was changed to one second, and the test waited until the key expired.

After expiration, the next search queried MongoDB again and stored a new cache entry.

**Observation:** TTL limits the lifetime of a cache entry, but it does not guarantee that data remains correct before expiration.

### Test D — Invalid Cached Values

Three invalid cache values were tested:

| Case | Invalid value | Expected handling |
|---|---|---|
| D1 | `not-json` | Reject invalid JSON |
| D2 | Wrong `schemaVersion` | Reject incompatible format |
| D3 | Missing `items` | Reject incomplete entry |

The application treated invalid cache values as misses, queried MongoDB, and replaced the invalid values.

**Decision:** Cached JSON must be validated before it is trusted.

## 5. Cache Invalidation

A scheduled journey was first cached in Redis using a temporary five-minute TTL.

The departure `LAB05-T-OK` was then cancelled in MongoDB.

### Observations

| Search | Result |
|---|---|
| Uncached search after cancellation | `[]` |
| Cached search before invalidation | `LAB05-T-OK` |
| Cached search after deleting the key | `[]` |

This demonstrated that Redis could still contain outdated data after MongoDB had been updated.

Deleting the exact cache key forced the next search to retrieve the updated result from MongoDB.

### Discussion

**What did the passenger see before and after deletion?**

Before deletion, the passenger could see a cancelled departure because Redis still contained the old result. After deletion, the next search returned an empty result.

**What if MongoDB had not received the cancellation?**

The search could retrieve the still-scheduled departure from MongoDB and cache it again. Cache invalidation alone cannot correct an outdated source database.

**Can an old result be written back after invalidation?**

Yes. An in-progress search could read the original data before cancellation, and write that result into Redis after the cache key has already been deleted.

This is a race condition. Possible mitigations include version checks or coordinating invalidation with concurrent cache writes.

## 6. Redis Failure Handling

The application was tested under the following conditions.

| Situation | Expected behaviour |
|---|---|
| Redis unavailable, MongoDB available | Search succeeds through MongoDB |
| Redis and MongoDB unavailable | Search reports a visible error |
| Both services restored | Search works normally |

A Redis failure should not prevent the application from using MongoDB.

However, if MongoDB is also unavailable and no usable cached data exists, the application must report an error rather than returning an empty array.

Returning `[]` would incorrectly suggest that the database query succeeded but found no journeys.

### Measured Response Times

| Situation | Elapsed time |
|---|---|
| Redis unavailable | TODO: actual ms |
| Redis and MongoDB unavailable | TODO: actual ms |
| Services restored | TODO: actual ms |

**Observation:** Graceful degradation allows the application to continue operating when Redis is unavailable, although response times may increase.

## 7. Concurrent Searches

Ten concurrent requests were sent for the same journey search.

A temporary artificial delay of 200 milliseconds was added before the MongoDB query.

### Results

| Batch | Cache status | MongoDB calls | Elapsed time |
|---|---|---|---|
| First 10 requests | Cold cache | TODO | TODO ms |
| Second 10 requests | Warm cache | TODO | TODO ms |

During the first batch, several requests can observe a cache miss before any request finishes querying MongoDB and writes the cache entry.

As a result, multiple identical MongoDB queries may run concurrently.

This behaviour is known as a **cache stampede**.

During the second batch, requests should find the already populated Redis cache, avoiding additional MongoDB queries.

Possible improvements include single-flight request coordination, local semaphores, or distributed locking.

These improvements were optional and were not required for the lab.

The artificial MongoDB delay was removed after the test.

## 8. Atomic Seat Counter

Redis was used to simulate remaining seats for two trips.

Example keys:

```text
availability:LAB06:TRIP-ONE
availability:LAB06:TRIP-TWO
```

The initial values were zero and ten seats.

These values cannot be represented accurately by one cache key containing only the route and date because two trips on the same route can have different availability.

### Missing Value vs Zero

A stored value of `0` means that no seats remain.

A missing key means the availability is unknown.

These situations must be treated differently.

### Atomic Decrement

A Lua script was implemented in:

```text
database/redis/lecture06/decrement_if_positive.lua
```

The script:

1. Reads the current availability.
2. Rejects missing or invalid values.
3. Rejects a decrement when the count is zero.
4. Decrements the value only when it is positive.

### Expected Results

| Operation | Result |
|---|---|
| Decrement from 1 seat | 0 |
| Attempt another decrement | -1 (rejected) |
| Read stored value | 0 |
| Execute script after deleting key | `availability unknown` |

The Redis Lua script executes the check and decrement atomically, preventing another Redis operation from changing the counter between those steps.

However, this does not create a ticket, reserve a seat in PostgreSQL, or process a payment.

**Decision:** Redis can support fast availability checks, but final ticket reservations and payments must be handled by the authoritative transactional system.

## 9. Redis Cache Strategy

### Proposed Cache Design

| Data | Example key | Example value | Source | Proposed TTL |
|---|---|---|---|---|
| Journey Search | `search:LAB06:v2:...` | Journey JSON | MongoDB | 30 seconds |
| Trip Availability | `availability:LAB06:TRIP-ONE` | `10` | PostgreSQL reservation state | 5 seconds |
| Route Information | `route:LAB06:LINE-M2` | Route JSON | Route data in MongoDB | 24 hours |
| Stop Information | `stop:LAB06:STOP-NORREPORT` | Stop JSON | Authoritative stop data; verify collection/table | 24 hours |

### Cache Ownership and Invalidation

**Journey Search**

The journey search application creates the cache entry after querying MongoDB.

Cached results should be invalidated when relevant departures are cancelled, rescheduled, or otherwise changed. If Redis has no usable result, the search must query MongoDB.

I would initially test a TTL of 30 seconds outside the lab.

**Trip Availability**

The reservation service should control availability changes and related invalidation.

Because seat availability changes frequently, a short TTL is preferable. Even a short-lived cached count may become outdated during simultaneous purchases.

The authoritative reservation state should be used when Redis has no reliable value and when committing a reservation.

**Route Information**

The service responsible for route information should invalidate the corresponding cache when routes are updated.

If the cache is missing, the route should be fetched from its authoritative database.

A 24-hour TTL is reasonable as an initial choice because route metadata changes relatively infrequently.

**Stop Information**

The service maintaining stop information should update or invalidate the corresponding cache when a stop changes.

If Redis has no usable value, the stop information should be retrieved from the authoritative stop data source.

A 24-hour TTL is reasonable for relatively stable stop metadata.

### Will Two Passengers Searching a Minute Apart Share a Cache Entry?

They can share a cache entry if their search parameters generate the same key and the entry remains valid.

However, with a 30-second TTL, the first passenger's cached result would normally have expired before a second passenger searches one minute later.

The second search would then query MongoDB and create a new cache entry.

### What Could Make Search Results Outdated?

Search results can become stale because of:

- Trip cancellations.
- Schedule changes.
- Seat availability updates.
- Delayed updates in the underlying database.
- Concurrent requests writing outdated cache entries after invalidation.

TTL alone does not eliminate these risks.

## 10. Conclusion

This lab demonstrated how Redis can reduce repeated MongoDB queries using the Cache-Aside pattern.

The most important lessons were:

- Cache keys must contain all relevant search parameters.
- Cache hits can reduce database load.
- Empty results are valid cache entries.
- TTL controls expiration but does not guarantee consistency.
- Invalid cache data should be rejected and refreshed.
- Redis failures should not prevent access to the primary database.
- Concurrent cache misses can cause duplicate database queries.
- Lua scripts can provide atomic counter operations.
- Redis is not a replacement for authoritative reservation and payment transactions.

The main architectural takeaway is that **Redis improves performance, while the primary databases remain responsible for authoritative data and transactional correctness**.
