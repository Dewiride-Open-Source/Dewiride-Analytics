<?php
/**
 * Tests for reading and changing an option as the database holds it.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Dewiride\Analytics\WordPress\Stored_Option;

/**
 * An option is read as stored now, and replaced only while it still holds what was read.
 */
final class Stored_Option_Test extends Test_Case {

	/**
	 * The option the tests keep.
	 */
	private const OPTION = 'dewiride_analytics_example';

	/**
	 * The options the database holds, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $stored = array();

	/**
	 * The database.
	 *
	 * @var Fake_Database
	 */
	private $database;

	/**
	 * Starts each test with a database holding one option.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->stored   = array( self::OPTION => array( 'turn' => 0 ) );
		$this->database = new Fake_Database(
			function ( string $option ) {
				return $this->stored[ $option ] ?? false;
			},
			function ( string $option, $value ): void {
				$this->stored[ $option ] = $value;
			}
		);
		self::given_database( $this->database );
	}

	/**
	 * An option is read as the database holds it, serialised; one not stored reads as nothing.
	 */
	public function test_an_option_is_read_as_stored_now(): void {
		self::assertSame( maybe_serialize( array( 'turn' => 0 ) ), Stored_Option::now( self::OPTION ) );
		self::assertNull( Stored_Option::now( 'dewiride_analytics_missing' ) );
	}

	/**
	 * An option still holding what was read is replaced.
	 */
	public function test_an_option_as_read_is_replaced(): void {
		$read = (string) Stored_Option::now( self::OPTION );

		self::assertTrue( Stored_Option::replace( self::OPTION, $read, array( 'turn' => 1 ) ) );
		self::assertSame( array( 'turn' => 1 ), $this->stored[ self::OPTION ] );
	}

	/**
	 * An option another request changed after it was read is left as that request left it.
	 */
	public function test_an_option_changed_since_it_was_read_is_left_alone(): void {
		$read                          = (string) Stored_Option::now( self::OPTION );
		$this->database->before_update = function (): void {
			$this->stored[ self::OPTION ] = array( 'turn' => 2 );
		};

		self::assertFalse( Stored_Option::replace( self::OPTION, $read, array( 'turn' => 1 ) ) );
		self::assertSame( array( 'turn' => 2 ), $this->stored[ self::OPTION ] );
	}

	/**
	 * Of two requests that read the same value, only the first to replace it does.
	 */
	public function test_only_the_first_of_two_replaces(): void {
		$first  = (string) Stored_Option::now( self::OPTION );
		$second = (string) Stored_Option::now( self::OPTION );

		self::assertTrue( Stored_Option::replace( self::OPTION, $first, array( 'turn' => 1 ) ) );
		self::assertFalse( Stored_Option::replace( self::OPTION, $second, array( 'turn' => 2 ) ) );
		self::assertSame( array( 'turn' => 1 ), $this->stored[ self::OPTION ] );
	}
}
