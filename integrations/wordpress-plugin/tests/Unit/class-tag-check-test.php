<?php
/**
 * Tests for the check of the tag visitors receive.
 *
 * The markup itself is read by WordPress's HTML API, which exists only inside WordPress, so how a
 * real page is read is tested by the smoke harness against a real installation. What is tested
 * here is everything around it: when the check runs, how it fetches, what it keeps, and what it
 * says — and, with a stand-in for the HTML API that finds one tag, how that tag is judged against
 * the page that served it.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Settings_Page;
use Dewiride\Analytics\WordPress\Tag_Check;
use Dewiride\Analytics\WordPress\Tracker_Tag;
use Mockery;
use RuntimeException;

/**
 * When the tag is checked, what is kept, and what the owner is told.
 */
final class Tag_Check_Test extends Test_Case {

	/**
	 * What a fetch that failed outright returns in these tests.
	 */
	private const FAILED = 'failed fetch';

	/**
	 * The site's home page in these tests.
	 */
	private const HOME_PAGE = 'https://example.com/';

	/**
	 * The same home page served over http.
	 */
	private const HTTP_HOME_PAGE = 'http://example.com/';

	/**
	 * An analytics address saved before the site moved to https.
	 */
	private const INSECURE_ADDRESS = 'http://analytics.example.com';

	/**
	 * Starts each test with counting on and fetches described by plain arrays.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_settings();
		Functions\when( 'is_wp_error' )->alias(
			static function ( $thing ): bool {
				return self::FAILED === $thing;
			}
		);
		Functions\when( 'wp_remote_retrieve_response_code' )->alias( array( self::class, 'status_of' ) );
		Functions\when( 'wp_remote_retrieve_header' )->alias( array( self::class, 'header_of' ) );
	}

	/**
	 * The status of a response made by this test, as WordPress reads it.
	 *
	 * @param array{response: array{code: int}} $response The response.
	 */
	public static function status_of( array $response ): int {
		return $response['response']['code'];
	}

	/**
	 * A header of a response made by this test, as WordPress reads it.
	 *
	 * @param array{headers: array<string, string>} $response The response.
	 * @param string                                $header   The header's name.
	 */
	public static function header_of( array $response, string $header ): string {
		return $response['headers'][ $header ] ?? '';
	}

	/**
	 * The daily check and the forgetting of stale outcomes are hooked on every request.
	 */
	public function test_it_hooks_the_daily_check_and_settings_changes(): void {
		$check = new Tag_Check( new Settings() );

		$check->register();

		self::assertSame( 10, has_action( Tag_Check::EVENT, array( $check, 'run' ) ) );
		self::assertSame( 10, has_action( 'add_option_' . Settings::OPTION, array( $check, 'settings_added' ) ) );
		self::assertSame( 10, has_action( 'update_option_' . Settings::OPTION, array( $check, 'settings_changed' ) ) );
	}

	/**
	 * The button and the scheduling are hooked in the dashboard.
	 */
	public function test_it_hooks_the_button_and_scheduling_in_the_dashboard(): void {
		$check = new Tag_Check( new Settings() );

		$check->register_admin();

		self::assertSame( 10, has_action( 'admin_post_' . Tag_Check::ACTION, array( $check, 'handle_request' ) ) );
		self::assertSame( 10, has_action( 'admin_init', array( Tag_Check::class, 'schedule' ) ) );
	}

	/**
	 * The daily check is scheduled once.
	 */
	public function test_the_daily_check_is_scheduled_when_it_is_not_yet(): void {
		Functions\when( 'wp_next_scheduled' )->justReturn( false );
		Functions\expect( 'wp_schedule_event' )->once()->with( Mockery::type( 'int' ), 'daily', Tag_Check::EVENT );

		Tag_Check::schedule();
	}

