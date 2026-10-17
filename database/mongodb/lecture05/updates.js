const m = db.getSiblingDB("mobility");

printjson(
  m.journey_search.updateMany(
    {
      cityId: "CPH",
      routeId: "LINE-M2",
    },
    {
      $set: {
        price: NumberDecimal("40.00"),
      },
    }
  )
);