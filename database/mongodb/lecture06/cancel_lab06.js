
const database = db.getSiblingDB("mobility");

// Cancel every LAB05-T-OK departure in the fixture.

const result = database.journey_search.updateMany(
    {
        _id: /^LAB05:/,
        "departures.tripId": "LAB05-T-OK"
    },
    {
        $set: {
            "departures.$[trip].status": "Cancelled"
        }
    },
    {
        arrayFilters: [
            { "trip.tripId": "LAB05-T-OK" }
        ]
    }
);

printjson({
    matchedCount: result.matchedCount,
    modifiedCount: result.modifiedCount
});

// Verify that no scheduled copies remain.

const remaining = database.journey_search.countDocuments({
    _id: /^LAB05:/,
    departures: {
        $elemMatch: {
            tripId: "LAB05-T-OK",
            status: "Scheduled"
        }
    }
});

printjson({
    documentsWithScheduledCopy: remaining
});
