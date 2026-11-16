<?php
/**
 * A stand-in for WordPress's database.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

/**
 * The part of `wpdb` the plugin uses, over the options a test keeps.
 *
 * A query is prepared to the option it names, the only value the plugin's queries hold. A row is
 * changed only while it still holds what the condition says, and only a row whose value changes
 * is counted as changed, as MySQL counts it.
 */
final class Fake_Database {

	/**
	 * The options table's name.
	 *
	 * @var string
	 */
	public $options = 'wp_options';

	/**
	 * What runs just before a row is changed, standing in for another request writing first.
	 *
	 * @var (callable(): void)|null
	 */
	public $before_update;

	/**
	 * Reads an option as the test keeps it: its value, or false when it is not stored.
	 *
	 * @var callable(string): mixed
	 */
	private $read;

	/**
	 * Keeps an option's new value where the test keeps it.
	 *
	 * @var callable(string, mixed): void
	 */
	private $write;

	/**
	 * Creates the database over the options a test keeps.
	 *
	 * @param callable(string): mixed       $read  Reads an option: its value, or false.
	 * @param callable(string, mixed): void $write Keeps an option's new value.
	 */
	public function __construct( callable $read, callable $write ) {
		$this->read  = $read;
		$this->write = $write;
	}

	/**
	 * A query that looks an option up by its name in the options table, prepared: here, the name.
	 *
	 * @param string $query     The query.
	 * @param mixed  ...$values The values: the table, then the option's name.
	 */
	public function prepare( string $query, ...$values ): string {
		$table = $values[0] ?? null;
		$name  = $values[1] ?? null;

		return false !== strpos( $query, 'FROM %i WHERE option_name = %s' ) && $table === $this->options && is_string( $name ) ? $name : '';
	}

	/**
	 * What a prepared query reads: the option's value, serialised as WordPress stores it, or
	 * nothing when it is not stored.
	 *
	 * @param string $option The prepared query: the option's name.
	 */
	public function get_var( string $option ): ?string {
		$value      = ( $this->read )( $option );
		$serialised = false === $value ? null : maybe_serialize( $value );

		return is_string( $serialised ) ? $serialised : null;
	}

	/**
	 * Changes an option's row where it still holds what the condition says, and gives the number
	 * of rows changed.
	 *
	 * @param string               $table The table.
	 * @param array<string, mixed> $data  The new values.
	 * @param array<string, mixed> $where The condition.
	 */
	public function update( string $table, array $data, array $where ): int {
		if ( null !== $this->before_update ) {
			( $this->before_update )();
		}

		$option = $where['option_name'] ?? null;
		$value  = $data['option_value'] ?? null;
		$stored = is_string( $option ) && $this->options === $table ? $this->get_var( $option ) : null;

		if ( null === $stored || ! is_string( $value ) || $value === $stored || ( $where['option_value'] ?? $stored ) !== $stored ) {
			return 0;
		}

		( $this->write )( $option, maybe_unserialize( $value ) );

		return 1;
	}
}
