Feature: List Games
  A user can list their saved analysis games with pagination.

  Scenario: First page returns correct slice
    Given "user-1" has 3 saved games
    When "user-1" lists games page 1 page_size 2
    Then the list result contains 2 games
    And the list result total is 3
    And the list result page is 1
    And the list result page_size is 2

  Scenario: Page beyond total returns empty games list
    Given "user-1" has 0 saved games
    When "user-1" lists games page 2 page_size 10
    Then the list result contains 0 games
    And the list result total is 0

  Scenario: page_size is clamped to 100
    Given "user-1" has 5 saved games
    When "user-1" lists games page 1 page_size 500
    Then the list result page_size is 100

  Scenario: Page 0 is clamped to page 1
    Given "user-1" has 3 saved games
    When "user-1" lists games page 0 page_size 2
    Then the list result page is 1
    And the list result contains 2 games
