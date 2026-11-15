<?php
/**
 * The plugin's settings: where the analytics installation is, which website this is, and which
 * signed-in roles are counted.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reads, registers and validates the one option the plugin keeps.
 */
final class Settings {

	/**
	 * The option holding the settings.
	 */
	public const OPTION = 'dewiride_analytics_settings';

	/**
	 * The settings group the settings form saves.
	 */
	public const GROUP = 'dewiride_analytics';

	/**
	 * An address the plugin can print: http or https, a host name or an IPv4 address, an optional
	 * port, and an optional path made of letters, digits, `-._~` and percent escapes.
	 *
	 * Narrower than what a URL may hold, on purpose. Optimisation plugins are handed this address
	 * as exclusion patterns, and several of them use the same text as a regular expression and as a
	 * plain substring, so it may only contain characters that read the same either way.
	 */
	private const ADDRESS_PATTERN = '#^(https?)://((?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)(?::([0-9]{1,5}))?((?:/(?!\.\.?(?:/|$))(?:[a-z0-9._~-]|%[0-9a-f]{2})+)*)/?$#i';

	/**
	 * The highest port number there is.
	 */
	private const HIGHEST_PORT = 65535;

	/**
	 * Registers the setting with the Settings API.
	 */
	public function register(): void {
		add_action( 'admin_init', array( $this, 'register_setting' ) );
	}

	/**
	 * Declares the option, how it is cleaned and what it holds before anything is saved.
	 */
	public function register_setting(): void {
		register_setting(
			self::GROUP,
			self::OPTION,
			array(
				'type'              => 'array',
				'show_in_rest'      => false,
				'sanitize_callback' => array( $this, 'sanitize' ),
				'default'           => self::defaults(),
			)
		);
	}

	/**
	 * Creates the option, loaded with every page, when the plugin is activated.
	 *
	 * Every page a visitor reads needs it, so it is created explicitly autoloaded rather than left
	 * to whatever the first save would choose. An option that already exists is left as it is.
	 */
	public static function create(): void {
		add_option( self::OPTION, self::defaults(), '', true );
	}

	/**
	 * What the option holds before anything is saved.
	 *
	 * @return array{address: string, site_id: string, counted_roles: list<string>}
	 */
	public static function defaults(): array {
		return array(
			'address'       => '',
			'site_id'       => '',
			'counted_roles' => array(),
		);
	}

	/**
	 * The saved settings, each read back as the type it is meant to be.
	 *
	 * @return array{address: string, site_id: string, counted_roles: list<string>}
	 */
	public function all(): array {
		$stored = get_option( self::OPTION, self::defaults() );
		$stored = is_array( $stored ) ? $stored : array();
		$roles  = isset( $stored['counted_roles'] ) && is_array( $stored['counted_roles'] ) ? $stored['counted_roles'] : array();

		return array(
			'address'       => isset( $stored['address'] ) && is_string( $stored['address'] ) ? $stored['address'] : '',
			'site_id'       => isset( $stored['site_id'] ) && is_string( $stored['site_id'] ) ? $stored['site_id'] : '',
			'counted_roles' => array_values( array_filter( $roles, 'is_string' ) ),
		);
	}

	/**
	 * The address of the analytics installation, without a trailing slash.
	 */
	public function address(): string {
		return $this->all()['address'];
	}

	/**
	 * The Website ID visits are counted under.
	 */
	public function site_id(): string {
		return $this->all()['site_id'];
	}

	/**
	 * The roles whose signed-in members are counted.
	 *
	 * @return list<string>
	 */
	public function counted_roles(): array {
		return $this->all()['counted_roles'];
	}

	/**
	 * Whether both the address and the Website ID are saved, which is when counting is on.
	 */
	public function is_complete(): bool {
		$settings = $this->all();

		return '' !== $settings['address'] && '' !== $settings['site_id'];
	}

