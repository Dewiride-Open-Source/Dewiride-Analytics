<?php
/**
 * A stand-in for WordPress's errors.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

/**
 * The part of `WP_Error` the plugin reads: what went wrong, in the words of whatever failed.
 */
final class Fake_Wp_Error {

	/**
	 * What went wrong.
	 *
	 * @var string
	 */
	private $message;

	/**
	 * Creates the error.
	 *
	 * @param string $message What went wrong.
	 */
	public function __construct( string $message ) {
		$this->message = $message;
	}

	/**
	 * What went wrong.
	 */
	public function get_error_message(): string {
		return $this->message;
	}
}
