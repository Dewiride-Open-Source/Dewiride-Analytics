<?php
/**
 * Tests for keeping Cloudflare's addresses.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Mockery;

/**
 * The list is fetched from Cloudflare when first needed and weekly after, the last good copy is
 * kept, and an answer that is not Cloudflare's list is refused whole.
 */
final class Cloudflare_Ranges_Test extends Test_Case {

	/**
	 * Cloudflare's answer, as its API gave it on 2026-11-15.
	 */
	private const ANSWER = array(
		'result'   => array(
			'ipv4_cidrs' => array( '173.245.48.0/20', '103.21.244.0/22', '104.16.0.0/13' ),
			'ipv6_cidrs' => array( '2400:cb00::/32', '2a06:98c0::/29' ),
			'etag'       => '38f79d050aa027e3be3865e495dcc9bc',
		),
		'success'  => true,
		'errors'   => array(),
		'messages' => array(),
	);

	/**
	 * What a fetch that failed outright returns in these tests.
	 */
	private const FAILED = 'failed fetch';

	/**
	 * Starts each test with fetches described by plain arrays.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'is_wp_error' )->alias(
			static function ( $thing ): bool {
				return self::FAILED === $thing;
			}
		);
		$this->given_responses_as_arrays();
	}

	/**
	 * The fetch is hooked.
	 */
	public function test_the_fetch_is_hooked(): void {
		$ranges = new Cloudflare_Ranges();

		$ranges->register();

		self::assertSame( 10, has_action( Cloudflare_Ranges::EVENT, array( $ranges, 'refresh' ) ) );
	}

	/**
	 * Cloudflare's own answer is read as its ranges.
	 */
	public function test_cloudflares_answer_is_read_as_its_ranges(): void {
		self::assertSame(
			array( '173.245.48.0/20', '103.21.244.0/22', '104.16.0.0/13', '2400:cb00::/32', '2a06:98c0::/29' ),
			Cloudflare_Ranges::read( self::ANSWER )
		);
	}

	/**
	 * Answers that are not Cloudflare's list.
	 *
	 * @return array<string, array{mixed}>
	 */
	public function not_the_list(): array {
		$answer = self::ANSWER;

		return array(
			'not an answer'                  => array( 'nonsense' ),
			'an answer that failed'          => array( array( 'success' => false ) + $answer ),
			'an answer holding no ranges'    => array( array( 'result' => array() ) + $answer ),
			'a range that is not one'        => array( self::with_ipv4( 'example.com/20' ) ),
			'a range that is not text'       => array( self::with_ipv4( 7 ) ),
			'every IPv4 address'             => array( self::with_ipv4( '0.0.0.0/0' ) ),
			'a quarter of the IPv4 internet' => array( self::with_ipv4( '64.0.0.0/2' ) ),
			'an IPv4 range wider than /10'   => array( self::with_ipv4( '64.0.0.0/9' ) ),
			'an IPv6 range wider than /20'   => array( self::with_ipv6( '2400::/19' ) ),
			'far too many ranges'            => array( self::with_ipv4( ...array_fill( 0, 101, '173.245.48.0/20' ) ) ),
		);
	}

	/**
	 * An answer that is not Cloudflare's list is refused whole.
	 *
	 * @dataProvider not_the_list
	 *
	 * @param mixed $answer The answer.
	 */
	public function test_an_answer_that_is_not_the_list_is_refused( $answer ): void {
		self::assertNull( Cloudflare_Ranges::read( $answer ) );
	}

	/**
	 * Lists at the edge of what is believed, which are Cloudflare's.
	 *
	 * @return array<string, array{array<string, mixed>}>
	 */
	public function lists_at_the_edge(): array {
		return array(
			'an IPv4 range of /10'   => array( self::with_ipv4( '64.0.0.0/10' ) ),
			'an IPv6 range of /20'   => array( self::with_ipv6( '2400::/20' ) ),
			'a hundred ranges'       => array( self::with_ipv4( ...array_fill( 0, 98, '173.245.48.0/20' ) ) ),
			'a single address range' => array( self::with_ipv4( '173.245.48.7/32' ) ),
		);
	}

	/**
	 * A list at the edge of what is believed is read.
	 *
	 * @dataProvider lists_at_the_edge
	 *
	 * @param array<string, mixed> $answer The answer.
	 */
	public function test_a_list_at_the_edge_is_read( array $answer ): void {
		self::assertNotNull( Cloudflare_Ranges::read( $answer ) );
	}