	/**
	 * Whether whoever is saving the settings may change the address.
	 *
	 * The address names the script every counted page loads, so choosing it is adding code to the
	 * site, which WordPress allows only to people who hold `unfiltered_html`: on a network, only
	 * its administrators, and nobody at all where `DISALLOW_UNFILTERED_HTML` is set. A site's own
	 * administrator on a network manages its options without being trusted with code, and where
	 * the network's sites share one domain, a script they chose would run where the network
	 * administrator is signed in.
	 *
	 * WP-CLI may always change it. Whoever runs it operates the server and can already run any
	 * code there, `wp eval` included, so refusing would protect nothing and would only stop a site
	 * being set up from a script.
	 */
	public static function may_change_address(): bool {
		return ( defined( 'WP_CLI' ) && WP_CLI ) || current_user_can( 'unfiltered_html' );
	}

	/**
	 * Who can change the address, and what to do about it, for somebody who cannot.
	 */
	public static function address_lock_message(): string {
		return is_multisite() && ! is_super_admin()
			? __( 'Only a network administrator can change the address. Ask one to change it for you.', 'dewiride-analytics' )
			: __( 'Only someone allowed to add code to this site can change the address. Ask whoever looks after your site to change it for you.', 'dewiride-analytics' );
	}

	/**
	 * Whether browsers would refuse to load the beacon from an address: one starting with http,
	 * on a site served over https, where a page may not load a script over an insecure connection.
	 *
	 * @param string $address The address.
	 */
	public static function is_mixed_content( string $address ): bool {
		return 'http' === strtolower( (string) wp_parse_url( $address, PHP_URL_SCHEME ) )
			&& 'https' === wp_parse_url( home_url(), PHP_URL_SCHEME );
	}

	/**
	 * Where the beacon is loaded from.
	 */
	public function script_url(): string {
		return $this->address() . '/dw.js';
	}

	/**
	 * Where the beacon sends what it measures.
	 */
	public function collector_url(): string {
		return $this->address() . '/collect';
	}

	/**
	 * Cleans what the settings form sent.
	 *
	 * Running it again over its own result changes nothing and reports nothing, because the
	 * Settings API does exactly that the first time the option is saved. A value that cannot be
	 * used is refused with a message and the saved one is kept, so a mistyped field never undoes a
	 * setup that was working.
	 *
	 * @param mixed $input What the settings form sent.
	 * @return array{address: string, site_id: string, counted_roles: list<string>}
	 */
	public function sanitize( $input ): array {
		$input   = is_array( $input ) ? $input : array();
		$current = $this->all();

		return array(
			'address'       => $this->sanitize_address( $input['address'] ?? '', $current['address'] ),
			'site_id'       => $this->sanitize_site_id( $input['site_id'] ?? '', $current['site_id'] ),
			'counted_roles' => self::existing_roles( $input['counted_roles'] ?? array() ),
		);
	}

	/**
	 * Reduces an address to the form the plugin prints, or to nothing when it cannot be used.
	 *
	 * The scheme and host are lower-cased and a trailing slash is removed. Anything with a user
	 * name, a password, a query or a fragment is refused, as is anything outside the characters
	 * {@see self::ADDRESS_PATTERN} allows.
	 *
	 * @param string $value The address as entered.
	 * @return string The address, or an empty string when it cannot be used.
	 */
	public static function normalise_address( string $value ): string {
		if ( 1 !== preg_match( self::ADDRESS_PATTERN, trim( $value ), $parts ) ) {
			return '';
		}

		// The path's group always takes part in a match, so the port's group is always present,
		// empty when the address names no port.
		$port = $parts[3];

		if ( '' !== $port && ( (int) $port < 1 || (int) $port > self::HIGHEST_PORT ) ) {
			return '';
		}

		return strtolower( $parts[1] ) . '://' . strtolower( $parts[2] ) . ( '' === $port ? '' : ':' . $port ) . $parts[4];
	}

