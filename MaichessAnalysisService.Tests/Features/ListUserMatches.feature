Feature: List User Matches
  Users can list their matches imported directly from match-db. By default only
  finished matches are returned; the status filter can additionally surface ongoing
  games so a player can open one for review while it is still in progress. Results
  are paginated.

  Scenario: Finished matches from both sides are returned newest first
    Given user "user-1" has the following finished matches as white:
      | id      | status    | time_format_id | last_move_at         |
      | match-1 | white_won | 5+0            | 2026-05-01T10:00:00Z |
    And user "user-1" has the following finished matches as black:
      | id      | status    | time_format_id | last_move_at         |
      | match-2 | black_won | 3+2            | 2026-05-02T11:00:00Z |
    When "user-1" lists their finished matches page 1 page_size 20
    Then the user matches result contains 2 matches
    And the user matches result total is 2
    And the user matches result first match id is "match-2"

  Scenario: Ongoing matches are excluded from the list
    Given user "user-1" has the following finished matches as white:
      | id      | status    | time_format_id | last_move_at         |
      | match-1 | ongoing   | 5+0            | 2026-05-01T10:00:00Z |
      | match-2 | white_won | 5+0            | 2026-05-02T10:00:00Z |
    And user "user-1" has no finished matches as black
    When "user-1" lists their finished matches page 1 page_size 20
    Then the user matches result contains 1 matches
    And the user matches result first match id is "match-2"

  Scenario: Status all surfaces both ongoing and finished matches
    Given user "user-1" has the following finished matches as white:
      | id      | status    | time_format_id | last_move_at         |
      | match-1 | ongoing   | 5+0            | 2026-05-01T10:00:00Z |
      | match-2 | white_won | 5+0            | 2026-05-02T10:00:00Z |
    And user "user-1" has no finished matches as black
    When "user-1" lists their matches with status "all" page 1 page_size 20
    Then the user matches result contains 2 matches

  Scenario: Status ongoing surfaces only in-progress matches
    Given user "user-1" has the following finished matches as white:
      | id      | status    | time_format_id | last_move_at         |
      | match-1 | ongoing   | 5+0            | 2026-05-01T10:00:00Z |
      | match-2 | white_won | 5+0            | 2026-05-02T10:00:00Z |
    And user "user-1" has no finished matches as black
    When "user-1" lists their matches with status "ongoing" page 1 page_size 20
    Then the user matches result contains 1 matches
    And the user matches result first match id is "match-1"

  Scenario: Pagination returns the requested slice
    Given user "user-1" has 5 finished matches as white starting "2026-05-01T10:00:00Z"
    And user "user-1" has no finished matches as black
    When "user-1" lists their finished matches page 2 page_size 2
    Then the user matches result contains 2 matches
    And the user matches result total is 5

  Scenario: page_size is clamped to 100
    Given user "user-1" has no finished matches as white
    And user "user-1" has no finished matches as black
    When "user-1" lists their finished matches page 1 page_size 500
    Then the user matches result page_size is 100

  Scenario: The time format embedded in a match is preserved
    Given user "user-1" has the following finished matches as white:
      | id      | status    | time_format_id | last_move_at         |
      | match-1 | white_won | 3+2            | 2026-05-01T10:00:00Z |
    And user "user-1" has no finished matches as black
    When "user-1" lists their finished matches page 1 page_size 20
    Then the user matches first time format id is "3+2"
    And the user matches first increment ms is 2000