	/**
	 * Until a list has arrived there are no ranges, and reading them asks for nothing: whatever
	 * sends reports schedules the fetch, so nothing reaches Cloudflare before reporting is on.
	 */
	public function test_without_a_list_there_are_no_ranges_and_nothing_is_asked_for(): void {
		Functions\when( 'get_option' )->justReturn( false );
		Functions\expect( 'wp_schedule_event' )->never();
		Functions\expect( 'wp_remote_get' )->never();

		self::assertNull( ( new Cloudflare_Ranges() )->ranges() );
	}

	/**
	 * The weekly fetch is scheduled, the first one at once, unless it already is.
	 */
	public function test_the_weekly_fetch_is_scheduled_once(): void {
		Functions\when( 'wp_next_scheduled' )->justReturn( false );
		Functions\expect( 'wp_schedule_event' )->once()->with( Mockery::type( 'int' ), 'weekly', Cloudflare_Ranges::EVENT );

		Cloudflare_Ranges::schedule();
	}

	/**
	 * A list kept is used, and nothing is fetched.
	 */
	public function test_a_kept_list_is_used(): void {
		Functions\when( 'get_option' )->justReturn( array( 'ranges' => array( '173.245.48.0/20', 7 ) ) );
		Functions\expect( 'wp_schedule_event' )->never();

		self::assertSame( array( '173.245.48.0/20' ), ( new Cloudflare_Ranges() )->ranges() );
	}

	/**
	 * A fetched list is kept, loaded with every page.
	 */
	public function test_a_fetched_list_is_kept(): void {
		Functions\expect( 'wp_remote_get' )
			->once()
			->with( 'https://api.cloudflare.com/client/v4/ips', Mockery::subset( array( 'redirection' => 0 ) ) )
			->andReturn( self::response( 200, self::ANSWER ) );
		Functions\expect( 'update_option' )
			->once()
			->with( Cloudflare_Ranges::OPTION, Mockery::subset( array( 'ranges' => Cloudflare_Ranges::read( self::ANSWER ) ) ), true );

		( new Cloudflare_Ranges() )->refresh();
	}

	/**
	 * A failed fetch keeps the last good copy and waits for the week.
	 */
	public function test_a_failed_fetch_keeps_the_last_good_copy(): void {
		Functions\when( 'wp_remote_get' )->justReturn( self::response( 503, array() ) );
		Functions\when( 'get_option' )->justReturn( array( 'ranges' => array( '173.245.48.0/20' ) ) );
		Functions\expect( 'update_option' )->never();
		Functions\expect( 'wp_schedule_single_event' )->never();

		( new Cloudflare_Ranges() )->refresh();
	}

	/**
	 * A failed fetch with no copy yet tries again within the hour.
	 */
	public function test_a_failed_first_fetch_tries_again_soon(): void {
		Functions\when( 'wp_remote_get' )->justReturn( self::FAILED );
		Functions\when( 'get_option' )->justReturn( false );
		Functions\expect( 'update_option' )->never();
		Functions\expect( 'wp_schedule_single_event' )
			->once()
			->with(
				Mockery::on(
					static function ( $when ): bool {
						return is_int( $when ) && $when > time() && $when <= time() + 3600;
					}
				),
				Cloudflare_Ranges::EVENT
			);

		( new Cloudflare_Ranges() )->refresh();
	}

	/**
	 * The weekly fetch is removed with the plugin's other events.
	 */
	public function test_the_weekly_fetch_can_be_removed(): void {
		Functions\expect( 'wp_clear_scheduled_hook' )->once()->with( Cloudflare_Ranges::EVENT );

		Cloudflare_Ranges::unschedule();
	}

	/**
	 * Cloudflare's answer with other IPv4 ranges.
	 *
	 * @param mixed ...$ranges The IPv4 ranges.
	 * @return array<string, mixed>
	 */
	private static function with_ipv4( ...$ranges ): array {
		$answer                         = self::ANSWER;
		$answer['result']['ipv4_cidrs'] = $ranges;

		return $answer;
	}

	/**
	 * Cloudflare's answer with other IPv6 ranges.
	 *
	 * @param mixed ...$ranges The IPv6 ranges.
	 * @return array<string, mixed>
	 */
	private static function with_ipv6( ...$ranges ): array {
		$answer                         = self::ANSWER;
		$answer['result']['ipv6_cidrs'] = $ranges;

		return $answer;
	}

	/**
	 * A response as these tests describe them.
	 *
	 * @param int          $status The status.
	 * @param array<mixed> $body   The body, before it is encoded.
	 * @return array{response: array{code: int}, body: string}
	 */
	private static function response( int $status, array $body ): array {
		return array(
			'response' => array( 'code' => $status ),
			'body'     => (string) wp_json_encode( $body ),
		);
	}
}
