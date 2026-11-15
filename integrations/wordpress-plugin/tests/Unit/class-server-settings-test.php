<?php
/**
 * Tests for the settings page's part about the server's reports.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Collector_Client;
use Dewiride\Analytics\WordPress\Connection_Check;
use Dewiride\Analytics\WordPress\Reporting_Status;
use Dewiride\Analytics\WordPress\Server_Key;
use Dewiride\Analytics\WordPress\Server_Settings;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tag_Check;
use Dewiride\Analytics\WordPress\Visitor_Address;

/**
 * The key is never shown, every choice is a field of the settings form, and every state the
 * reports can be in is a sentence that says what to do.
 */
final class Server_Settings_Test extends Test_Case {

	/**
	 * A key shaped as Dewiride Analytics issues them; plainly not a real one.
	 */
	private const KEY = 'dwk_eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee';

	/**
	 * The options in each test, by name.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

	/**
	 * Starts each test with counting on and no key, and the page's functions stood in.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->options = array(
			Settings::OPTION => array(
				'address'           => self::ADDRESS,
				'site_id'           => self::SITE_ID,
				'report_unfinished' => true,
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
		Functions\when( 'checked' )->alias(
			static function ( $checked, $current = true ): string {
				return is_scalar( $checked ) && is_scalar( $current ) && (string) $checked === (string) $current ? " checked='checked'" : '';
			}
		);
		Functions\when( 'human_time_diff' )->justReturn( '5 mins' );
		Functions\when( 'admin_url' )->returnArg();
		Functions\when( 'wp_nonce_field' )->justReturn( '' );
		Functions\when( 'submit_button' )->alias(
			static function ( string $text ): void {
				echo '<button>' . $text . '</button>'; // phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped -- A stand-in for WordPress's own button.
			}
		);
		$this->given_request( array( 'REMOTE_ADDR' => '203.0.113.7' ) );
	}

	/**
	 * Without a key, the field asks for one and says where it comes from.
	 */
	public function test_without_a_key_the_field_asks_for_one(): void {
		$output = $this->render( 'render_key' );

		self::assertStringContainsString( 'type="password"', $output );
		self::assertStringContainsString( 'name="dewiride_analytics_settings[server_key]" value=""', $output );
		self::assertStringContainsString( 'choose Tracking code, then WordPress', $output );
		self::assertStringNotContainsString( 'forget_server_key', $output );
	}

	/**
	 * A saved key is shown by its ending, never in full, with a box that removes it.
	 */
	public function test_a_saved_key_is_shown_only_by_its_ending(): void {
		$this->given_key();

		$output = $this->render( 'render_key' );

		self::assertStringNotContainsString( self::KEY, $output );
		self::assertStringContainsString( 'Saved, ending in eeee.', $output );
		self::assertStringContainsString( 'name="dewiride_analytics_settings[forget_server_key]"', $output );
	}

	/**
	 * Reporting is on to begin with, and on a server that holds pages open the second switch
	 * says what it costs.
	 */
	public function test_the_switches_say_what_they_cost(): void {
		$this->options[ Settings::OPTION ] = array_diff_key( self::as_array( $this->options[ Settings::OPTION ] ), array( 'report_unfinished' => true ) );

		$output = $this->render( 'render_reporting' );

		self::assertStringContainsString( 'name="dewiride_analytics_settings[report_server]" value="1" checked=\'checked\'', $output );
		self::assertStringContainsString( 'would arrive a little later', $output );
		self::assertStringContainsString( 'name="dewiride_analytics_settings[report_unfinished]" value="1">', $output );
	}

	/**
	 * Feeds are reported to begin with, and sign-in attempts are not.
	 */
	public function test_feeds_are_on_and_sign_ins_off_to_begin_with(): void {
		$output = $this->render( 'render_categories' );

		self::assertStringContainsString( 'name="dewiride_analytics_settings[report_feeds]" value="1" checked=\'checked\'', $output );
		self::assertStringContainsString( 'name="dewiride_analytics_settings[report_sign_in]" value="1">', $output );
	}

