Feature: Import From Match
  A user can import a finished match into their analysis games.

  Scenario: Finished match with moves imports successfully
    Given match "match-1" exists with status "white_won" and white user "user-1" and black user "user-2"
    And match "match-1" has moves "e2e4" and san "e4" from the initial fen
    When "user-1" imports match "match-1"
    Then the result game source is "match"
    And the result game match id is "match-1"
    And the result game has 1 moves

  Scenario: Ongoing match import throws MatchStillOngoingException
    Given match "match-2" exists with status "ongoing" and white user "user-1" and black user "user-2"
    When "user-1" imports match "match-2"
    Then a MatchStillOngoingException is thrown

  Scenario: Import by non-participant throws MatchAccessDeniedException
    Given match "match-3" exists with status "white_won" and white user "user-1" and black user "user-2"
    When "outsider" imports match "match-3"
    Then a MatchAccessDeniedException is thrown

  Scenario: Match not found throws AnalysisGameNotFoundException
    Given match "missing" does not exist
    When "user-1" imports match "missing"
    Then an AnalysisGameNotFoundException is thrown

  Scenario: BlackWon match with bot white imports with correct result
    Given match "match-5" exists with status "black_won" and white bot "bot-1" and black user "user-1"
    When "user-1" imports match "match-5"
    Then the result game result is "0-1"

  Scenario: Draw match with no black imports with correct result
    Given match "match-6" exists with status "draw" and white user "user-1" and no black
    When "user-1" imports match "match-6"
    Then the result game result is "1/2-1/2"

  Scenario: Match with bot black player imports successfully
    Given match "match-7" exists with status "white_won" and white user "user-1" and black bot "bot-1"
    When "user-1" imports match "match-7"
    Then the result game source is "match"

  Scenario: Unspecified status imports with asterisk result
    Given match "match-8" exists with status "unspecified" and no white and black user "user-1"
    When "user-1" imports match "match-8"
    Then the result game result is "*"
