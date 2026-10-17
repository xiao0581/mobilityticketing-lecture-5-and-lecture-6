const m = db.getSiblingDB("mobility");

if (!m.getCollectionNames().includes("journey_search_by_trip")) {
  m.createCollection("journey_search_by_trip");
} else {
  m.journey_search_by_trip.deleteMany({
    $or: [
      { _id: /^LAB05:/ },
      { tripId: /^LAB05-/ },
    ],
  });
}

const journeys = m.journey_search.find({ _id: /^LAB05:/ }).toArray();

for (const journey of journeys) {
  for (const departure of journey.departures) {

    const doc = {
      _id:
        `${journey.cityId}:${journey.routeId}:` +
        `${journey.fromStopId}:${journey.toStopId}:` +
        `${departure.tripId}`,

      cityId: journey.cityId,
      routeId: journey.routeId,
      fromStopId: journey.fromStopId,
      toStopId: journey.toStopId,

      tripId: departure.tripId,
      departureUtc: departure.departureUtc,
      arrivalUtc: departure.arrivalUtc,
      status: departure.status,
      availableSeats: departure.availableSeats,

      price: journey.price,
      currency: journey.currency,
      schemaVersion: journey.schemaVersion,
    };

    m.journey_search_by_trip.insertOne(doc);
  }
}

printjson({
  count: m.journey_search_by_trip.countDocuments({
    tripId: /^LAB05-/,
  }),
});

function searchByTrip(cityId, fromStopId, toStopId, start, end) {
  return m.journey_search_by_trip
    .find(
      {
        cityId,
        fromStopId,
        toStopId,
        status: "Scheduled",
        departureUtc: {
          $gte: start,
          $lt: end,
        },
      },
      {
        _id: 0,
        cityId: 1,
        routeId: 1,
        fromStopId: 1,
        toStopId: 1,
        tripId: 1,
        departureUtc: 1,
        arrivalUtc: 1,
        availableSeats: 1,
        price: 1,
        currency: 1,
      }
    )
    .sort({
      departureUtc: 1,
    })
    .toArray();
}

print("Alternative search:");

printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 1: 06:00 - 07:00");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 2: end at 06:20");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T06:20:00Z")
  )
);

print("Test 3: start at 06:20");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-02T06:20:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 4: Airport to Nørreport");
printjson(
  searchByTrip(
    "CPH",
    "STOP-AIRPORT",
    "STOP-NORREPORT",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Test 5: 3 October");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    ISODate("2026-10-03T06:00:00Z"),
    ISODate("2026-10-03T07:00:00Z")
  )
);

print("Test 6: no matching destination");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-NO-MATCH",
    ISODate("2026-10-02T06:00:00Z"),
    ISODate("2026-10-02T07:00:00Z")
  )
);

print("Cancel LAB05-T-OK in alternative model:");

printjson(
  m.journey_search_by_trip.updateMany(
    {
      tripId: "LAB05-T-OK",
    },
    {
      $set: {
        status: "Cancelled",
      },
    }
  )
);

print("LAB05-T-OK after cancellation:");

printjson(
  m.journey_search_by_trip
    .find(
      {
        tripId: "LAB05-T-OK",
      },
      {
        _id: 1,
        tripId: 1,
        toStopId: 1,
        status: 1,
      }
    )
    .toArray()
);