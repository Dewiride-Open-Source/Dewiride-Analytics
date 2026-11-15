<?php
/**
 * Tests for the plugin's settings page.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Settings_Page;
use Dewiride\Analytics\WordPress\Tag_Check;

/**
 * The page's warnings, its status, the Settings link and the reminder on the Plugins screen.
 */
final class Settings_Page_Test extends Test_Case {

	/**
	 * The plugin's basename.
	 */
	private const BASENAME = 'dewiride-analytics/dewiride-analytics.php';

	/**
	 * The page's title, which is also its name under Settings.
	 */
	private const TITLE = 'Dewiride Analytics';

	/**
	 * An analytics address saved before the site moved to https.
	 */
	private const INSECURE_ADDRESS = 'http://analytics.example.com';

	/**
	 * What somebody who may not change the address is told on a single site.
	 */
	private const SITE_LOCK = 'Only someone allowed to add code to this site can change the address. Ask whoever looks after your site to change it for you.';

	/**
	 * What WordPress's own options hold in these tests, beside the plugin's.
	 *
	 * @var array<string, mixed>
	 */
	private $options = array();

	/**
	 * Starts each test with counting on, pretty permalinks and registration closed.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_roles(
			array(
				'administrator' => 'Administrator',
				'subscriber'    => 'Subscriber',
			)
		);
		$this->given_options( array() );
		Functions\when( 'current_user_can' )->justReturn( true );
		Functions\when( 'translate_user_role' )->returnArg();
		Functions\when( 'admin_url' )->alias(
			static function ( string $path = '' ): string {
				return 'https://example.com/wp-admin/' . $path;
			}
		);
	}

	/**
	 * The page, its fields, the reminder and the link are hooked.
	 */
	public function test_it_hooks_the_page_the_reminder_and_the_link(): void {
		$page = $this->page();

		$page->register();

		self::assertSame( 10, has_action( 'admin_menu', array( $page, 'add_page' ) ) );
		self::assertSame( 10, has_action( 'admin_init', array( $page, 'add_fields' ) ) );
		self::assertSame( 10, has_action( 'admin_notices', array( $page, 'print_setup_notice' ) ) );
		self::assertSame( 10, has_filter( 'plugin_action_links_' . self::BASENAME, array( $page, 'action_links' ) ) );
	}

	/**
	 * The page sits under Settings, for those who manage the site's options.
	 */
	public function test_the_page_is_added_under_settings(): void {
		$page = $this->page();

		Functions\expect( 'add_options_page' )
			->once()
			->with( self::TITLE, self::TITLE, 'manage_options', Settings_Page::SLUG, array( $page, 'render' ) );

		$page->add_page();
	}

	/**
	 * No warning applies to a site with a secure address, pretty permalinks and closed
	 * registration.
	 */
	public function test_no_warning_applies_to_an_ordinary_site(): void {
		self::assertSame(
			array(
				'insecure_address'  => false,
				'plain_permalinks'  => false,
				'open_registration' => '',
			),
			$this->page()->warnings()
		);
	}

	/**
	 * An http address on a site served over https is warned about, since browsers block it.
	 */
	public function test_an_http_address_on_an_https_site_is_warned_about(): void {
		$this->given_options( array(), array(), self::INSECURE_ADDRESS );

		self::assertTrue( $this->page()->warnings()['insecure_address'] );
	}

	/**
	 * An http address on a site served over http loads, and needs no warning.
	 */
	public function test_an_http_address_on_an_http_site_needs_no_warning(): void {
		Functions\when( 'home_url' )->justReturn( 'http://example.com' );
		$this->given_options( array(), array(), self::INSECURE_ADDRESS );

		self::assertFalse( $this->page()->warnings()['insecure_address'] );
	}

	/**
	 * Plain permalinks are warned about.
	 */
	public function test_plain_permalinks_are_warned_about(): void {
		$this->given_options( array( 'permalink_structure' => '' ) );

		self::assertTrue( $this->page()->warnings()['plain_permalinks'] );
	}

	/**
	 * Open registration is warned about while the role it gives is not counted.
	 */
	public function test_open_registration_is_warned_about_when_its_role_is_left_out(): void {
		$this->given_options(
			array(
				'users_can_register' => '1',
				'default_role'       => 'subscriber',
			)
		);

		self::assertSame( 'subscriber', $this->page()->warnings()['open_registration'] );
	}

	/**
	 * Open registration needs no warning once its role is counted.
	 */
	public function test_open_registration_is_fine_when_its_role_is_counted(): void {
		$this->given_options(
			array(
				'users_can_register' => '1',
				'default_role'       => 'subscriber',
			),
			array( 'subscriber' )
		);

		self::assertSame( '', $this->page()->warnings()['open_registration'] );
	}

