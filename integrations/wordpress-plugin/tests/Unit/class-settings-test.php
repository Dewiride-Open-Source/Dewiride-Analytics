<?php
/**
 * Tests for the plugin's settings.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Settings;
use Mockery;

/**
 * Reading, registering and cleaning the settings.
 */
final class Settings_Test extends Test_Case {

	/**
	 * The address of an installation on the same machine, which an http site may use.
	 */
	private const LOCAL_ADDRESS = 'http://localhost:3000';

	/**
	 * Another analytics address, which only somebody allowed to add code to the site may save.
	 */
	private const OTHER_ADDRESS = 'https://analytics.example.org';

	/**
	 * An analytics address reached over http.
	 */
	private const HTTP_ADDRESS = 'http://analytics.example.com';

	/**
	 * A site served over https.
	 */
	private const HTTPS_HOME = 'https://example.com';

	/**
	 * A site served over http.
	 */
	private const HTTP_HOME = 'http://example.com';

	/**
	 * What somebody who may not change the address is told on a single site.
	 */
	private const SITE_LOCK = 'Only someone allowed to add code to this site can change the address. Ask whoever looks after your site to change it for you.';

	/**
	 * What a site's administrator on a network is told.
	 */
	private const NETWORK_LOCK = 'Only a network administrator can change the address. Ask one to change it for you.';

	/**
	 * Whether the person saving the form holds every capability asked about.
	 *
	 * @var bool
	 */
	private $capable = true;

	/**
	 * The capabilities asked about, in order.
	 *
	 * @var list<string>
	 */
	private $capabilities_asked = array();

	/**
	 * Starts each test from a site with three roles, WordPress's own UUID test, and somebody
	 * allowed to add code to the site saving the form.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_roles(
			array(
				'administrator' => 'Administrator',
				'editor'        => 'Editor',
				'subscriber'    => 'Subscriber',
			)
		);
		Functions\when( 'wp_is_uuid' )->alias(
			static function ( $uuid ): bool {
				return is_string( $uuid ) && 1 === preg_match( '/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/', $uuid );
			}
		);
		Functions\when( 'current_user_can' )->alias(
			function ( string $capability ): bool {
				$this->capabilities_asked[] = $capability;

				return $this->capable;
			}
		);
	}

	/**
	 * Addresses that can be used, and the form each is saved in.
	 *
	 * @return array<string, array{string, string}>
	 */
	public function usable_addresses(): array {
		return array(
			'a host'                          => array( self::ADDRESS, self::ADDRESS ),
			'a trailing slash'                => array( self::ADDRESS . '/', self::ADDRESS ),
			'capitals in the scheme and host' => array( 'HTTPS://Analytics.Example.COM', self::ADDRESS ),
			'surrounding spaces'              => array( '  ' . self::ADDRESS . '  ', self::ADDRESS ),
			'a port'                          => array( self::LOCAL_ADDRESS, self::LOCAL_ADDRESS ),
			'a path'                          => array( 'https://example.com/analytics/', 'https://example.com/analytics' ),
			'a path whose case is kept'       => array( 'https://example.com/Analytics', 'https://example.com/Analytics' ),
			'an IPv4 address'                 => array( 'http://192.0.2.10', 'http://192.0.2.10' ),
			'a percent escape'                => array( 'https://example.com/a%20b', 'https://example.com/a%20b' ),
		);
	}

	/**
	 * Addresses that cannot be used.
	 *
	 * @return array<string, array{string}>
	 */
	public function unusable_addresses(): array {
		return array(
			'no scheme'                 => array( 'analytics.example.com' ),
			'another scheme'            => array( 'ftp://analytics.example.com' ),
			'a user name'               => array( 'https://user@analytics.example.com' ),
			'a query'                   => array( 'https://analytics.example.com/?site=1' ),
			'an empty query'            => array( 'https://analytics.example.com/?' ),
			'a fragment'                => array( 'https://analytics.example.com/#top' ),
			'port nought'               => array( 'https://analytics.example.com:0' ),
			'a port too high'           => array( 'https://analytics.example.com:65536' ),
			'an IPv6 literal'           => array( 'http://[2001:db8::1]' ),
			'a dot-dot segment'         => array( 'https://example.com/a/../b' ),
			'a space in the host'       => array( 'https://analytics example.com' ),
			'a quote in the path'       => array( 'https://example.com/a"b' ),
			'a backslash'               => array( 'https://example.com\\@evil.example' ),
			'a host ending in a hyphen' => array( 'https://analytics-.example.com' ),
		);
	}

