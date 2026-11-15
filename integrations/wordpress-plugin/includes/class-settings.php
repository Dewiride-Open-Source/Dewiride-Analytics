<?php
/**
 * The plugin's settings: where the analytics installation is, which website this is, which
 * signed-in roles are counted, and what the site's own server reports.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reads, registers and validates the plugin's settings, which are one option, and saves the
 * server key the settings form is given, which is kept apart from them.
 *
 * @phpstan-type Saved array{address: string, site_id: string, counted_roles: list<string>, key_ending: string, report_server: bool, report_unfinished: bool, report_feeds: bool, report_sign_in: bool, proxy_mode: string, proxy_header: string, proxy_trusted: list<string>}
 * @phpstan-type Proxy array{mode: string, header: string, trusted: list<string>}
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
	 * Visitors reach the site directly, or through Cloudflare, which the plugin recognises itself.
	 */
	public const AUTOMATIC = 'automatic';

	/**
	 * Visitors reach the site through a proxy named in the settings.
	 */
	public const THROUGH_PROXY = 'proxy';

	/**
	 * The header a proxy names the visitor in unless another is chosen.
	 */
	private const DEFAULT_PROXY_HEADER = 'X-Forwarded-For';

	/**
	 * The most proxy addresses the settings keep.
	 */
	private const MOST_PROXIES = 50;

	/**
	 * The shortest IPv4 prefix a proxy's range may have, and the shortest IPv6 one: a range wider
	 * than these believes a good part of the internet about where it is, which no proxy needs.
	 */
	private const SHORTEST_IPV4_PROXY_PREFIX = 8;

	/**
	 * See {@see self::SHORTEST_IPV4_PROXY_PREFIX}.
	 */
	private const SHORTEST_IPV6_PROXY_PREFIX = 16;

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
	 * The server's reports are on, so they start the moment a server key is saved, and so are
	 * feeds, robots.txt and sitemaps, which are what feed readers and crawlers ask for. Sign-in
	 * attempts and XML-RPC are off: attacks on them arrive by the thousand, and each would count
	 * as a page view.
	 *
	 * @return Saved
	 */
	public static function defaults(): array {
		return array(
			'address'           => '',
			'site_id'           => '',
			'counted_roles'     => array(),
			'key_ending'        => '',
			'report_server'     => true,
			'report_unfinished' => false,
			'report_feeds'      => true,
			'report_sign_in'    => false,
			'proxy_mode'        => self::AUTOMATIC,
			'proxy_header'      => self::DEFAULT_PROXY_HEADER,
			'proxy_trusted'     => array(),
		);
	}

	/**
	 * The saved settings, each read back as the type it is meant to be, and anything missing — a
	 * setting a newer version of the plugin added — as its default.
	 *
	 * The Website ID is read in lower case, as the settings page saves it, because a value written
	 * some other way — from WP-CLI, which no sanitising reaches — would otherwise fail to match the
	 * website Dewiride Analytics names in its answers.
	 *
	 * @return Saved
	 */
	public function all(): array {
		$stored   = get_option( self::OPTION, self::defaults() );
		$stored   = is_array( $stored ) ? $stored : array();
		$defaults = self::defaults();

		return array(
			'address'           => self::text( $stored, 'address', $defaults['address'] ),
			'site_id'           => strtolower( self::text( $stored, 'site_id', $defaults['site_id'] ) ),
			'counted_roles'     => self::texts( $stored, 'counted_roles' ),
			'key_ending'        => self::text( $stored, 'key_ending', $defaults['key_ending'] ),
			'report_server'     => self::flag( $stored, 'report_server', $defaults['report_server'] ),
			'report_unfinished' => self::flag( $stored, 'report_unfinished', $defaults['report_unfinished'] ),
			'report_feeds'      => self::flag( $stored, 'report_feeds', $defaults['report_feeds'] ),
			'report_sign_in'    => self::flag( $stored, 'report_sign_in', $defaults['report_sign_in'] ),
			'proxy_mode'        => self::THROUGH_PROXY === ( $stored['proxy_mode'] ?? null ) ? self::THROUGH_PROXY : self::AUTOMATIC,
			'proxy_header'      => self::text( $stored, 'proxy_header', $defaults['proxy_header'] ),
			'proxy_trusted'     => self::texts( $stored, 'proxy_trusted' ),
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
	 * The last characters of the server key reports are sent with, or an empty string when there
	 * is none: the one `wp-config.php` defines, or the one saved on the settings page.
	 *
	 * Read from the settings rather than from the key itself, which is not loaded with every page.
	 */
	public function key_ending(): string {
		if ( Server_Key::is_in_config() ) {
			$key = constant( Server_Key::CONSTANT );

			return is_string( $key ) && Server_Key::looks_valid( $key ) ? Server_Key::ending_of( $key ) : '';
		}

		return $this->all()['key_ending'];
	}

	/**
	 * Whether the site's own server reports what it answers, as far as the settings say: the
	 * switch is on, and where the server cannot finish a page before sending its report, sending
	 * it first was chosen too.
	 *
	 * @param bool $finishes_first Whether the server finishes a page before it reports it.
	 */
	public function reports_server( bool $finishes_first ): bool {
		$settings = $this->all();

		return $settings['report_server'] && ( $finishes_first || $settings['report_unfinished'] );
	}

	/**
	 * Whether feeds, robots.txt and sitemaps are reported.
	 */
	public function reports_feeds(): bool {
		return $this->all()['report_feeds'];
	}

	/**
	 * Whether sign-in attempts and XML-RPC requests are reported.
	 */
	public function reports_sign_in(): bool {
		return $this->all()['report_sign_in'];
	}

	/**
	 * How visitors reach the site: automatically, or through the proxies named, the header they
	 * name the visitor in, and their addresses.
	 *
	 * @return Proxy
	 */
	public function proxy(): array {
		$settings = $this->all();

		return array(
			'mode'    => $settings['proxy_mode'],
			'header'  => $settings['proxy_header'],
			'trusted' => $settings['proxy_trusted'],
		);
	}

	/**
	 * Where the site's own server sends its reports: the saved address, unless a filter names the
	 * address WordPress reaches the same installation on.
	 *
	 * A site and the installation can sit side by side on one private network, where visitors'
	 * browsers reach the installation at one address and WordPress reaches it at another. Only
	 * code can say so, through the `dewiride_analytics_server_address` filter, and an address it
	 * gives that cannot be used is ignored.
	 */
	public function server_address(): string {
		$address  = $this->address();
		$filtered = apply_filters( 'dewiride_analytics_server_address', $address );
		$filtered = is_string( $filtered ) ? self::normalise_address( $filtered ) : '';

		return '' === $filtered ? $address : $filtered;
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
	 * Ticked boxes are on and absent ones off, as a form sends them. The server key the form may
	 * carry is saved on its own, sealed, and only its last characters are kept here; see
	 * {@see self::sanitize_server_key()}.
	 *
	 * @param mixed $input What the settings form sent.
	 * @return Saved
	 */
	public function sanitize( $input ): array {
		$input   = is_array( $input ) ? $input : array();
		$current = $this->all();
		$proxy   = $this->sanitize_proxy( $input, $current );

		return array(
			'address'           => $this->sanitize_address( $input['address'] ?? '', $current['address'] ),
			'site_id'           => $this->sanitize_site_id( $input['site_id'] ?? '', $current['site_id'] ),
			'counted_roles'     => self::existing_roles( $input['counted_roles'] ?? array() ),
			'key_ending'        => self::sanitize_server_key( $input ),
			'report_server'     => self::ticked( $input['report_server'] ?? false ),
			'report_unfinished' => self::ticked( $input['report_unfinished'] ?? false ),
			'report_feeds'      => self::ticked( $input['report_feeds'] ?? false ),
			'report_sign_in'    => self::ticked( $input['report_sign_in'] ?? false ),
			'proxy_mode'        => $proxy['mode'],
			'proxy_header'      => $proxy['header'],
			'proxy_trusted'     => $proxy['trusted'],
		);
	}

	/**
	 * Saves or removes the server key the form carried, and gives the last characters of the key
	 * that is saved afterwards.
	 *
	 * The key itself never enters these settings. What is kept is worked out from the key as it is
	 * saved, so cleaning the result again — which the Settings API does on a first save — keeps
	 * the same ending rather than forgetting it. Saving or removing a key also starts the reports'
	 * state afresh, so a key pasted again after it stopped opening is tried at once.
	 *
	 * @param array<mixed> $input What the settings form sent.
	 * @return string The last characters of the saved key, or an empty string when none is saved.
	 */
	private static function sanitize_server_key( array $input ): string {
		$server_key = new Server_Key();
		$entered    = isset( $input['server_key'] ) && is_string( $input['server_key'] ) ? trim( $input['server_key'] ) : '';

		if ( self::ticked( $input['forget_server_key'] ?? false ) ) {
			$server_key->forget();
			delete_option( Reporting_Status::OPTION );
		} elseif ( Server_Key::looks_valid( $entered ) ) {
			if ( $server_key->save( $entered ) ) {
				delete_option( Reporting_Status::OPTION );
			} else {
				add_settings_error( self::OPTION, 'dewiride-analytics-server-key', esc_html__( 'The server key couldn\'t be saved. Save it again.', 'dewiride-analytics' ) );
			}
		} elseif ( '' !== $entered ) {
			add_settings_error( self::OPTION, 'dewiride-analytics-server-key', esc_html__( 'That server key isn\'t complete. Copy it again from Dewiride Analytics and paste it here.', 'dewiride-analytics' ) );
		}

		return Server_Key::SAVED === $server_key->source() ? Server_Key::ending_of( $server_key->key() ) : '';
	}

	/**
	 * Cleans how visitors reach the site.
	 *
	 * The header is a header's name and nothing else. The proxies' addresses are read one to a
	 * line, or separated by commas or spaces, each as an address or a range; a range wider than
	 * /8, or /16 for IPv6, is refused, since it would believe much of the internet about where it
	 * is. Choosing
	 * a proxy without naming its addresses is refused too, since no request could then be
	 * believed. Anything refused keeps what was saved, with a message.
	 *
	 * @param array<mixed> $input   What the settings form sent.
	 * @param array        $current The settings saved now.
	 * @phpstan-param Saved $current
	 * @return Proxy
	 */
	private function sanitize_proxy( array $input, array $current ): array {
		$saved   = array(
			'mode'    => $current['proxy_mode'],
			'header'  => $current['proxy_header'],
			'trusted' => $current['proxy_trusted'],
		);
		$mode    = self::THROUGH_PROXY === ( $input['proxy_mode'] ?? null ) ? self::THROUGH_PROXY : self::AUTOMATIC;
		$header  = isset( $input['proxy_header'] ) && is_string( $input['proxy_header'] ) ? trim( $input['proxy_header'] ) : '';
		$header  = '' === $header ? self::DEFAULT_PROXY_HEADER : $header;
		$trusted = self::proxy_ranges( $input['proxy_trusted'] ?? '' );
		$problem = '';

		if ( 1 !== preg_match( '/^[A-Za-z0-9-]{1,64}$/', $header ) ) {
			$problem = esc_html__( 'Enter the name of the header your proxy puts the visitor\'s address in, such as X-Forwarded-For.', 'dewiride-analytics' );
		} elseif ( null === $trusted ) {
			$problem = esc_html__( 'Enter your proxies\' addresses one to a line, each as an address like 203.0.113.10 or a range like 203.0.113.0/24, no wider than /8.', 'dewiride-analytics' );
		} elseif ( self::THROUGH_PROXY === $mode && array() === $trusted ) {
			$problem = esc_html__( 'Enter your proxies\' addresses, so that the plugin knows which requests to believe.', 'dewiride-analytics' );
		}

		if ( '' !== $problem || null === $trusted ) {
			add_settings_error( self::OPTION, 'dewiride-analytics-proxy', $problem );

			return $saved;
		}

		return array(
			'mode'    => $mode,
			'header'  => $header,
			'trusted' => $trusted,
		);
	}

	/**
	 * The proxies' addresses as written in the form, each as an address and the length of its
	 * prefix, or nothing when any of them cannot be used.
	 *
	 * @param mixed $value What the form sent: text, or a list already cleaned.
	 * @return list<string>|null
	 */
	private static function proxy_ranges( $value ): ?array {
		$entries = is_array( $value ) ? $value : preg_split( '/[\s,]+/', is_string( $value ) ? $value : '', -1, PREG_SPLIT_NO_EMPTY );
		$ranges  = array();

		if ( ! is_array( $entries ) ) {
			return null;
		}

		foreach ( $entries as $entry ) {
			$range = is_string( $entry ) ? Address_Range::parse( $entry ) : null;

			if ( null === $range || $range[1] < ( 4 === strlen( $range[0] ) ? self::SHORTEST_IPV4_PROXY_PREFIX : self::SHORTEST_IPV6_PROXY_PREFIX ) ) {
				return null;
			}

			$ranges[] = (string) inet_ntop( $range[0] ) . '/' . $range[1];
		}

		$ranges = array_values( array_unique( $ranges ) );

		return count( $ranges ) > self::MOST_PROXIES ? null : $ranges;
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

	/**
	 * A saved setting that is text, or its default when it is missing or something else.
	 *
	 * @param array<mixed> $stored   The saved settings.
	 * @param string       $name     The setting.
	 * @param string       $fallback What it is before anything is saved.
	 */
	private static function text( array $stored, string $name, string $fallback ): string {
		return isset( $stored[ $name ] ) && is_string( $stored[ $name ] ) ? $stored[ $name ] : $fallback;
	}

	/**
	 * A saved setting that is a list of text, with anything in it that is not text left out.
	 *
	 * @param array<mixed> $stored The saved settings.
	 * @param string       $name   The setting.
	 * @return list<string>
	 */
	private static function texts( array $stored, string $name ): array {
		return isset( $stored[ $name ] ) && is_array( $stored[ $name ] ) ? array_values( array_filter( $stored[ $name ], 'is_string' ) ) : array();
	}

	/**
	 * A saved setting that is on or off, or its default when it is missing or something else.
	 *
	 * @param array<mixed> $stored   The saved settings.
	 * @param string       $name     The setting.
	 * @param bool         $fallback What it is before anything is saved.
	 */
	private static function flag( array $stored, string $name, bool $fallback ): bool {
		return isset( $stored[ $name ] ) && is_scalar( $stored[ $name ] ) ? self::ticked( $stored[ $name ] ) : $fallback;
	}

	/**
	 * Whether a switch is on, however it was written: by the settings form, which sends `1` for a
	 * ticked box and nothing for one left empty, or by WP-CLI, which writes `false` as text.
	 *
	 * @param mixed $value The switch as written.
	 */
	private static function ticked( $value ): bool {
		return is_scalar( $value ) && wp_validate_boolean( $value );
	}
}
