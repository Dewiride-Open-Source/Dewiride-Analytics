<?php
/**
 * A stand-in for WordPress's roles.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

/**
 * The part of `WP_Roles` the plugin reads.
 */
final class Fake_Roles {

	/**
	 * Each role's slug and its definition.
	 *
	 * @var array<string, array{name: string}>
	 */
	public $roles = array();

	/**
	 * Each role's slug and display name.
	 *
	 * @var array<string, string>
	 */
	private $names;

	/**
	 * Creates the roles.
	 *
	 * @param array<string, string> $names Each role's slug and display name.
	 */
	public function __construct( array $names ) {
		$this->names = $names;

		foreach ( $names as $role => $name ) {
			$this->roles[ $role ] = array( 'name' => $name );
		}
	}

	/**
	 * Each role's slug and display name.
	 *
	 * @return array<string, string>
	 */
	public function get_names(): array {
		return $this->names;
	}
}