	/**
	 * The Settings link comes first among the plugin's links.
	 */
	public function test_the_settings_link_comes_first(): void {
		$links = $this->page()->action_links( array( 'deactivate' => '<a href="#">Deactivate</a>' ) );

		self::assertIsArray( $links );
		self::assertSame( array( 'settings', 'deactivate' ), array_keys( $links ) );
		self::assertSame( '<a href="https://example.com/wp-admin/options-general.php?page=dewiride-analytics">Settings</a>', $links['settings'] );
	}

	/**
	 * Links of an unexpected shape are handed back untouched.
	 */
	public function test_links_of_another_shape_are_left_alone(): void {
		self::assertSame( 'links', $this->page()->action_links( 'links' ) );
	}

	/**
	 * The Connect section says where its two values come from and what has to exist first, with a
	 * link to the hosted service.
	 */
	public function test_the_connect_section_says_what_is_needed_first(): void {
		Functions\when( 'wp_kses' )->returnArg();
		$this->expectOutputString( '<p>Copy both from the WordPress steps in Dewiride Analytics. You need an account at <a href="https://analytics.dewiride.com/">analytics.dewiride.com</a>, or an installation of Dewiride Analytics you run yourself.</p>' );

		$this->page()->describe_connect();
	}

	/**
	 * The reminder shows on the Plugins screen while the setup is unfinished.
	 */
	public function test_the_reminder_shows_on_the_plugins_screen_until_setup_is_done(): void {
		$this->given_options( array(), array(), '' );
		$this->given_the_plugins_screen();
		$this->expectOutputRegex( '~^<div class="notice notice-info is-dismissible"><p>Dewiride Analytics is almost ready\. <a href="https://example\.com/wp-admin/options-general\.php\?page=dewiride-analytics">~' );

		$this->page()->print_setup_notice();
	}

	/**
	 * What is missing from the setup, and what the reminder asks to be added.
	 *
	 * @return array<string, array{string, string, string}>
	 */
	public function missing_settings(): array {
		return array(
			'both'           => array( '', '', 'Add your address and Website ID' ),
			'the address'    => array( '', self::SITE_ID, 'Add your address' ),
			'the Website ID' => array( self::ADDRESS, '', 'Add your Website ID' ),
		);
	}

	/**
	 * The reminder asks for exactly what is missing.
	 *
	 * @dataProvider missing_settings
	 *
	 * @param string $address The saved address.
	 * @param string $site_id The saved Website ID.
	 * @param string $request What the reminder asks to be added.
	 */
	public function test_the_reminder_asks_for_what_is_missing( string $address, string $site_id, string $request ): void {
		$this->given_options( array(), array(), $address, $site_id );
		$this->given_the_plugins_screen();
		$this->expectOutputRegex( '~">' . preg_quote( $request, '~' ) . '</a> to start counting visitors\.</p></div>$~' );

		$this->page()->print_setup_notice();
	}

	/**
	 * Somebody who may not change the address is not asked to add it, since they cannot.
	 */
	public function test_the_reminder_leaves_a_missing_address_to_somebody_allowed_to_change_it(): void {
		$this->given_no_permission_to_add_code();
		$this->given_options( array(), array(), '', '' );
		$this->given_the_plugins_screen();
		$this->expectOutputString( '' );

		$this->page()->print_setup_notice();
	}

	/**
	 * Once the address is saved, the Website ID is all that is left, and anybody who manages the
	 * site's options is asked for it.
	 */
	public function test_the_reminder_asks_anybody_who_manages_the_site_for_a_missing_website_id(): void {
		$this->given_no_permission_to_add_code();
		$this->given_options( array(), array(), self::ADDRESS, '' );
		$this->given_the_plugins_screen();
		$this->expectOutputRegex( '~">Add your Website ID</a> to start counting visitors\.</p></div>$~' );

		$this->page()->print_setup_notice();
	}

	/**
	 * Places and states where the reminder stays away.
	 *
	 * @return array<string, array{string, bool, string}>
	 */
	public function reminder_absences(): array {
		return array(
			'another screen'                => array( 'dashboard', true, '' ),
			'somebody who cannot finish it' => array( 'plugins', false, '' ),
			'a finished setup'              => array( 'plugins', true, self::ADDRESS ),
		);
	}

	/**
	 * The reminder stays away from other screens, from people who cannot act on it, and once the
	 * setup is finished.
	 *
	 * @dataProvider reminder_absences
	 *
	 * @param string $screen  The current screen.
	 * @param bool   $capable Whether the person can manage options.
	 * @param string $address The saved address.
	 */
	public function test_the_reminder_stays_away_where_it_does_not_belong( string $screen, bool $capable, string $address ): void {
		$this->given_options( array(), array(), $address );
		Functions\when( 'get_current_screen' )->justReturn( (object) array( 'id' => $screen ) );
		Functions\when( 'current_user_can' )->justReturn( $capable );
		$this->expectOutputString( '' );

		$this->page()->print_setup_notice();
	}

