<?php
/**
 * An option as the database holds it at this moment.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reads an option as the database holds it now, rather than as it was loaded when the request
 * began, and changes it only while it still holds what was read.
 *
 * WordPress loads the options every page needs once, as a request begins, so a request deciding
 * something from them cannot tell whether another request has changed them since. Where exactly
 * one of several requests running at once may act, the change is made in the database itself: of
 * the requests that read the same value, only the first to replace it finds it still there.
 */
final class Stored_Option {

	/**
	 * What an option holds in the database now, serialised as stored, or nothing when it is not
	 * stored.
	 *
	 * @param string $option The option's name.
	 */
	public static function now( string $option ): ?string {
		$wpdb = self::database();

		// phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching -- What the option holds now, which the copy loaded as the request began may no longer be.
		$stored = $wpdb->get_var( $wpdb->prepare( 'SELECT option_value FROM %i WHERE option_name = %s', $wpdb->options, $option ) );

		return is_string( $stored ) ? $stored : null;
	}

	/**
	 * Replaces what an option holds, if it still holds what was read, and says whether it did.
	 *
	 * WordPress's own copy of the option is left as it was loaded; the next write through
	 * update_option() brings it up to date.
	 *
	 * @param string $option The option's name.
	 * @param string $was    What it held when it was read, serialised as stored.
	 * @param mixed  $value  What it is to hold.
	 */
	public static function replace( string $option, string $was, $value ): bool {
		$wpdb = self::database();

		// phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching -- Changed only while it still holds what was read, which is what lets one request of several act.
		$changed = $wpdb->update(
			$wpdb->options,
			array( 'option_value' => maybe_serialize( $value ) ),
			array(
				'option_name'  => $option,
				'option_value' => $was,
			)
		);

		return 1 === $changed;
	}

	/**
	 * WordPress's connection to its database.
	 *
	 * @return \wpdb
	 */
	private static function database() {
		global $wpdb;

		/**
		 * WordPress connects before any plugin loads, and keeps the connection here.
		 *
		 * @var \wpdb $wpdb
		 */
		return $wpdb;
	}
}
