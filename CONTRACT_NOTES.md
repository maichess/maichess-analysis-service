# Contract Notes — Analysis Service

## Whatif position_history limitation

Whatif move validation uses an empty initial `position_history`. Threefold repetition detection
covers only positions within the whatif branch, not positions from the preceding game. This is a
known acceptable limitation for analysis use.

## Analysis cache per-depth document limit

Cache reads use `Database.List(limit=100)`. With practical engine depth ceilings around 40, this
is safe. If the engine ever exceeds 100 depths for a position, cache reads will silently miss the
deepest entries. Raise the limit if this becomes an issue.