	/**
	 * The status says counting is on, what the last check found, and offers the button.
	 */
	public function test_the_page_shows_the_status_and_the_last_check(): void {
		$this->given_page_functions();
		$this->given_options(
			array(
				Tag_Check::OPTION => array(
					'outcome'    => Tag_Check::INTACT,
					'tool'       => '',
					'checked_at' => 1700000000,
				),
			)
		);

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( '<h1>Dewiride Analytics</h1>', $page );
		self::assertStringContainsString( 'On. The tag is added to your pages for every visitor who is counted.', $page );
		self::assertStringContainsString( 'The tag is in place on your home page.', $page );
		self::assertStringContainsString( 'Checked 3 hours ago.', $page );
		self::assertStringContainsString( '<input type="hidden" name="action" value="' . Tag_Check::ACTION . '">', $page );
		self::assertStringContainsString( 'Dewiride Analytics ' . DEWIRIDE_ANALYTICS_VERSION, $page );
		self::assertStringNotContainsString( 'notice-warning', $page );
	}

	/**
	 * Settings that leave counting off, and what the status asks for.
	 *
	 * @return array<string, array{string, string, string}>
	 */
	public function unfinished_settings(): array {
		return array(
			'both'           => array( '', '', 'Off. Add your address and Website ID below to start counting.' ),
			'the address'    => array( '', self::SITE_ID, 'Off. Add your address below to start counting.' ),
			'the Website ID' => array( self::ADDRESS, '', 'Off. Add your Website ID below to start counting.' ),
		);
	}

	/**
	 * Before setup the status says counting is off, names what is missing, and offers no check.
	 *
	 * @dataProvider unfinished_settings
	 *
	 * @param string $address The saved address.
	 * @param string $site_id The saved Website ID.
	 * @param string $status  What the status says.
	 */
	public function test_the_page_shows_counting_off_before_setup( string $address, string $site_id, string $status ): void {
		$this->given_page_functions();
		$this->given_options( array(), array(), $address, $site_id );

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( '<td>' . $status . '</td>', $page );
		self::assertStringNotContainsString( Tag_Check::ACTION, $page );
	}

	/**
	 * Somebody who may not add the missing address is told who can, rather than asked to add it.
	 */
	public function test_counting_off_sends_somebody_who_cannot_add_the_address_to_who_can(): void {
		$this->given_page_functions();
		$this->given_no_permission_to_add_code();
		$this->given_options( array(), array(), '', '' );

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( '<td>Off. ' . self::SITE_LOCK . '</td>', $page );
		self::assertStringNotContainsString( 'Add your address', $page );
	}

	/**
	 * The page warns about plain permalinks and open registration, naming the role to tick.
	 */
	public function test_the_page_shows_the_warnings_that_apply(): void {
		$this->given_page_functions();
		$this->given_options(
			array(
				'permalink_structure' => '',
				'users_can_register'  => '1',
				'default_role'        => 'subscriber',
			)
		);

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( 'every visit would be recorded as your home page', $page );
		self::assertStringContainsString( 'href="https://example.com/wp-admin/options-permalink.php"', $page );
		self::assertStringContainsString( 'tick <strong>Subscriber</strong> under Roles to count', $page );
	}

	/**
	 * An http address on a site served over https is warned about, with what to do.
	 */
	public function test_the_page_says_how_to_unblock_an_http_address(): void {
		$this->given_page_functions();
		$this->given_options( array(), array(), self::INSECURE_ADDRESS );

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( '<div class="notice notice-warning inline"><p>Your site is served over https, but the address starts with http://, so browsers block the tag. Change the address to start with https:// and save.</p></div>', $page );
	}

	/**
	 * Somebody who may not change the address is told who can, instead of to change it.
	 */
	public function test_the_warning_sends_somebody_who_cannot_change_the_address_to_who_can(): void {
		$this->given_page_functions();
		$this->given_no_permission_to_add_code();
		$this->given_options( array(), array(), self::INSECURE_ADDRESS );

		ob_start();
		$this->page()->render();
		$page = (string) ob_get_clean();

		self::assertStringContainsString( 'so browsers block the tag. ' . self::SITE_LOCK . '</p></div>', $page );
		self::assertStringNotContainsString( 'Change the address to start with https://', $page );
	}

