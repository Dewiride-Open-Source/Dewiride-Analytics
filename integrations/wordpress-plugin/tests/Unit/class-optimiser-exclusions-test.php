<?php
/**
 * Tests for the exclusions handed to optimisation plugins.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Dewiride\Analytics\WordPress\Optimiser_Exclusions;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tracker_Tag;

/**
 * Each optimiser gets the beacon in the form it matches against, and keeps what it had.
 */
final class Optimiser_Exclusions_Test extends Test_Case {

	/**
	 * Where the beacon is loaded from in these tests.
	 */
	private const SCRIPT = 'https://example.com/analytics/dw.js';

	/**
	 * Starts each test with an installation served under a path on another host.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_settings( 'https://example.com/analytics' );
	}

	/**
	 * Every filter, with the callback it is given.
	 *
	 * @return array<string, array{string, string}>
	 */
	public function filters(): array {
		return array(
			'WP Rocket delayed JavaScript'       => array( 'rocket_delay_js_exclusions', 'exclude_address' ),
			'WP Rocket deferred JavaScript'      => array( 'rocket_exclude_defer_js', 'exclude_address' ),
			'WP Rocket scripts from elsewhere'   => array( 'rocket_minify_excluded_external_js', 'exclude_address' ),
			'WP Rocket scripts on this host'     => array( 'rocket_exclude_js', 'exclude_path' ),
			'LiteSpeed Cache minify and combine' => array( 'litespeed_optimize_js_excludes', 'exclude_address' ),
			'LiteSpeed Cache deferred'           => array( 'litespeed_optm_js_defer_exc', 'exclude_address' ),
			'LiteSpeed Cache guest mode'         => array( 'litespeed_optm_gm_js_exc', 'exclude_address' ),
			'Perfmatters delayed JavaScript'     => array( 'perfmatters_delay_js_exclusions', 'exclude_address' ),
			'Perfmatters deferred JavaScript'    => array( 'perfmatters_defer_js_exclusions', 'exclude_address' ),
			'Breeze delayed JavaScript'          => array( 'default_scripts_gnore_from_delay', 'exclude_address' ),
			'SiteGround minify'                  => array( 'sgo_js_minify_exclude', 'exclude_handle' ),
			'SiteGround combine by handle'       => array( 'sgo_javascript_combine_exclude', 'exclude_handle' ),
			'SiteGround combine by id'           => array( 'sgo_javascript_combine_exclude_ids', 'exclude_element_id' ),
			'SiteGround asynchronous loading'    => array( 'sgo_js_async_exclude', 'exclude_handle' ),
			'Autoptimize'                        => array( 'autoptimize_filter_js_exclude', 'exclude_from_autoptimize' ),
		);
	}

	/**
	 * Each optimiser's filter is hooked to the callback for its kind of list.
	 *
	 * @dataProvider filters
	 *
	 * @param string $hook     The optimiser's filter.
	 * @param string $callback The method that answers it.
	 */
	public function test_each_filter_is_hooked( string $hook, string $callback ): void {
		$exclusions = $this->exclusions();

		$exclusions->register();

		self::assertSame( 10, has_filter( $hook, array( $exclusions, $callback ) ) );
	}

	/**
	 * The beacon's address is added after what other plugins put in the list.
	 */
	public function test_the_address_is_added_to_the_list(): void {
		self::assertSame(
			array( 'jquery.min.js', '/wp-includes/js/', self::SCRIPT ),
			$this->exclusions()->exclude_address( array( 'jquery.min.js', '/wp-includes/js/' ) )
		);
	}

	/**
	 * An address already in the list is not added twice.
	 */
	public function test_the_address_is_not_added_twice(): void {
		self::assertSame( array( self::SCRIPT ), $this->exclusions()->exclude_address( array( self::SCRIPT ) ) );
	}

	/**
	 * The path is added where entries are matched against the path of a file on this host.
	 */
	public function test_the_path_is_added_to_the_list(): void {
		self::assertSame( array( '/analytics/dw.js' ), $this->exclusions()->exclude_path( array() ) );
	}

	/**
	 * The handle and the element id are added where entries name those.
	 */
	public function test_the_handle_and_the_element_id_are_added(): void {
		$exclusions = $this->exclusions();

		self::assertSame( array( 'jquery', Tracker_Tag::HANDLE ), $exclusions->exclude_handle( array( 'jquery' ) ) );
		self::assertSame( array( Tracker_Tag::ELEMENT_ID ), $exclusions->exclude_element_id( array() ) );
	}

	/**
	 * Something that is not a list is handed back untouched.
	 */
	public function test_something_that_is_not_a_list_is_left_alone(): void {
		$exclusions = $this->exclusions();

		self::assertSame( 'a string', $exclusions->exclude_address( 'a string' ) );
		self::assertNull( $exclusions->exclude_handle( null ) );
	}

	/**
	 * Until the address is saved, the lists are left as they are.
	 */
	public function test_nothing_is_added_until_the_address_is_saved(): void {
		$this->given_settings( '', '' );
		$exclusions = $this->exclusions();

		self::assertSame( array( 'jquery.min.js' ), $exclusions->exclude_address( array( 'jquery.min.js' ) ) );
		self::assertSame( array(), $exclusions->exclude_path( array() ) );
		self::assertSame( 'jquery.js', $exclusions->exclude_from_autoptimize( 'jquery.js' ) );
	}

	/**
	 * Autoptimize's exclusions as a comma-separated string, as its settings keep them.
	 *
	 * @return array<string, array{string, string}>
	 */
	public function autoptimize_strings(): array {
		return array(
			'empty'         => array( '', self::SCRIPT ),
			'blank'         => array( '  ', self::SCRIPT ),
			'with entries'  => array( 'wp-includes/js/dist/, jquery.js', 'wp-includes/js/dist/, jquery.js, ' . self::SCRIPT ),
			'already there' => array( 'jquery.js, ' . self::SCRIPT, 'jquery.js, ' . self::SCRIPT ),
		);
	}

	/**
	 * The address is appended to Autoptimize's comma-separated exclusions.
	 *
	 * @dataProvider autoptimize_strings
	 *
	 * @param string $given    The exclusions as given.
	 * @param string $expected The exclusions handed back.
	 */
	public function test_autoptimize_string_exclusions_gain_the_address( string $given, string $expected ): void {
		self::assertSame( $expected, $this->exclusions()->exclude_from_autoptimize( $given ) );
	}

	/**
	 * In the array form, the address becomes a key and every other key keeps its flag.
	 */
	public function test_autoptimize_array_exclusions_keep_their_flags(): void {
		self::assertSame(
			array(
				'jquery.js'  => '',
				'slider.js'  => 'defer',
				self::SCRIPT => '',
			),
			$this->exclusions()->exclude_from_autoptimize(
				array(
					'jquery.js' => '',
					'slider.js' => 'defer',
				)
			)
		);
	}

	/**
	 * Autoptimize exclusions of an unexpected shape are handed back untouched.
	 */
	public function test_autoptimize_exclusions_of_another_shape_are_left_alone(): void {
		self::assertSame( 42, $this->exclusions()->exclude_from_autoptimize( 42 ) );
	}

	/**
	 * The exclusions over the saved settings.
	 */
	private function exclusions(): Optimiser_Exclusions {
		return new Optimiser_Exclusions( new Settings() );
	}
}