	/**
	 * A usable address is saved in its normal form.
	 *
	 * @dataProvider usable_addresses
	 *
	 * @param string $entered  What was entered.
	 * @param string $expected What is saved.
	 */
	public function test_a_usable_address_is_normalised( string $entered, string $expected ): void {
		self::assertSame( $expected, Settings::normalise_address( $entered ) );
	}

	/**
	 * An unusable address normalises to nothing.
	 *
	 * @dataProvider unusable_addresses
	 *
	 * @param string $entered What was entered.
	 */
	public function test_an_unusable_address_is_refused( string $entered ): void {
		self::assertSame( '', Settings::normalise_address( $entered ) );
	}

	/**
	 * Whatever the option holds, it reads back as the types it is meant to have.
	 */
	public function test_a_damaged_option_reads_as_nothing_saved(): void {
		Functions\when( 'get_option' )->justReturn(
			array(
				'address'       => array( 'not', 'text' ),
				'site_id'       => 42,
				'counted_roles' => array( 'editor', 7, null ),
			)
		);

		$settings = new Settings();

		self::assertSame(
			array(
				'address'       => '',
				'site_id'       => '',
				'counted_roles' => array( 'editor' ),
			),
			$settings->all()
		);
		self::assertFalse( $settings->is_complete() );
	}

	/**
	 * An option that is not an array at all reads as the defaults.
	 */
	public function test_an_option_that_is_not_an_array_reads_as_the_defaults(): void {
		Functions\when( 'get_option' )->justReturn( 'nonsense' );

		self::assertSame( Settings::defaults(), ( new Settings() )->all() );
	}

	/**
	 * The beacon's and the collector's addresses are built from the saved address.
	 */
	public function test_the_script_and_collector_addresses_follow_the_saved_address(): void {
		$this->given_settings( 'https://example.com/analytics' );

		$settings = new Settings();

		self::assertTrue( $settings->is_complete() );
		self::assertSame( 'https://example.com/analytics/dw.js', $settings->script_url() );
		self::assertSame( 'https://example.com/analytics/collect', $settings->collector_url() );
	}

	/**
	 * Counting is on only once both values are saved.
	 */
	public function test_counting_needs_both_the_address_and_the_website_id(): void {
		$this->given_settings( self::ADDRESS, '' );

		self::assertFalse( ( new Settings() )->is_complete() );
	}

	/**
	 * The setting is registered with its cleaning, its defaults, and out of the REST API.
	 */
	public function test_the_setting_is_registered_with_its_sanitising_and_defaults(): void {
		$settings = new Settings();

		Functions\expect( 'register_setting' )
			->once()
			->with(
				Settings::GROUP,
				Settings::OPTION,
				array(
					'type'              => 'array',
					'show_in_rest'      => false,
					'sanitize_callback' => array( $settings, 'sanitize' ),
					'default'           => Settings::defaults(),
				)
			);

		$settings->register_setting();
	}

	/**
	 * Activation creates the option, autoloaded.
	 */
	public function test_the_option_is_created_autoloaded(): void {
		Functions\expect( 'add_option' )->once()->with( Settings::OPTION, Settings::defaults(), '', true );

		Settings::create();
	}

	/**
	 * What the form sends is saved in its clean form, with no message.
	 */
	public function test_usable_values_are_saved_clean(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize(
			array(
				'address'       => ' https://Analytics.Example.com/ ',
				'site_id'       => strtoupper( self::SITE_ID ),
				'counted_roles' => array( 'editor', 'ghost', 'editor', 3 ),
			)
		);

		self::assertSame(
			array(
				'address'       => self::ADDRESS,
				'site_id'       => self::SITE_ID,
				'counted_roles' => array( 'editor' ),
			),
			$saved
		);
	}

