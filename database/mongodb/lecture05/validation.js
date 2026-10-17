const m = db.getSiblingDB("mobility");

const invalid = m.journey_search.findOne({ _id: "LAB05:A" });

invalid._id = "LAB05:INVALID-SEATS";


invalid.departures[2].availableSeats = NumberInt(-1);

m.journey_search.insertOne(invalid);