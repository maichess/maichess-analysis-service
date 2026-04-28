# Contract Notes — Analysis Service

## Database.Count RPC missing

The Database Service proto (`database.proto`) has no `Count` RPC. `GET /games` requires a total
count for pagination metadata.

Workaround: `CountByUserIdAsync` calls `Database.List` with `limit=10000` and returns
`records.Count`. This is O(N) and degrades for users with many saved games.

Proposed change: add to `database.proto`:
```proto
rpc Count(CountRequest) returns (CountResponse);
message CountRequest { string collection = 1; google.protobuf.Struct filter = 2; }
message CountResponse { int64 count = 1; }
```
Do not implement until explicitly told to proceed.

## Match.fen_history missing from GetMatch response

`Matches.GetMatch` returns a `Match` message that contains `current_fen` and `moves` but no
full FEN history. `ImportFromMatchAsync` must call `Matches.GetMatchPosition` for every move
index to reconstruct the history — N gRPC round-trips for an N-move game.

Proposed change: add `repeated string fen_history = 10;` to the `Match` message in
`matches.proto` and populate it in `GetMatch`.

Do not implement until explicitly told to proceed.

## Stored PGN uses UCI notation instead of SAN

`BuildMinimalPgn` (used during match import) writes moves in UCI notation (e.g. "e2e4")
rather than standard SAN notation (e.g. "e4"). Converting UCI→SAN requires reverse-lookup
against legal moves and piece disambiguation, which is non-trivial without a chess library.

This is a known simplification. PGN files produced by this service are functional for import
back into the analysis service but may not be compatible with external PGN readers that
expect SAN notation.