	/**
	 * Cleaning what was already cleaned changes nothing and reports nothing, which is how the
	 * Settings API saves the option the first time.
	 */
	public function test_sanitising_twice_changes_nothing_the_second_time(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'add_settings_error' )->once();

		$settings = new Settings();
		$once     = $settings->sanitize(
			array(
				'address'       => 'not an address',
				'site_id'       => self::SITE_ID,
				'counted_roles' => array( 'subscriber' ),
			)
		);

		self::assertSame( $once, $settings->sanitize( $once ) );
	}

	/**
	 * A mistyped address is refused with a message and the working one is kept.
	 */
	public function test_an_unusable_address_keeps_the_saved_one(): void {
		$this->given_settings();
		Functions\expect( 'add_settings_error' )
			->once()
			->with( Settings::OPTION, 'dewiride-analytics-address', Mockery::type( 'string' ) );

		$saved = ( new Settings() )->sanitize(
			array(
				'address' => 'https://analytics.example.com/?oops',
				'site_id' => self::SITE_ID,
			)
		);

		self::assertSame( self::ADDRESS, $saved['address'] );
	}

	/**
	 * An https site cannot load the beacon over http.
	 */
	public function test_an_https_site_refuses_an_http_address(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'add_settings_error' )
			->once()
			->with( Settings::OPTION, 'dewiride-analytics-address', Mockery::pattern( '/https/' ) );

		$saved = ( new Settings() )->sanitize( array( 'address' => self::HTTP_ADDRESS ) );

		self::assertSame( '', $saved['address'] );
	}

	/**
	 * An http site may use an http address.
	 */
	public function test_an_http_site_accepts_an_http_address(): void {
		$this->given_settings( '', '' );
		Functions\when( 'home_url' )->justReturn( self::HTTP_HOME );
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize( array( 'address' => self::LOCAL_ADDRESS ) );

		self::assertSame( self::LOCAL_ADDRESS, $saved['address'] );
	}

	/**
	 * On a network the address must also pass WordPress's test for addresses it may fetch, and the
	 * network administrator who sent it is told what else to use or how to allow it.
	 */
	public function test_a_network_refuses_an_address_wordpress_would_not_fetch(): void {
		$this->given_settings( '', '' );
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\expect( 'wp_http_validate_url' )->once()->with( 'https://10.0.0.5' )->andReturn( false );
		Functions\expect( 'add_settings_error' )
			->once()
			->with( Settings::OPTION, 'dewiride-analytics-address', Mockery::pattern( '/a private or unusual address\. Use the public https address .+, or allow this address in your network/' ) );

		$saved = ( new Settings() )->sanitize( array( 'address' => 'https://10.0.0.5' ) );

		self::assertSame( '', $saved['address'] );
	}

	/**
	 * On a network an address WordPress would fetch is saved.
	 */
	public function test_a_network_accepts_an_address_wordpress_would_fetch(): void {
		$this->given_settings( '', '' );
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\when( 'wp_http_validate_url' )->returnArg();
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize( array( 'address' => self::ADDRESS ) );

		self::assertSame( self::ADDRESS, $saved['address'] );
	}

	/**
	 * An incomplete Website ID is refused with a message and the saved one is kept.
	 */
	public function test_an_incomplete_website_id_keeps_the_saved_one(): void {
		$this->given_settings();
		Functions\expect( 'add_settings_error' )
			->once()
			->with( Settings::OPTION, 'dewiride-analytics-site-id', Mockery::type( 'string' ) );

		$saved = ( new Settings() )->sanitize(
			array(
				'address' => self::ADDRESS,
				'site_id' => substr( self::SITE_ID, 0, 20 ),
			)
		);

		self::assertSame( self::SITE_ID, $saved['site_id'] );
	}

	/**
	 * Emptying both fields turns counting off without a message.
	 */
	public function test_empty_fields_clear_the_settings(): void {
		$this->given_settings();
		Functions\expect( 'add_settings_error' )->never();

		self::assertSame(
			Settings::defaults(),
			( new Settings() )->sanitize(
				array(
					'address' => '   ',
					'site_id' => '',
				)
			)
		);
	}

	/**
	 * Anything other than what the form sends is treated as nothing entered.
	 */
	public function test_input_of_the_wrong_shape_is_treated_as_empty(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'add_settings_error' )->never();

		$settings = new Settings();

		self::assertSame( Settings::defaults(), $settings->sanitize( 'nonsense' ) );
		self::assertSame(
			Settings::defaults(),
			$settings->sanitize(
				array(
					'address'       => array( self::ADDRESS ),
					'site_id'       => 12,
					'counted_roles' => 'editor',
				)
			)
		);
	}

	/**
	 * Somebody allowed to add code to the site may point the plugin at another address.
	 */
	public function test_somebody_allowed_to_add_code_may_change_the_address(): void {
		$this->given_settings();
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize(
			array(
				'address' => self::OTHER_ADDRESS,
				'site_id' => self::SITE_ID,
			)
		);

		self::assertSame( self::OTHER_ADDRESS, $saved['address'] );
		self::assertSame( array( 'unfiltered_html' ), $this->capabilities_asked );
	}

	/**
	 * Without that, the saved address is kept and the reason given, while the Website ID and the
	 * roles, which load nothing, still change.
	 */
	public function test_somebody_not_allowed_to_add_code_keeps_the_saved_address(): void {
		$this->given_settings();
		$this->capable = false;
		Functions\expect( 'add_settings_error' )->once()->with( Settings::OPTION, 'dewiride-analytics-address', self::SITE_LOCK );

		$saved = ( new Settings() )->sanitize(
			array(
				'address'       => self::OTHER_ADDRESS,
				'site_id'       => '9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d',
				'counted_roles' => array( 'editor' ),
			)
		);

		self::assertSame(
			array(
				'address'       => self::ADDRESS,
				'site_id'       => '9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d',
				'counted_roles' => array( 'editor' ),
			),
			$saved
		);
		self::assertSame( array( 'unfiltered_html' ), $this->capabilities_asked );
	}

	/**
	 * A site's administrator on a network is told that a network administrator can change it, and
	 * the address is not even tested as one the network could reach.
	 */
	public function test_a_sites_administrator_on_a_network_is_sent_to_a_network_administrator(): void {
		$this->given_settings();
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\when( 'is_super_admin' )->justReturn( false );
		$this->capable = false;
		Functions\expect( 'wp_http_validate_url' )->never();
		Functions\expect( 'add_settings_error' )->once()->with( Settings::OPTION, 'dewiride-analytics-address', self::NETWORK_LOCK );

		$saved = ( new Settings() )->sanitize( array( 'address' => self::OTHER_ADDRESS ) );

		self::assertSame( self::ADDRESS, $saved['address'] );
	}

	/**
	 * The saved address sent back as it is shown, or written another way.
	 *
	 * @return array<string, array{string}>
	 */
	public function the_saved_address_sent_back(): array {
		return array(
			'as it is shown'        => array( self::ADDRESS ),
			'with a trailing slash' => array( self::ADDRESS . '/' ),
			'in capitals'           => array( 'HTTPS://Analytics.Example.com' ),
			'with spaces around'    => array( '  ' . self::ADDRESS . ' ' ),
		);
	}

	/**
	 * The read-only field sends the saved address back, which is no change and needs no message.
	 *
	 * @dataProvider the_saved_address_sent_back
	 *
	 * @param string $sent What the form sent.
	 */
	public function test_the_saved_address_sent_back_is_kept_without_a_message( string $sent ): void {
		$this->given_settings();
		$this->capable = false;
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize(
			array(
				'address' => $sent,
				'site_id' => self::SITE_ID,
			)
		);

		self::assertSame( self::ADDRESS, $saved['address'] );
	}

	/**
	 * Emptying the address is a change too, and is refused the same way.
	 */
	public function test_somebody_not_allowed_to_add_code_cannot_empty_the_address(): void {
		$this->given_settings();
		$this->capable = false;
		Functions\expect( 'add_settings_error' )->once()->with( Settings::OPTION, 'dewiride-analytics-address', self::SITE_LOCK );

		$saved = ( new Settings() )->sanitize(
			array(
				'address' => '',
				'site_id' => self::SITE_ID,
			)
		);

		self::assertSame( self::ADDRESS, $saved['address'] );
	}

	/**
	 * A refused first save is reported once, though WordPress cleans a first save twice.
	 */
	public function test_a_refused_first_save_is_reported_once(): void {
		$this->given_settings( '', '' );
		$this->capable = false;
		Functions\expect( 'add_settings_error' )->once();

		$settings = new Settings();
		$once     = $settings->sanitize(
			array(
				'address' => self::ADDRESS,
				'site_id' => self::SITE_ID,
			)
		);

		self::assertSame( '', $once['address'] );
		self::assertSame( $once, $settings->sanitize( $once ) );
	}

	/**
	 * WP-CLI changes the address whoever it runs as, since whoever runs it can already run any
	 * code on the server.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_wp_cli_may_change_the_address(): void {
		define( 'WP_CLI', true ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedConstantFound -- WP-CLI's own constant, defined as WP-CLI defines it.
		$this->given_settings();
		$this->capable = false;
		Functions\expect( 'add_settings_error' )->never();

		$saved = ( new Settings() )->sanitize( array( 'address' => self::OTHER_ADDRESS ) );

		self::assertTrue( Settings::may_change_address() );
		self::assertSame( self::OTHER_ADDRESS, $saved['address'] );
		self::assertSame( array(), $this->capabilities_asked );
	}

	/**
	 * Who is named as able to change the address, by kind of site and of person.
	 *
	 * @return array<string, array{bool, bool, string}>
	 */
	public function lock_messages(): array {
		return array(
			'a single site'           => array( false, false, self::SITE_LOCK ),
			'a site of a network'     => array( true, false, self::NETWORK_LOCK ),
			'a network administrator' => array( true, true, self::SITE_LOCK ),
		);
	}

	/**
	 * A network administrator, who is refused only where nobody may add code, is never told to
	 * ask a network administrator.
	 *
	 * @dataProvider lock_messages
	 *
	 * @param bool   $network     Whether the site is part of a network.
	 * @param bool   $super_admin Whether the person is a network administrator.
	 * @param string $message     What they are told.
	 */
	public function test_the_lock_names_who_can_change_the_address( bool $network, bool $super_admin, string $message ): void {
		Functions\when( 'is_multisite' )->justReturn( $network );
		Functions\when( 'is_super_admin' )->justReturn( $super_admin );

		self::assertSame( $message, Settings::address_lock_message() );
	}

	/**
	 * Addresses on sites served one way or the other, and whether browsers refuse them.
	 *
	 * @return array<string, array{string, string, bool}>
	 */
	public function mixed_content(): array {
		return array(
			'http on an https site'      => array( self::HTTP_ADDRESS, self::HTTPS_HOME, true ),
			'http in capitals'           => array( 'HTTP://analytics.example.com', self::HTTPS_HOME, true ),
			'https on an https site'     => array( self::ADDRESS, self::HTTPS_HOME, false ),
			'http on an http site'       => array( self::HTTP_ADDRESS, self::HTTP_HOME, false ),
			'no address on an https one' => array( '', self::HTTPS_HOME, false ),
		);
	}

	/**
	 * Only an http address on a site served over https is refused by browsers.
	 *
	 * @dataProvider mixed_content
	 *
	 * @param string $address  The analytics address.
	 * @param string $home     The site's address.
	 * @param bool   $expected Whether browsers refuse it.
	 */
	public function test_only_http_on_an_https_site_is_mixed_content( string $address, string $home, bool $expected ): void {
		Functions\when( 'home_url' )->justReturn( $home );

		self::assertSame( $expected, Settings::is_mixed_content( $address ) );
	}
}