	/**
	 * A check already scheduled is left as it is.
	 */
	public function test_a_scheduled_check_is_not_scheduled_again(): void {
		Functions\when( 'wp_next_scheduled' )->justReturn( 1700000000 );
		Functions\expect( 'wp_schedule_event' )->never();

		Tag_Check::schedule();
	}

	/**
	 * Unscheduling clears the event.
	 */
	public function test_unscheduling_clears_the_event(): void {
		Functions\expect( 'wp_clear_scheduled_hook' )->once()->with( Tag_Check::EVENT );

		Tag_Check::unschedule();
	}

	/**
	 * Nothing is fetched until counting is on.
	 */
	public function test_nothing_is_checked_until_counting_is_on(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'wp_remote_get' )->never();
		Functions\expect( 'update_option' )->never();

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * The home page is fetched anonymously, as the plugin, without following redirects blindly.
	 */
	public function test_the_home_page_is_fetched_as_the_plugin(): void {
		Functions\when( 'home_url' )->justReturn( self::HOME_PAGE );
		Functions\when( 'update_option' )->justReturn( true );
		Functions\expect( 'wp_remote_get' )
			->once()
			->with(
				self::HOME_PAGE,
				array(
					'timeout'     => 10,
					'redirection' => 0,
					'user-agent'  => 'DewirideAnalytics/' . DEWIRIDE_ANALYTICS_VERSION,
				)
			)
			->andReturn( self::FAILED );

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * A fetch that fails is kept as unreachable, with the time, and nothing from the page.
	 */
	public function test_a_failed_fetch_is_kept_as_unreachable(): void {
		Functions\when( 'wp_remote_get' )->justReturn( self::FAILED );
		Functions\expect( 'update_option' )
			->once()
			->with(
				Tag_Check::OPTION,
				Mockery::on(
					static function ( $kept ): bool {
						return is_array( $kept )
							&& array( 'outcome', 'tool', 'checked_at' ) === array_keys( $kept )
							&& Tag_Check::UNREACHABLE === $kept['outcome']
							&& '' === $kept['tool']
							&& is_int( $kept['checked_at'] );
					}
				),
				false
			);

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * A home page that answers with an error is kept as unreachable.
	 */
	public function test_an_error_page_is_kept_as_unreachable(): void {
		Functions\when( 'wp_remote_get' )->justReturn( self::response( 503 ) );
		Functions\expect( 'update_option' )->once()->with( Tag_Check::OPTION, Mockery::subset( array( 'outcome' => Tag_Check::UNREACHABLE ) ), false );

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * A redirect to another host is not followed.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_redirect_to_another_host_is_not_followed(): void {
		Mockery::mock( 'alias:WP_Http' )->shouldReceive( 'make_absolute_url' )->andReturnUsing(
			static function ( string $location ): string {
				return $location;
			}
		);
		Functions\when( 'home_url' )->justReturn( self::HOME_PAGE );
		Functions\expect( 'wp_remote_get' )->once()->andReturn( self::response( 302, 'https://elsewhere.example/' ) );
		Functions\expect( 'update_option' )->once()->with( Tag_Check::OPTION, Mockery::subset( array( 'outcome' => Tag_Check::UNREACHABLE ) ), false );

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * Redirects within the site are followed a few times and no more.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_redirects_within_the_site_are_followed_a_few_times(): void {
		Mockery::mock( 'alias:WP_Http' )->shouldReceive( 'make_absolute_url' )->andReturnUsing(
			static function ( string $location ): string {
				return $location;
			}
		);
		Functions\when( 'home_url' )->justReturn( 'http://example.com/' );
		Functions\expect( 'wp_remote_get' )->times( 4 )->andReturn( self::response( 301, self::HOME_PAGE ) );
		Functions\expect( 'update_option' )->once()->with( Tag_Check::OPTION, Mockery::subset( array( 'outcome' => Tag_Check::UNREACHABLE ) ), false );

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * A kept outcome is read back as it was stored.
	 */
	public function test_the_last_outcome_is_read_back(): void {
		Functions\when( 'get_option' )->justReturn(
			array(
				'outcome'    => Tag_Check::ALTERED,
				'tool'       => Tag_Check::WP_ROCKET,
				'checked_at' => 1700000000,
			)
		);

		self::assertSame(
			array(
				'outcome'    => Tag_Check::ALTERED,
				'tool'       => Tag_Check::WP_ROCKET,
				'checked_at' => 1700000000,
			),
			( new Tag_Check( new Settings() ) )->last_result()
		);
	}

	/**
	 * Kept outcomes that cannot be read.
	 *
	 * @return array<string, array{mixed}>
	 */
	public function unreadable_outcomes(): array {
		return array(
			'nothing kept'       => array( false ),
			'not an array'       => array( 'intact' ),
			'an unknown outcome' => array(
				array(
					'outcome'    => 'splendid',
					'checked_at' => 1700000000,
				),
			),
			'no time'            => array( array( 'outcome' => Tag_Check::INTACT ) ),
		);
	}

	/**
	 * A kept outcome that cannot be read reads as never checked.
	 *
	 * @dataProvider unreadable_outcomes
	 *
	 * @param mixed $kept What the option holds.
	 */
	public function test_an_unreadable_outcome_reads_as_never_checked( $kept ): void {
		Functions\when( 'get_option' )->justReturn( $kept );

		self::assertNull( ( new Tag_Check( new Settings() ) )->last_result() );
	}

	/**
	 * Saving the settings without moving the address or Website ID keeps the outcome.
	 */
	public function test_saving_the_same_connection_keeps_the_outcome(): void {
		Functions\expect( 'delete_option' )->never();
		Functions\expect( 'wp_schedule_single_event' )->never();

		( new Tag_Check( new Settings() ) )->settings_changed(
			array(
				'address'       => self::ADDRESS,
				'site_id'       => self::SITE_ID,
				'counted_roles' => array(),
			),
			array(
				'address'       => self::ADDRESS,
				'site_id'       => self::SITE_ID,
				'counted_roles' => array( 'editor' ),
			)
		);
	}

	/**
	 * The first save, which WordPress makes by adding the option, checks soon as well.
	 */
	public function test_the_first_save_checks_soon(): void {
		Functions\expect( 'delete_option' )->once()->with( Tag_Check::OPTION );
		Functions\expect( 'wp_schedule_single_event' )->once()->with( Mockery::type( 'int' ), Tag_Check::EVENT );

		( new Tag_Check( new Settings() ) )->settings_added();
	}

	/**
	 * The empty settings activation adds name nothing to check.
	 */
	public function test_adding_the_empty_settings_checks_nothing(): void {
		$this->given_settings( '', '' );
		Functions\expect( 'delete_option' )->never();
		Functions\expect( 'wp_schedule_single_event' )->never();

		( new Tag_Check( new Settings() ) )->settings_added();
	}

	/**
	 * A new address or Website ID forgets the outcome and checks again soon.
	 */
	public function test_a_new_connection_forgets_the_outcome_and_checks_again(): void {
		Functions\expect( 'delete_option' )->once()->with( Tag_Check::OPTION );
		Functions\expect( 'wp_schedule_single_event' )->once()->with( Mockery::type( 'int' ), Tag_Check::EVENT );

		( new Tag_Check( new Settings() ) )->settings_changed(
			array(
				'address' => self::ADDRESS,
				'site_id' => self::SITE_ID,
			),
			array(
				'address' => 'https://analytics.example.org',
				'site_id' => self::SITE_ID,
			)
		);
	}

	/**
	 * The button refuses anybody who cannot manage the site's options.
	 */
	public function test_the_button_refuses_somebody_without_the_capability(): void {
		Functions\when( 'current_user_can' )->justReturn( false );
		Functions\expect( 'wp_die' )->once()->andThrow( new RuntimeException( 'refused' ) );
		Functions\expect( 'check_admin_referer' )->never();
		Functions\expect( 'wp_remote_get' )->never();
		$this->expectExceptionMessage( 'refused' );

		( new Tag_Check( new Settings() ) )->handle_request();
	}

	/**
	 * The button checks its nonce, runs the check and returns to the settings page.
	 */
	public function test_the_button_runs_the_check_and_returns_to_the_settings_page(): void {
		Functions\when( 'current_user_can' )->justReturn( true );
		Functions\when( 'admin_url' )->alias(
			static function ( string $path ): string {
				return 'https://example.com/wp-admin/' . $path;
			}
		);
		Functions\when( 'update_option' )->justReturn( true );
		Functions\expect( 'check_admin_referer' )->once()->with( Tag_Check::ACTION );
		Functions\expect( 'wp_remote_get' )->once()->andReturn( self::FAILED );
		Functions\expect( 'wp_safe_redirect' )
			->once()
			->with( 'https://example.com/wp-admin/options-general.php?page=' . Settings_Page::SLUG )
			->andThrow( new RuntimeException( 'redirected' ) );
		$this->expectExceptionMessage( 'redirected' );

		( new Tag_Check( new Settings() ) )->handle_request();
	}

	/**
	 * Every outcome the check can reach, and the start of what the owner is told.
	 *
	 * @return array<string, array{string, string, string}>
	 */
	public function outcomes(): array {
		return array(
			'intact'                     => array( Tag_Check::INTACT, '', 'The tag is in place on your home page.' ),
			'missing'                    => array( Tag_Check::MISSING, '', 'Your home page doesn\'t carry the tag.' ),
			'duplicate'                  => array( Tag_Check::DUPLICATE, '', 'Your home page loads the tag more than once.' ),
			'unreachable'                => array( Tag_Check::UNREACHABLE, '', 'Your site couldn\'t open its own home page' ),
			'blocked as insecure'        => array( Tag_Check::INSECURE, '', 'Your home page is served over https, but the tag loads over http' ),
			'altered by WP Rocket'       => array( Tag_Check::ALTERED, Tag_Check::WP_ROCKET, 'WP Rocket is delaying the tag' ),
			'altered by LiteSpeed Cache' => array( Tag_Check::ALTERED, Tag_Check::LITESPEED_CACHE, 'LiteSpeed Cache is delaying the tag' ),
			'held by a consent tool'     => array( Tag_Check::ALTERED, Tag_Check::CONSENT_TOOL, 'A consent tool is holding the tag back' ),
			'combined or moved'          => array( Tag_Check::ALTERED, Tag_Check::OPTIMISER, 'An optimisation plugin has combined the tag' ),
			'altered by something else'  => array( Tag_Check::ALTERED, '', 'Something on your site changes the tag' ),
			'altered by a tool unknown'  => array( Tag_Check::ALTERED, 'another-tool', 'Something on your site changes the tag' ),
		);
	}

	/**
	 * Each outcome is told in its own words.
	 *
	 * @dataProvider outcomes
	 *
	 * @param string $outcome The outcome.
	 * @param string $tool    The tool that caused it.
	 * @param string $opening How the sentence begins.
	 */
	public function test_each_outcome_is_told_in_plain_words( string $outcome, string $tool, string $opening ): void {
		self::assertStringStartsWith(
			$opening,
			Tag_Check::message(
				array(
					'outcome' => $outcome,
					'tool'    => $tool,
				)
			)
		);
	}

	/**
	 * An outcome of the tag loading over http on a secure page is kept and read back.
	 */
	public function test_an_insecure_outcome_is_read_back(): void {
		Functions\when( 'get_option' )->justReturn(
			array(
				'outcome'    => Tag_Check::INSECURE,
				'tool'       => '',
				'checked_at' => 1700000000,
			)
		);

		self::assertSame( Tag_Check::INSECURE, ( new Tag_Check( new Settings() ) )->last_result()['outcome'] ?? null );
	}

	/**
	 * A tag printed as it should be, loading over http on a page served over https, is told apart
	 * from one that works, since browsers refuse it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_tag_loading_over_http_on_a_secure_page_is_insecure(): void {
		self::given_pages_carry_the_tag_from( self::INSECURE_ADDRESS );

		self::assertSame(
			array(
				'outcome' => Tag_Check::INSECURE,
				'tool'    => '',
			),
			Tag_Check::inspect( '<html></html>', self::INSECURE_ADDRESS, self::SITE_ID, self::HOME_PAGE )
		);
	}

	/**
	 * The same tag on a page served over http loads, and is intact.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_tag_loading_over_http_on_a_page_served_over_http_is_intact(): void {
		self::given_pages_carry_the_tag_from( self::INSECURE_ADDRESS );

		self::assertSame(
			array(
				'outcome' => Tag_Check::INTACT,
				'tool'    => '',
			),
			Tag_Check::inspect( '<html></html>', self::INSECURE_ADDRESS, self::SITE_ID, self::HTTP_HOME_PAGE )
		);
	}

	/**
	 * The page is judged by the address that served it: a site WordPress still names with http,
	 * redirecting to https, refuses a tag loading over http.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_the_page_is_judged_by_the_address_that_served_it(): void {
		self::given_pages_carry_the_tag_from( self::INSECURE_ADDRESS );
		Mockery::mock( 'alias:WP_Http' )->shouldReceive( 'make_absolute_url' )->andReturnUsing(
			static function ( string $location ): string {
				return $location;
			}
		);
		$this->given_settings( self::INSECURE_ADDRESS );
		Functions\when( 'home_url' )->justReturn( self::HTTP_HOME_PAGE );
		Functions\when( 'wp_remote_retrieve_body' )->justReturn( '<html></html>' );
		Functions\when( 'wp_remote_get' )->alias(
			static function ( string $url ): array {
				return self::HTTP_HOME_PAGE === $url ? self::response( 301, self::HOME_PAGE ) : self::response( 200 );
			}
		);
		Functions\expect( 'update_option' )->once()->with( Tag_Check::OPTION, Mockery::subset( array( 'outcome' => Tag_Check::INSECURE ) ), false );

		( new Tag_Check( new Settings() ) )->run();
	}

	/**
	 * Makes every page read hold one tag, the plugin's, printed for the given address.
	 *
	 * WordPress's HTML API exists only inside WordPress, so its processor is replaced by one that
	 * stands on that tag and then finds no more. Only for a test run in a process of its own,
	 * which the replacement class outlives.
	 *
	 * @param string $address The analytics address the tag loads from and sends to.
	 */
	private static function given_pages_carry_the_tag_from( string $address ): void {
		$attributes = array(
			'id'             => Tracker_Tag::ELEMENT_ID,
			'src'            => $address . '/dw.js',
			'data-site'      => self::SITE_ID,
			'data-collector' => $address . '/collect',
		);
		$processor  = Mockery::mock( 'overload:WP_HTML_Tag_Processor' );

		$processor->shouldReceive( 'next_tag' )->andReturn( true, false );
		$processor->shouldReceive( 'get_attribute_names_with_prefix' )->with( 'data-' )->andReturn( array( 'data-site', 'data-collector' ) );
		$processor->shouldReceive( 'get_attribute' )->andReturnUsing(
			static function ( string $name ) use ( $attributes ): ?string {
				return $attributes[ $name ] ?? null;
			}
		);
	}

	/**
	 * A response with the given status, and where it redirects.
	 *
	 * @param int    $status   The status code.
	 * @param string $location Where it redirects, if anywhere.
	 * @return array{response: array{code: int}, headers: array<string, string>, body: string}
	 */
	private static function response( int $status, string $location = '' ): array {
		return array(
			'response' => array( 'code' => $status ),
			'headers'  => '' === $location ? array() : array( 'location' => $location ),
			'body'     => '',
		);
	}
}
