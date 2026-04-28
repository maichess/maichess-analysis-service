Feature: Get Game
  A user can retrieve a saved analysis game by ID.

  Scenario: Owner retrieves their game
    Given a saved game "game-1" owned by "user-1"
    When "user-1" retrieves game "game-1"
    Then the result game has id "game-1"

  Scenario: Game not found throws AnalysisGameNotFoundException
    Given game "missing" does not exist
    When "user-1" retrieves game "missing"
    Then an AnalysisGameNotFoundException is thrown

  Scenario: Non-owner throws AccessDeniedException
    Given a saved game "game-1" owned by "user-1"
    When "user-2" retrieves game "game-1"
    Then an AccessDeniedException is thrown
