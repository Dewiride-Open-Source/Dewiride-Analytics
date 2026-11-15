<?php
/**
 * Tests for the settings of the server's reports.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Filters;
use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Settings;
use Mockery;

/**
 * The key the form carries is saved sealed and kept out of the settings, the switches are read as
 * a form sends them, and how visitors reach the site is refused when it could not be believed.
 */
final class Server_Settings_Sanitizing_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them; plainly not a real one.
	 */
	private const KEY = 'dwk_fffffffffffffffffffffffffffffffffffffffffff';

	/**
	 * The options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

	/**
	 * Starts each test set up, with every role there is and somebody allowed to add code saving.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->options = array(
			Settings::OPTION => array(
				'address' => self::ADDRESS,
				'site_id' => self::SITE_ID,
			),
		);
		Functions\when( 'get_option' )->alias(
			function ( string $option, $fallback = false ) {
				return $this->options[ $option ] ?? $fallback;
			}
		);
		Functions\when( 'update_option' )->alias(
			function ( string $option, $value ): bool {
				$this->options[ $option ] = $value;

				return true;
			}
		);
		Functions\when( 'delete_option' )->alias(
			function ( string $option ): bool {
				unset( $this->options[ $option ] );

				return true;
			}
		);
		Functions\when( 'wp_is_uuid' )->justReturn( true );
		Functions\when( 'current_user_can' )->justReturn( true );
		$this->given_roles( array( 'administrator' => 'Administrator' ) );
	}

	/**
	 * A key pasted into the form is saved sealed, and only its ending stays in the settings.
	 */
	public function test_a_pasted_key_is_saved_sealed_and_only_its_ending_kept(): void {
		Functions\expect( 'add_settings_error' )->never();

		$saved = $this->sanitize( array( 'server_key' => ' ' . self::KEY . ' ' ) );

		self::assertSame( 'ffff', $saved['key_ending'] );
		self::assertArrayNotHasKey( 'server_key', $saved );
		self::assertSame( self::KEY, ( new Server_Key() )->key() );
		self::assertStringNotContainsString( self::KEY, (string) wp_json_encode( $saved ) );
	}

	/**
	 * Cleaning the result again, as the Settings API does on a first save, keeps the ending.
	 */
	public function test_cleaning_twice_keeps_the_key(): void {
		$settings = new Settings();
		$once     = $settings->sanitize( array( 'server_key' => self::KEY ) );

		self::assertSame( $once, $settings->sanitize( $once ) );
	}

	/**
	 * A key that is not complete is refused, and the saved one kept.
	 */
	public function test_an_incomplete_key_is_refused_and_the_saved_one_kept(): void {
		( new Server_Key() )->save( self::KEY );
		Functions\expect( 'add_settings_error' )->once()->with( Settings::OPTION, 'dewiride-analytics-server-key', Mockery::pattern( '/server key isn&#039;t complete/' ) );

		$saved = $this->sanitize( array( 'server_key' => substr( self::KEY, 0, 20 ) ) );

		self::assertSame( 'ffff', $saved['key_ending'] );
		self::assertSame( self::KEY, ( new Server_Key() )->key() );
	}

	/**
	 * Ticking the box removes the saved key.
	 */
	public function test_the_box_removes_the_saved_key(): void {
		( new Server_Key() )->save( self::KEY );

		$saved = $this->sanitize( array( 'forget_server_key' => '1' ) );

		self::assertSame( '', $saved['key_ending'] );
		self::assertArrayNotHasKey( Server_Key::OPTION, $this->options );
	}

	/**
	 * The switches are what the form ticked, and nothing else.
	 */
	public function test_the_switches_are_what_the_form_ticked(): void {
		$saved = $this->sanitize(
			array(
				'report_server'  => '1',
				'report_sign_in' => '1',
			)
		);

		self::assertTrue( $saved['report_server'] );
		self::assertFalse( $saved['report_unfinished'] );
		self::assertFalse( $saved['report_feeds'] );
		self::assertTrue( $saved['report_sign_in'] );
	}

	/**
	 * Proxies' addresses are read one to a line, or separated by commas or spaces, written each
	 * as an address and the length of its prefix.
	 */
	public function test_proxies_addresses_are_read_and_written_in_one_form(): void {
		Functions\expect( 'add_settings_error' )->never();

		$saved = $this->sanitize(
			array(
				'proxy_mode'    => Settings::THROUGH_PROXY,
				'proxy_header'  => ' X-Real-IP ',
				'proxy_trusted' => "10.0.0.0/8\n203.0.113.9, 2001:DB8::/32 10.0.0.0/8 2a00::/16",
			)
		);

		self::assertSame( Settings::THROUGH_PROXY, $saved['proxy_mode'] );
		self::assertSame( 'X-Real-IP', $saved['proxy_header'] );
		self::assertSame( array( '10.0.0.0/8', '203.0.113.9/32', '2001:db8::/32', '2a00::/16' ), $saved['proxy_trusted'] );
	}

	/**
	 * Ways of naming proxies that could not be believed, and what the form is told.
	 *
	 * @return array<string, array{array<string, string>}>
	 */
	public function unbelievable_proxies(): array {
		return array(
			'every address on the internet'    => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => '0.0.0.0/0',
				),
			),
			'something that is not an address' => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => "10.0.0.0/8\nmy-proxy",
				),
			),
			'no address at all'                => array( array( 'proxy_mode' => Settings::THROUGH_PROXY ) ),
			'half of the internet'             => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => "0.0.0.0/1\n128.0.0.0/1",
				),
			),
			'one IPv4 range wider than /8'     => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => '10.0.0.0/8, 100.0.0.0/7',
				),
			),
			'one IPv6 range wider than /16'    => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => '2001:db8::/32, 2a00::/15',
				),
			),
			'every public IPv6 address'        => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => '2000::/3',
				),
			),
			'more proxies than any site has'   => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_trusted' => implode(
						"\n",
						array_map(
							static function ( int $last ): string {
								return '10.0.0.' . $last;
							},
							range( 1, 51 )
						)
					),
				),
			),
			'a header that is not a name'      => array(
				array(
					'proxy_mode'    => Settings::THROUGH_PROXY,
					'proxy_header'  => 'X-Forwarded-For: 1.2.3.4',
					'proxy_trusted' => '10.0.0.0/8',
				),
			),
		);
	}

	/**
	 * Proxies that could not be believed are refused, with a message, and what was saved kept.
	 *
	 * @dataProvider unbelievable_proxies
	 *
	 * @param array<string, string> $input What the form sent.
	 */
	public function test_proxies_that_could_not_be_believed_are_refused( array $input ): void {
		Functions\expect( 'add_settings_error' )->once()->with( Settings::OPTION, 'dewiride-analytics-proxy', Mockery::type( 'string' ) );

		$saved = $this->sanitize( $input );

		self::assertSame( Settings::AUTOMATIC, $saved['proxy_mode'] );
		self::assertSame( array(), $saved['proxy_trusted'] );
	}

	/**
	 * Reporting needs the switch, and where the server cannot finish a page first, the second
	 * one too.
	 */
	public function test_reporting_needs_its_switches(): void {
		$settings = new Settings();

		self::assertTrue( $settings->reports_server( true ) );
		self::assertFalse( $settings->reports_server( false ) );

		$this->options[ Settings::OPTION ] = array( 'report_unfinished' => true ) + self::as_array( $this->options[ Settings::OPTION ] );
		self::assertTrue( $settings->reports_server( false ) );

		$this->options[ Settings::OPTION ] = array( 'report_server' => false ) + self::as_array( $this->options[ Settings::OPTION ] );
		self::assertFalse( $settings->reports_server( true ) );
	}

	/**
	 * Code may send the server's reports to another address than the browsers', and an address
	 * it gives that cannot be used is ignored.
	 */
	public function test_code_may_name_another_address_for_the_server(): void {
		Filters\expectApplied( 'dewiride_analytics_server_address' )->once()->with( self::ADDRESS )->andReturn( 'http://web:3000/' );
		self::assertSame( 'http://web:3000', ( new Settings() )->server_address() );

		Filters\expectApplied( 'dewiride_analytics_server_address' )->once()->andReturn( 'not an address' );
		self::assertSame( self::ADDRESS, ( new Settings() )->server_address() );
	}


	/**
	 * Saving a key starts the reports' state afresh, so a key pasted again after it stopped opening
	 * is tried at once.
	 */
	public function test_saving_a_key_starts_the_reports_afresh(): void {
		$this->options[ Reporting_Status::OPTION ] = array( 'stopped' => Reporting_Status::KEY_UNREADABLE );

		$this->sanitize( array( 'server_key' => self::KEY ) );

		self::assertArrayNotHasKey( Reporting_Status::OPTION, $this->options );
	}

	/**
	 * Switches written as text — as WP-CLI writes them — read as what they say.
	 */
	public function test_switches_written_as_text_read_as_they_say(): void {
		$this->options[ Settings::OPTION ] = array(
			'report_server'  => 'false',
			'report_sign_in' => '1',
		) + self::as_array( $this->options[ Settings::OPTION ] );

		$settings = ( new Settings() )->all();

		self::assertFalse( $settings['report_server'] );
		self::assertTrue( $settings['report_sign_in'] );
		self::assertFalse( $this->sanitize( array( 'report_server' => 'false' ) )['report_server'] );
	}

	/**
	 * A Website ID written in capitals — as WP-CLI can write it — reads as the one the settings
	 * page would have saved, so it still matches the website Dewiride Analytics names.
	 */
	public function test_a_website_id_written_in_capitals_reads_in_lower_case(): void {
		$this->options[ Settings::OPTION ] = array( 'site_id' => strtoupper( self::SITE_ID ) ) + self::as_array( $this->options[ Settings::OPTION ] );

		self::assertSame( self::SITE_ID, ( new Settings() )->site_id() );
	}

	/**
	 * Cleans what a form sent, from the settings saved in each test.
	 *
	 * @param array<string, mixed> $input What the form sent, beyond the address and Website ID.
	 * @return array<string, mixed>
	 */
	private function sanitize( array $input ): array {
		return ( new Settings() )->sanitize(
			$input + array(
				'address' => self::ADDRESS,
				'site_id' => self::SITE_ID,
			)
		);
	}
}
