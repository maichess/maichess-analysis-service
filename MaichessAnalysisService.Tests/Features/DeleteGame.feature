Feature: Delete Game
  A user can delete their own saved analysis game.

  Scenario: Owner deletes their game
    Given a saved game "game-1" owned by "user-1"
    When "user-1" deletes game "game-1"
    Then no exception is thrown

  Scenario: Game not found throws AnalysisGameNotFoundException
    Given game "missing" does not exist
    When "user-1" deletes game "missing"
    Then an AnalysisGameNotFoundException is thrown

  Scenario: Non-owner throws AccessDeniedException
    Given a saved game "game-1" owned by "user-1"
    When "user-2" deletes game "game-1"
    Then an AccessDeniedException is thrown