	/**
	 * Cleans the address field.
	 *
	 * Somebody who may not change the address keeps the saved one whatever they send, emptied
	 * included, and is told who can change it only when they sent a different address: the saved
	 * one, or the same address written another way, such as with a trailing slash, is no change.
	 * The comparison is with what is saved, so the second cleaning of a first save, which receives
	 * the saved address back, reports nothing.
	 *
	 * @param mixed  $value   What was entered.
	 * @param string $current The address saved now.
	 * @return string The address to save.
	 */
	private function sanitize_address( $value, string $current ): string {
		$entered = is_string( $value ) ? trim( $value ) : '';
		$address = '' === $entered ? '' : self::normalise_address( $entered );

		if ( ! self::may_change_address() ) {
			if ( $entered !== $current && ( '' === $address || self::normalise_address( $current ) !== $address ) ) {
				add_settings_error( self::OPTION, 'dewiride-analytics-address', esc_html( self::address_lock_message() ) );
			}

			return $current;
		}

		// An emptied field turns counting off, and needs no test.
		$problem = '' === $entered ? '' : $this->address_problem( $address );

		if ( '' !== $problem ) {
			add_settings_error( self::OPTION, 'dewiride-analytics-address', $problem );

			return $current;
		}

		return $address;
	}

	/**
	 * What stops an address from being used, in the words the settings page shows.
	 *
	 * A page served over https cannot load a script over http, so a secure site needs a secure
	 * address. Any other site's visitors report to it as well, so the address is an https one
	 * unless it names this computer or a private network, where what visitors send never crosses
	 * the internet. On a network the address must also pass WordPress's own test for addresses it
	 * may fetch, so that no site's setting points the network's servers at an address WordPress
	 * would refuse to reach. Only somebody who may change the address gets this far — on a network,
	 * its administrators or WP-CLI — so the message speaks to them: an address is allowed by
	 * widening that test through WordPress's own filters, in the network's code.
	 *
	 * @param string $address The normalised address, or an empty string when it could not be read.
	 * @return string The problem, or an empty string when there is none.
	 */
	private function address_problem( string $address ): string {
		$problem = '';

		if ( '' === $address ) {
			$problem = esc_html__( 'Enter the address you open Dewiride Analytics on, like https://analytics.example.com.', 'dewiride-analytics' );
		} elseif ( self::is_mixed_content( $address ) ) {
			$problem = esc_html__( 'Your site is served over https, so the address must start with https:// too.', 'dewiride-analytics' );
		} elseif ( 0 === strpos( $address, 'http://' ) && ! Private_Host::matches( $address ) ) {
			$problem = esc_html__( 'Use the address starting with https:// that you open Dewiride Analytics on. http:// is accepted only for a private address, such as http://localhost or http://192.168.1.20, or a name ending in .local, .internal, .home.arpa or .test.', 'dewiride-analytics' );
		} elseif ( is_multisite() && false === wp_http_validate_url( $address ) ) {
			$problem = esc_html__( 'Sites on a network can\'t use a private or unusual address. Use the public https address you open Dewiride Analytics on, or allow this address in your network\'s code.', 'dewiride-analytics' );
		}

		return $problem;
	}

	/**
	 * Cleans the Website ID field.
	 *
	 * @param mixed  $value   What was entered.
	 * @param string $current The Website ID saved now.
	 * @return string The Website ID to save.
	 */
	private function sanitize_site_id( $value, string $current ): string {
		$entered = is_string( $value ) ? strtolower( trim( $value ) ) : '';

		if ( '' === $entered ) {
			return '';
		}

		if ( ! wp_is_uuid( $entered ) ) {
			add_settings_error(
				self::OPTION,
				'dewiride-analytics-site-id',
				esc_html__( 'That Website ID isn\'t complete. Copy it again from Dewiride Analytics and paste it here.', 'dewiride-analytics' )
			);

			return $current;
		}

		return $entered;
	}

	/**
	 * The roles from a list that exist on this site, each once.
	 *
	 * @param mixed $roles What was ticked.
	 * @return list<string>
	 */
	private static function existing_roles( $roles ): array {
		if ( ! is_array( $roles ) ) {
			return array();
		}

		$existing = array_map( 'strval', array_keys( wp_roles()->roles ) );

		return array_values(
			array_unique(
				array_filter(
					$roles,
					static function ( $role ) use ( $existing ): bool {
						return is_string( $role ) && in_array( $role, $existing, true );
					}
				)
			)
		);
	}
}
