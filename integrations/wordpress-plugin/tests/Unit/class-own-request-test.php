<?php
/**
 * Tests for recognising the site's requests to itself.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Own_Request;

/**
 * The plugin's own requests are recognised by a header only the site can write, and the site's
 * other requests by where they come from and what they call themselves.
 */
final class Own_Request_Test extends Test_Case {

	/**
	 * The plugin's own request carries a header the site recognises.
	 */
	public function test_the_plugins_own_request_is_recognised_by_its_header(): void {
		$this->given_request( array( 'HTTP_X_DEWIRIDE_ANALYTICS_OWN' => Own_Request::marker() ) );

		self::assertTrue( Own_Request::is_marked() );
	}

	/**
	 * A header written without the site's secret keys is not recognised, nor is none at all.
	 */
	public function test_a_header_written_without_the_sites_keys_is_not_recognised(): void {
		$marker = Own_Request::marker();

		Functions\when( 'wp_salt' )->justReturn( 'somebody else\'s keys' );
		$this->given_request( array( 'HTTP_X_DEWIRIDE_ANALYTICS_OWN' => $marker ) );
		self::assertFalse( Own_Request::is_marked() );

		$this->given_request( array() );
		self::assertFalse( Own_Request::is_marked() );
	}

	/**
	 * The site's own addresses: this computer, the address its web server answers on, and the
	 * address its reports arrive from.
	 */
	public function test_the_sites_own_addresses_are_recognised(): void {
		$this->given_request( array( 'SERVER_ADDR' => '198.51.100.20' ) );

		self::assertTrue( Own_Request::is_own_address( '127.0.0.1', array() ) );
		self::assertTrue( Own_Request::is_own_address( '::1', array() ) );
		self::assertTrue( Own_Request::is_own_address( '198.51.100.20', array() ) );
		self::assertTrue( Own_Request::is_own_address( '203.0.113.9', array( '198.51.100.30', '203.0.113.9' ) ) );
		self::assertFalse( Own_Request::is_own_address( '203.0.113.7', array( '203.0.113.9' ) ) );
		self::assertFalse( Own_Request::is_own_address( '203.0.113.7', array() ) );
	}

	/**
	 * User agents, and whether each names a program the site itself runs.
	 *
	 * @return array<string, array{string, bool}>
	 */
	public function user_agents(): array {
		return array(
			'this plugin'                        => array( 'DewirideAnalytics/1.1.0', true ),
			'the installation\'s sitemap reader' => array( 'DewirideAnalytics/1.0 (sitemap fetcher; +https://analytics.dewiride.com)', true ),
			'WP Rocket warming its cache'        => array( 'WP Rocket/Preload', true ),
			'WordPress fetching this site'       => array( 'WordPress/7.1.4; https://example.com', true ),
			'WordPress fetching for another'     => array( 'WordPress/7.1.4; https://another.example', false ),
			'a browser'                          => array( 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36', false ),
			'a crawler'                          => array( 'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)', false ),
		);
	}

	/**
	 * Only the site's own programs are recognised by name.
	 *
	 * @dataProvider user_agents
	 *
	 * @param string $user_agent The user agent.
	 * @param bool   $own        Whether it names one of the site's own programs.
	 */
	public function test_the_sites_own_programs_are_recognised_by_name( string $user_agent, bool $own ): void {
		self::assertSame( $own, Own_Request::is_own_agent( $user_agent ) );
	}
}
