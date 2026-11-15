<?php
/**
 * Tests for the image that counts readers whose browser runs no scripts.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Audience;
use Dewiride\Analytics\WordPress\No_Script_Image;
use Dewiride\Analytics\WordPress\Settings;

/**
 * The image's address, the page it names, and where and when it is printed.
 */
final class No_Script_Image_Test extends Test_Case {

	/**
	 * The markup around the image's address, exactly as printed. The browser is asked to name the
	 * site as the referrer and never the page, whose query can hold what a reader searched for.
	 */
	private const MARKUP = '<noscript><img src="%s" referrerpolicy="strict-origin" alt="" width="1" height="1" style="position:absolute" data-no-lazy="1" class="skip-lazy"></noscript>' . "\n";

	/**
	 * Starts each test on an ordinary page read by somebody who is not signed in.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_settings();
		Functions\when( 'is_preview' )->justReturn( false );
		Functions\when( 'is_customize_preview' )->justReturn( false );
		Functions\when( 'is_embed' )->justReturn( false );
		Functions\when( 'is_user_logged_in' )->justReturn( false );
		Functions\when( 'add_query_arg' )->alias( array( self::class, 'with_query' ) );
	}

	/**
	 * Adds arguments, already encoded, to an address without a query, as WordPress does.
	 *
	 * @param array<string, string> $arguments The arguments.
	 * @param string                $url       The address.
	 */
	public static function with_query( array $arguments, string $url ): string {
		$pairs = array();

		foreach ( $arguments as $name => $value ) {
			$pairs[] = $name . '=' . $value;
		}

		return $url . '?' . implode( '&', $pairs );
	}

	/**
	 * Leaves the request without a path again, as it is on the command line.
	 */
	protected function tearDown(): void {
		unset( $_SERVER['REQUEST_URI'] );
		parent::tearDown();
	}

	/**
	 * The image goes at the top of the body, and in the footer for older themes.
	 */
	public function test_it_hooks_the_body_and_the_footer(): void {
		$image = $this->image();

		$image->register();

		self::assertSame( 10, has_action( 'wp_body_open', array( $image, 'print_in_body' ) ) );
		self::assertSame( 10, has_action( 'wp_footer', array( $image, 'print_in_footer' ) ) );
	}

	/**
	 * The image is loaded from the collector, naming the website and the page, each encoded.
	 */
	public function test_the_source_names_the_website_and_the_page(): void {
		self::assertSame(
			self::ADDRESS . '/collect/pixel.gif?site=' . self::SITE_ID . '&u=https%3A%2F%2Fexample.com%2Fblog%2Fhello%2F',
			No_Script_Image::source( self::ADDRESS, self::SITE_ID, 'https://example.com/blog/hello/' )
		);
	}

	/**
	 * Characters that would end the attribute or the address are encoded in the page.
	 */
	public function test_the_page_cannot_break_out_of_the_address(): void {
		$source = No_Script_Image::source( self::ADDRESS, self::SITE_ID, 'https://example.com/"><script>&x=1' );

		self::assertStringEndsWith( '&u=https%3A%2F%2Fexample.com%2F%22%3E%3Cscript%3E%26x%3D1', $source );
		self::assertStringNotContainsString( '"', $source );
		self::assertStringNotContainsString( '<', $source );
	}

	/**
	 * The page is the site's own scheme and host with the path that was asked for, and no query.
	 */
	public function test_the_page_is_the_sites_origin_and_the_requested_path(): void {
		$_SERVER['REQUEST_URI'] = '/blog/hello/?utm_source=mail&email=reader%40example.com#comments';

		self::assertSame( 'https://example.com/blog/hello/', No_Script_Image::current_page() );
	}

	/**
	 * A port in the site's address is kept.
	 */
	public function test_the_page_keeps_the_sites_port(): void {
		Functions\when( 'home_url' )->justReturn( 'http://localhost:8081' );
		$_SERVER['REQUEST_URI'] = '/about/';

		self::assertSame( 'http://localhost:8081/about/', No_Script_Image::current_page() );
	}

	/**
	 * A request target written as a full address still names this site.
	 */
	public function test_a_full_address_in_the_request_names_this_site(): void {
		$_SERVER['REQUEST_URI'] = 'https://elsewhere.example/claimed/path?q=1';

		self::assertSame( 'https://example.com/claimed/path', No_Script_Image::current_page() );
	}

	/**
	 * Control characters in the request are removed.
	 */
	public function test_control_characters_are_removed(): void {
		$_SERVER['REQUEST_URI'] = "/a\x00b\x1fc/";

		self::assertSame( 'https://example.com/abc/', No_Script_Image::current_page() );
	}

	/**
	 * Without a request path the page is the home page.
	 */
	public function test_no_request_path_means_the_home_page(): void {
		unset( $_SERVER['REQUEST_URI'] );

		self::assertSame( 'https://example.com/', No_Script_Image::current_page() );
	}

	/**
	 * The image is printed at the top of the body, its address escaped for the attribute.
	 */
	public function test_the_image_is_printed_with_an_escaped_address(): void {
		$_SERVER['REQUEST_URI'] = '/hello/';
		$source                 = self::ADDRESS . '/collect/pixel.gif?site=' . self::SITE_ID . '&u=https%3A%2F%2Fexample.com%2Fhello%2F';

		Functions\when( 'esc_url' )->alias(
			static function ( string $url ): string {
				return 'escaped(' . $url . ')';
			}
		);
		$this->expectOutputString( sprintf( self::MARKUP, 'escaped(' . $source . ')' ) );

		$this->image()->print_in_body();
	}

	/**
	 * A theme that opened the body through WordPress gets the image there, not again in the footer.
	 */
	public function test_the_footer_prints_nothing_once_the_body_has_it(): void {
		do_action( 'wp_body_open' ); // phpcs:ignore WordPress.NamingConventions.PrefixAllGlobals.NonPrefixedHooknameFound -- Fired as the theme fires WordPress's own hook.
		$this->expectOutputString( '' );

		$this->image()->print_in_footer();
	}

	/**
	 * A theme that never opened the body through WordPress gets the image in the footer.
	 */
	public function test_the_footer_prints_the_image_for_older_themes(): void {
		$_SERVER['REQUEST_URI'] = '/';
		$this->expectOutputRegex( '~^<noscript><img src="[^"]+/collect/pixel\.gif\?site=' . self::SITE_ID . '&u=https%3A%2F%2Fexample\.com%2F" ~' );

		$this->image()->print_in_footer();
	}

	/**
	 * Nothing is printed for a visitor who is not counted.
	 */
	public function test_nothing_is_printed_for_somebody_not_counted(): void {
		Functions\when( 'is_preview' )->justReturn( true );
		$this->expectOutputString( '' );

		$this->image()->print_in_body();
	}

	/**
	 * Nothing is printed until counting is on.
	 */
	public function test_nothing_is_printed_until_counting_is_on(): void {
		$this->given_settings( '', self::SITE_ID );
		$this->expectOutputString( '' );

		$this->image()->print_in_body();
	}

	/**
	 * The image over the saved settings.
	 */
	private function image(): No_Script_Image {
		$settings = new Settings();

		return new No_Script_Image( $settings, new Audience( $settings ) );
	}
}