	/**
	 * The proxy choice shows the address WordPress sees for whoever is reading it.
	 */
	public function test_the_proxy_choice_shows_the_address_wordpress_sees(): void {
		$output = $this->render( 'render_proxy' );

		self::assertStringContainsString( 'value="automatic" checked=\'checked\'', $output );
		self::assertStringContainsString( 'value="X-Forwarded-For"', $output );
		self::assertStringContainsString( 'WordPress sees your address as 203.0.113.7.', $output );

		$this->given_request(
			array(
				'REMOTE_ADDR'          => '10.0.0.5',
				'HTTP_X_FORWARDED_FOR' => '203.0.113.7',
			)
		);

		self::assertStringContainsString( 'behind a proxy WordPress doesn&#039;t know about', $this->render( 'render_proxy' ) );
	}

	/**
	 * Settings that keep the reports from being sent, and the start of what the page says.
	 *
	 * @return array<string, array{array<string, mixed>, string}>
	 */
	public function setup_problems(): array {
		return array(
			'no key'                 => array( array(), 'Off. Save a server key' ),
			'switched off'           => array( array( 'report_server' => false ), 'Off. Tick Report visits' ),
			'pages held open'        => array( array( 'report_unfinished' => false ), 'Off. This server can\'t send a page' ),
			'an unencrypted address' => array( array( 'address' => 'http://203.0.113.10' ), 'Off. Reports carry your server key' ),
		);
	}

	/**
	 * Each setting that keeps reports from being sent says what to do.
	 *
	 * @dataProvider setup_problems
	 *
	 * @param array<string, mixed> $settings What differs from settings ready to report.
	 * @param string               $sentence The start of what the page says.
	 */
	public function test_each_setup_problem_says_what_to_do( array $settings, string $sentence ): void {
		if ( array() !== $settings ) {
			$this->given_key();
		}

		$this->options[ Settings::OPTION ] = $settings + self::as_array( $this->options[ Settings::OPTION ] );

		self::assertStringStartsWith( $sentence, $this->page()->reporting_sentence() );
	}

	/**
	 * A key that no longer opens is said to need pasting again.
	 */
	public function test_a_key_that_no_longer_opens_is_said_to(): void {
		$this->given_key();
		Functions\when( 'wp_salt' )->justReturn( 'new secret keys' );

		self::assertStringContainsString( 'can\'t be read any more', $this->page()->reporting_sentence() );
	}

	/**
	 * While the tag is kept from readers, the reports are said to be paused, and why.
	 */
	public function test_a_tag_kept_from_readers_pauses_the_reports(): void {
		$this->given_key();
		$this->options[ Tag_Check::OPTION ] = array(
			'outcome'    => Tag_Check::MISSING,
			'tool'       => '',
			'checked_at' => time(),
		);

		self::assertStringStartsWith( 'Paused while the tag doesn\'t reach your readers', $this->page()->reporting_sentence() );
	}

	/**
	 * Every state the reports can be in once set up, and the start of what the page says.
	 *
	 * @return array<string, array{array<string, mixed>, string}>
	 */
	public function delivery_states(): array {
		return array(
			'never sent'          => array( array(), 'On. Robots and other programs are reported too.' ),
			'sent'                => array(
				array(
					'sent_at' => 1,
					'send_ms' => 84,
				),
				'On. The last report was sent 5 mins ago and took 84 ms.',
			),
			'a refused key'       => array( array( 'stopped' => Reporting_Status::KEY_REFUSED ), 'Stopped. Dewiride Analytics didn\'t accept' ),
			'another website'     => array( array( 'stopped' => Reporting_Status::OTHER_WEBSITE ), 'Stopped. The server key belongs to another website' ),
			'a redirect'          => array(
				array(
					'stopped'  => Reporting_Status::REDIRECTED,
					'redirect' => 'https://analytics.example.org',
				),
				'Stopped. The address redirects to https://analytics.example.org.',
			),
			'a redirect in place' => array(
				array(
					'stopped'  => Reporting_Status::REDIRECTED,
					'redirect' => '',
				),
				'Stopped. The address answers with a redirect. Check that it',
			),
			'asked to slow down'  => array(
				array(
					'paused_until' => PHP_INT_MAX,
					'pause'        => Reporting_Status::BUSY,
				),
				'Waiting. Dewiride Analytics asked for a pause',
			),
			'not being measured'  => array(
				array(
					'paused_until' => PHP_INT_MAX,
					'pause'        => Reporting_Status::NOT_MEASURING,
				),
				'Waiting. Dewiride Analytics isn\'t counting visits',
			),
			'no answer'           => array(
				array(
					'failures' => 1,
					'failure'  => 0,
				),
				'Waiting after an error. The last report got no answer',
			),
			'an error'            => array(
				array(
					'failures' => 2,
					'failure'  => 503,
				),
				'Waiting after an error. The last report got error 503',
			),
			'another address'     => array(
				array(
					'paused_until' => PHP_INT_MAX,
					'pause'        => Reporting_Status::UNCOVERED,
				),
				'Waiting. The website in Dewiride Analytics doesn',
			),
			'a key not opening'   => array( array( 'stopped' => Reporting_Status::KEY_UNREADABLE ), 'Stopped. The server key couldn' ),
		);
	}

