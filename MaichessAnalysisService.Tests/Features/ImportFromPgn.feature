Feature: Import From PGN
  A user can import a chess game from a PGN string.

  Scenario: Valid PGN with moves imports successfully
    Given the move validator resolves "e2e4" at the initial FEN to "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"
    And the move validator resolves "e7e5" at "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1" to "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2"
    When "user-1" imports the following PGN:
      """
      [White "Alice"]
      [Black "Bob"]
      [Result "*"]

      1. e4 e5 *
      """
    Then the result game source is "pgn"
    And the result game has 2 moves
    And the result game white name is "Alice"
    And the result game black name is "Bob"

  Scenario: PGN with empty movetext imports with zero moves
    When "user-1" imports the following PGN:
      """
      [White "Alice"]
      [Black "Bob"]
      [Result "*"]

      *
      """
    Then the result game source is "pgn"
    And the result game has 0 moves

  Scenario: PGN with missing White and Black tags defaults to "?"
    When "user-1" imports the following PGN:
      """
      [Event "Test"]
      [Result "*"]

      *
      """
    Then the result game white name is "?"
    And the result game black name is "?"

  Scenario: Malformed PGN with no tags and no moves throws InvalidPgnException
    When "user-1" imports PGN ""
    Then an InvalidPgnException is thrown

  Scenario: PGN with an illegal move throws InvalidPgnException with move in reason
    Given the move validator returns no legal moves at the initial FEN
    When "user-1" imports the following PGN:
      """
      [Event "Test"]
      [Result "*"]

      1. Zz99 *
      """
    Then an InvalidPgnException is thrown with reason containing "Zz99"
