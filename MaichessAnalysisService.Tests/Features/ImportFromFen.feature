Feature: Import From FEN
  A user can create an analysis game from an arbitrary FEN position.

  Scenario: Valid FEN imports successfully with no moves
    When "user-1" imports FEN "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"
    Then the result game source is "fen"
    And the result game has no moves and no fens
    And the result game starting fen is "r1bqkb1r/pppp1ppp/2n2n2/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4"
    And the result game has id "game-1"

  Scenario: Standard starting position FEN imports successfully
    When "user-1" imports FEN "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1"
    Then the result game source is "fen"
    And the result game result is "*"

  Scenario: Empty FEN string throws InvalidPgnException with fen reason
    When "user-1" imports FEN ""
    Then an InvalidPgnException is thrown with reason containing "fen must not be empty"