	/**
	 * Every state the reports can be in is said in a sentence.
	 *
	 * @dataProvider delivery_states
	 *
	 * @param array<string, mixed> $state    What differs from reports nothing has happened to.
	 * @param string               $sentence The start of what the page says.
	 */
	public function test_every_state_of_the_reports_is_said( array $state, string $sentence ): void {
		$this->given_key();
		$fresh                                     = ( new Reporting_Status( new Settings() ) )->state();
		$this->options[ Reporting_Status::OPTION ] = $state + $fresh;

		self::assertStringStartsWith( $sentence, $this->page()->reporting_sentence() );
	}

	/**
	 * Without a key the status shows only the reports' row; with one, the connection's too, with
	 * its button.
	 */
	public function test_the_connection_row_appears_with_a_key(): void {
		$without = $this->render( 'print_status_rows' );

		self::assertStringContainsString( 'Robots and other programs', $without );
		self::assertStringNotContainsString( 'Check connection', $without );

		$this->given_key();
		$with = $this->render( 'print_status_rows' );

		self::assertStringContainsString( 'Not checked yet.', $with );
		self::assertStringContainsString( 'value="' . Connection_Check::ACTION . '"', $with );
		self::assertStringContainsString( '<button>Check connection</button>', $with );
	}

	/**
	 * The section is declared with its four fields.
	 */
	public function test_the_section_and_its_fields_are_declared(): void {
		Functions\expect( 'add_settings_section' )->once();
		Functions\expect( 'add_settings_field' )->times( 4 );

		$this->page()->add_fields( 'dewiride-analytics' );
	}

	/**
	 * Saves a key, as the settings page would.
	 */
	private function given_key(): void {
		( new Server_Key() )->save( self::KEY );
		$this->options[ Settings::OPTION ] = array( 'key_ending' => Server_Key::ending_of( self::KEY ) ) + self::as_array( $this->options[ Settings::OPTION ] );
	}

	/**
	 * What a method of the page prints.
	 *
	 * @param string $method The method.
	 */
	private function render( string $method ): string {
		ob_start();
		$this->page()->$method();

		return (string) ob_get_clean();
	}


	/**
	 * A key saved some other way than through the page, which left the settings without its ending,
	 * is said to need saving again rather than said to be on.
	 */
	public function test_a_key_saved_around_the_page_is_said_to_need_saving(): void {
		( new Server_Key() )->save( self::KEY );

		self::assertStringStartsWith( 'Off. Paste the server key below again', $this->page()->reporting_sentence() );
	}

	/**
	 * The part of the page, over the options in each test.
	 */
	private function page(): Server_Settings {
		$settings   = new Settings();
		$server_key = new Server_Key();
		$status     = new Reporting_Status( $settings );
		$tag_check  = new Tag_Check( $settings );

		return new Server_Settings(
			$settings,
			$server_key,
			$status,
			new Connection_Check( $settings, $server_key, new Collector_Client( $settings, $server_key, $status ), $status, $tag_check ),
			$tag_check,
			new Visitor_Address( $settings, new Cloudflare_Ranges() )
		);
	}
}
