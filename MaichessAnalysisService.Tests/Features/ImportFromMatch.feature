Feature: Import From Match
  A user can import a finished match into their analysis games.

  Scenario: Finished match imports successfully with correct FEN history
    Given match "match-1" is finished with white user "user-1" and black user "user-2" with 1 move "e2e4"
    And match position 1 for "match-1" is "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"
    When "user-1" imports match "match-1"
    Then the result game source is "match"
    And the result game match id is "match-1"
    And the result game has 1 moves

  Scenario: Ongoing match import throws MatchStillOngoingException
    Given match "match-2" is ongoing with white user "user-1" and black user "user-2"
    When "user-1" imports match "match-2"
    Then a MatchStillOngoingException is thrown

  Scenario: Import by non-participant throws MatchAccessDeniedException
    Given match "match-3" is finished with white user "user-1" and black user "user-2" with 0 moves
    When "outsider" imports match "match-3"
    Then a MatchAccessDeniedException is thrown

  Scenario: Match not found propagates RpcException
    Given match "missing" does not exist
    When "user-1" imports match "missing"
    Then an RpcException with NotFound is thrown

  Scenario: BlackWon match with bot white imports with correct result
    Given match "match-5" has bot white "bot-1" and user black "user-1" finished with black winning
    When "user-1" imports match "match-5"
    Then the result game result is "0-1"

  Scenario: Draw match with no-identity black imports with correct result
    Given match "match-6" has user white "user-1" and no-identity black finished as draw
    When "user-1" imports match "match-6"
    Then the result game result is "1/2-1/2"

  Scenario: Match with bot black player imports successfully
    Given match "match-7" has user white "user-1" and bot black "bot-1" finished with white winning
    When "user-1" imports match "match-7"
    Then the result game source is "match"

  Scenario: Unspecified status match with no-identity white imports with asterisk result
    Given match "match-8" has no-identity white and user black "user-1" with unspecified status
    When "user-1" imports match "match-8"
    Then the result game result is "*"
