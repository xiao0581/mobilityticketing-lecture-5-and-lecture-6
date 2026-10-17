const m = db.getSiblingDB("mobility");

const original = m.journey_search.findOne({
  _id: "LAB05:A",
});

const departures1000 = Array.from({ length: 1000 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i}`,
}));

m.journey_search.updateOne(
  {
    _id: "LAB05:GROWTH",
  },
  {
    $set: {
      departures: departures1000,
    },
  }
);

printjson(
  m.journey_search
    .aggregate([
      {
        $match: {
          _id: "LAB05:GROWTH",
        },
      },
      {
        $project: {
          _id: 0,
          bytes: {
            $bsonSize: "$$ROOT",
          },
          departures: {
            $size: "$departures",
          },
        },
      },
    ])
    .toArray()
);