	/**
	 * Somebody allowed to add code to the site gets an address field they can change.
	 */
	public function test_the_address_can_be_changed_by_somebody_allowed_to_add_code(): void {
		ob_start();
		$this->page()->render_address();
		$field = (string) ob_get_clean();

		self::assertStringContainsString( 'value="' . self::ADDRESS . '"', $field );
		self::assertStringNotContainsString( 'readonly', $field );
		self::assertStringNotContainsString( 'description', $field );
	}

	/**
	 * Anybody else gets it read-only, still sending the saved address back, with who can change
	 * it beside it.
	 */
	public function test_the_address_is_read_only_for_somebody_not_allowed_to_add_code(): void {
		$this->given_no_permission_to_add_code();

		ob_start();
		$this->page()->render_address();
		$field = (string) ob_get_clean();

		self::assertSame(
			'<input type="url" id="dewiride-analytics-address" name="' . Settings::OPTION . '[address]" value="' . self::ADDRESS . '" class="regular-text code" placeholder="https://analytics.example.com" autocomplete="off" spellcheck="false" readonly aria-describedby="dewiride-analytics-address-note">'
				. '<p class="description" id="dewiride-analytics-address-note">' . self::SITE_LOCK . '</p>',
			$field
		);
	}

	/**
	 * On a network the note names a network administrator.
	 */
	public function test_on_a_network_the_note_names_a_network_administrator(): void {
		$this->given_no_permission_to_add_code();
		Functions\when( 'is_multisite' )->justReturn( true );
		Functions\when( 'is_super_admin' )->justReturn( false );

		ob_start();
		$this->page()->render_address();
		$field = (string) ob_get_clean();

		self::assertStringContainsString( '>Only a network administrator can change the address. Ask one to change it for you.</p>', $field );
	}

	/**
	 * The role checkboxes list every role and tick the counted ones.
	 */
	public function test_the_role_checkboxes_tick_the_counted_roles(): void {
		Functions\when( 'checked' )->alias(
			static function ( $checked, $current ): string {
				return $checked === $current ? ' checked=\'checked\'' : '';
			}
		);
		$this->given_options( array(), array( 'subscriber' ) );

		ob_start();
		$this->page()->render_counted_roles();
		$fields = (string) ob_get_clean();

		self::assertStringContainsString( 'value="administrator"> Administrator', $fields );
		self::assertStringContainsString( 'value="subscriber" checked=\'checked\'> Subscriber', $fields );
	}

	/**
	 * The page over the saved settings.
	 */
	private function page(): Settings_Page {
		$settings = new Settings();

		return new Settings_Page( $settings, new Tag_Check( $settings ), self::BASENAME );
	}

	/**
	 * Makes WordPress's options and the plugin's settings read as given.
	 *
	 * @param array<string, mixed> $options       WordPress's options that differ from an ordinary site.
	 * @param string[]             $counted_roles The roles whose signed-in members are counted.
	 * @param string               $address       The saved analytics address.
	 * @param string               $site_id       The saved Website ID.
	 */
	private function given_options( array $options, array $counted_roles = array(), string $address = self::ADDRESS, string $site_id = self::SITE_ID ): void {
		$this->options = $options + array(
			'permalink_structure' => '/%postname%/',
			'users_can_register'  => '0',
			'default_role'        => 'subscriber',
			Settings::OPTION      => array(
				'address'       => $address,
				'site_id'       => $site_id,
				'counted_roles' => $counted_roles,
			),
		);

		Functions\when( 'get_option' )->alias(
			function ( string $option ) {
				return $this->options[ $option ] ?? false;
			}
		);
	}

	/**
	 * Makes the person reading the page one who manages the site's options without being allowed
	 * to add code to it.
	 */
	private function given_no_permission_to_add_code(): void {
		Functions\when( 'current_user_can' )->alias(
			static function ( string $capability ): bool {
				return 'unfiltered_html' !== $capability;
			}
		);
	}

	/**
	 * Makes the screen being shown the Plugins screen, where the reminder belongs.
	 */
	private function given_the_plugins_screen(): void {
		Functions\when( 'get_current_screen' )->justReturn( (object) array( 'id' => 'plugins' ) );
		Functions\when( 'wp_kses' )->returnArg();
	}

	/**
	 * Stubs what the page prints through WordPress's own Settings API.
	 */
	private function given_page_functions(): void {
		Functions\when( 'get_admin_page_title' )->justReturn( self::TITLE );
		Functions\when( 'settings_fields' )->justReturn();
		Functions\when( 'do_settings_sections' )->justReturn();
		Functions\when( 'submit_button' )->justReturn();
		Functions\when( 'wp_nonce_field' )->justReturn();
		Functions\when( 'wp_kses' )->returnArg();
		Functions\when( 'human_time_diff' )->justReturn( '3 hours' );
	}
}
