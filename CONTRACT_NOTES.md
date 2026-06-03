# Contract Notes — Analysis Service

## Whatif position_history limitation

Whatif move validation uses an empty initial `position_history`. Threefold repetition detection
covers only positions within the whatif branch, not positions from the preceding game. This is a
known acceptable limitation for analysis use.

## from-match authorization broadened to include the match creator

`POST /games/from-match/{match_id}` originally authorized only board participants
(`white.user_id` / `black.user_id`). Profile → Past Matches (served by Match Manager)
also lists matches a user only *started* — bot-vs-bot games attributed via `created_by`
— and renders an Analyse button on every row. Those imports were rejected with `403`,
and the client swallowed the error, so the button appeared dead.

**Decision (approved by product owner):** allow the match's creator
(`created_by_user_id`) to import in addition to participants. The contract
(`rest/analysis.md`) and `ImportFromMatchAsync` were updated together. The match
document already carries `created_by_user_id` (written by Match Manager), so no new
fields or cross-service calls were needed.

## Analysis cache per-depth document limit

Cache reads use `Database.List(limit=100)`. With practical engine depth ceilings around 40, this
is safe. If the engine ever exceeds 100 depths for a position, cache reads will silently miss the
deepest entries. Raise the limit if this becomes an issue.
