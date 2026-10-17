const m = db.getSiblingDB("mobility");

function search(cityId, fromStopId, toStopId, start, end) {

  if (!fromStopId || !toStopId) {
    throw new Error("Stop ID cannot be empty");
  }

  if (end <= start) {
    throw new Error("End time must be after start time");
  }

  return m.journey_search
    .aggregate([
      {
        $match: { cityId, fromStopId, toStopId },
      },
      {
        $unwind: "$departures",
      },
      {
        $match: {
          "departures.status": "Scheduled",
          "departures.departureUtc": {
            $gte: start,
            $lt: end,
          },
        },
      },
      {
        $project: {
          _id: 0,
          cityId: 1,
          routeId: 1,
          fromStopId: 1,
          toStopId: 1,
          tripId: "$departures.tripId",
          departureUtc: "$departures.departureUtc",
          arrivalUtc: "$departures.arrivalUtc",
          availableSeats: "$departures.availableSeats",
          price: 1,
          currency: 1,
        },
      },
      {
        $sort: {
          departureUtc: 1,
        },
      },
    ])
    .toArray();
}

print("Test 1: 06:00 - 07:00");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 2: end at 06:20");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T06:20:00Z")
  )
);

print("Test 3: start at 06:20");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:20:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 4: Airport to Nørreport");
printjson(
  search(
    "CPH",
    "STOP-AIRPORT",
    "STOP-NORREPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 5: Same search on 3 October");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-03T06:00:00Z"),
    ISODate("2026-10-03T07:00:00Z")
  )
);

print("Test 6: No matching destination");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-NO-MATCH",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Invalid test: empty stop");

printjson(
  search(
    "CPH",
    "",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )

);

print("Search 1: Nørreport -> Airport");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Search 2: Nørreport -> Central");
printjson(
  search(
    "CPH",
    "STOP-NORREPORT",
    "STOP-CENTRAL",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);