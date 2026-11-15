<?php
/**
 * Tests for working out where the visitor is.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Cloudflare_Ranges;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Visitor_Address;

/**
 * A forwarding header is believed only of a proxy known to write it, and an address that cannot
 * be trusted is not sent at all, for a reason the settings page can explain.
 */
final class Visitor_Address_Test extends Test_Case {

	/**
	 * One of Cloudflare's addresses.
	 */
	private const CLOUDFLARE = '173.245.48.20';

	/**
	 * A visitor's address.
	 */
	private const VISITOR = '203.0.113.7';

	/**
	 * A proxy in front of the site, on its private network.
	 */
	private const PROXY = '10.0.0.5';

	/**
	 * What the settings say about proxies in each test.
	 *
	 * @var array<string, mixed>
	 */
	private $proxy = array();

	/**
	 * Whether Cloudflare's list has been fetched in each test.
	 *
	 * @var bool
	 */
	private $cloudflare_known = true;

	/**
	 * Starts each test with nothing said about proxies and Cloudflare's list fetched.
	 */
	protected function setUp(): void {
		parent::setUp();
		Functions\when( 'get_option' )->alias(
			function ( string $option ) {
				if ( Settings::OPTION === $option ) {
					return $this->proxy + array(
						'address' => self::ADDRESS,
						'site_id' => self::SITE_ID,
					);
				}

				return Cloudflare_Ranges::OPTION === $option && $this->cloudflare_known ? array( 'ranges' => array( '173.245.48.0/20', '2400:cb00::/32' ) ) : false;
			}
		);
		Functions\when( 'wp_next_scheduled' )->justReturn( 1 );
	}

	/**
	 * A visitor connecting directly is where the connection came from.
	 */
	public function test_a_direct_visitor_is_where_the_connection_came_from(): void {
		$this->given_request( array( 'REMOTE_ADDR' => self::VISITOR ) );

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * An IPv6 address carrying an IPv4 one is the IPv4 address.
	 */
	public function test_an_ipv4_address_written_as_ipv6_is_the_ipv4_address(): void {
		$this->given_request( array( 'REMOTE_ADDR' => '::ffff:' . self::VISITOR ) );

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * Without an address for the connection, there is none to send.
	 */
	public function test_no_address_is_none_to_send(): void {
		$this->given_request( array() );

		self::assertSame( self::refused( Visitor_Address::NO_ADDRESS ), $this->resolve() );
	}

	/**
	 * A visitor's own header is not believed of a connection from a public address.
	 */
	public function test_a_header_from_a_direct_visitor_is_not_believed(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::VISITOR,
				'HTTP_X_FORWARDED_FOR' => '66.249.66.1',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A private connection that forwards somebody else's address is a proxy WordPress was not told
	 * about, and nothing is sent.
	 */
	public function test_a_proxy_nobody_named_stops_the_address(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::PROXY,
				'HTTP_X_FORWARDED_FOR' => self::VISITOR,
			)
		);

		self::assertSame( self::refused( Visitor_Address::UNKNOWN_PROXY ), $this->resolve() );
	}

	/**
	 * A private address without a proxy is not sent, except on a developer's own copy of the site.
	 */
	public function test_a_private_address_is_sent_only_from_a_developers_copy(): void {
		$this->given_request( array( 'REMOTE_ADDR' => '192.168.1.20' ) );

		self::assertSame( self::refused( Visitor_Address::PRIVATE_ADDRESS ), $this->resolve() );

		Functions\when( 'wp_get_environment_type' )->justReturn( 'local' );

		self::assertSame( $this->found( '192.168.1.20' ), $this->resolve() );
	}

	/**
	 * Cloudflare's header is believed of a connection from Cloudflare for the site's own name, or
	 * the same name with or without `www.`.
	 *
	 * @return array<string, array{string}>
	 */
	public function this_sites_names(): array {
		return array(
			'the site\'s name'      => array( 'example.com' ),
			'the name with a port'  => array( 'example.com:443' ),
			'the name with www.'    => array( 'www.example.com' ),
			'the name, in capitals' => array( 'EXAMPLE.COM' ),
		);
	}

	/**
	 * A request through Cloudflare for the site's own name is the visitor Cloudflare names.
	 *
	 * @dataProvider this_sites_names
	 *
	 * @param string $host The name the request was for.
	 */
	public function test_cloudflare_names_the_visitor_for_the_sites_own_names( string $host ): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::CLOUDFLARE,
				'HTTP_HOST'             => $host,
				'HTTP_CF_CONNECTING_IP' => self::VISITOR,
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A request through Cloudflare for somebody else's name could have been written by another
	 * Cloudflare customer, and nothing is sent.
	 */
	public function test_cloudflare_for_another_name_is_not_believed(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::CLOUDFLARE,
				'HTTP_HOST'             => 'example.org',
				'HTTP_CF_CONNECTING_IP' => self::VISITOR,
			)
		);

		self::assertSame( self::refused( Visitor_Address::FOREIGN_CLOUDFLARE ), $this->resolve() );
	}

	/**
	 * Cloudflare's header without an address in it names nobody.
	 */
	public function test_cloudflare_naming_nobody_is_no_visitor(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::CLOUDFLARE,
				'HTTP_HOST'             => 'example.com',
				'HTTP_CF_CONNECTING_IP' => 'unknown',
			)
		);

		self::assertSame( self::refused( Visitor_Address::NO_VISITOR ), $this->resolve() );
	}

	/**
	 * Cloudflare's header on a connection from anywhere but Cloudflare is the connection's: a host
	 * that already restored the visitor's address, or a visitor writing the header themselves.
	 */
	public function test_cloudflares_header_from_elsewhere_is_not_believed(): void {
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::VISITOR,
				'HTTP_HOST'             => 'example.com',
				'HTTP_CF_CONNECTING_IP' => '66.249.66.1',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * Until Cloudflare's list has arrived, nothing that came through Cloudflare is sent.
	 */
	public function test_nothing_through_cloudflare_is_sent_before_its_list_arrives(): void {
		$this->cloudflare_known = false;
		$this->given_request(
			array(
				'REMOTE_ADDR'           => self::CLOUDFLARE,
				'HTTP_HOST'             => 'example.com',
				'HTTP_CF_CONNECTING_IP' => self::VISITOR,
			)
		);

		self::assertSame( self::refused( Visitor_Address::CLOUDFLARE_PENDING ), $this->resolve() );
	}

	/**
	 * Through a proxy named in the settings, a list of addresses is read from its right-hand end
	 * past the proxies, to the first address that is not one.
	 */
	public function test_through_a_named_proxy_the_list_is_read_from_its_end(): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8', '198.51.100.0/24' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::PROXY,
				'HTTP_X_FORWARDED_FOR' => '66.249.66.1, ' . self::VISITOR . ', 198.51.100.4, 10.0.0.9',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A request that reached the site without going through the proxies is the connection's own,
	 * whatever its headers say.
	 */
	public function test_a_request_around_the_proxies_is_the_connections_own(): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::VISITOR,
				'HTTP_X_FORWARDED_FOR' => '66.249.66.1',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A private connection that is not one of the proxies is most likely a proxy the settings do not
	 * name, and is said to be so.
	 */
	public function test_a_private_connection_that_is_not_a_proxy_is_said_to_be_one(): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => '192.168.1.5',
				'HTTP_X_FORWARDED_FOR' => self::VISITOR,
			)
		);

		self::assertSame( self::refused( Visitor_Address::UNTRUSTED_PROXY ), $this->resolve() );
	}

	/**
	 * A visitor who sends a list long enough to push the proxy's own entry past a limit is still
	 * the address the proxy saw, never the one they wrote at the end of their part.
	 */
	public function test_a_long_list_cannot_push_the_proxys_entry_out(): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::PROXY,
				'HTTP_X_FORWARDED_FOR' => str_repeat( '1', 2034 ) . ', 66.249.66.1, ' . self::VISITOR,
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A byte that is not UTF-8, written by the visitor at the start of the list, leaves what the proxy
	 * appended to be read.
	 */
	public function test_a_broken_byte_does_not_empty_the_list(): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::PROXY,
				'HTTP_X_FORWARDED_FOR' => "\xff, " . self::VISITOR,
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A comma inside a quoted string of the standard header is part of a value: a visitor's `Host`
	 * copied into it by a proxy cannot add an element of their own.
	 */
	public function test_a_quoted_comma_does_not_add_an_element(): void {
		$this->given_proxy( 'Forwarded', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_FORWARDED' => 'for=' . self::VISITOR . ';host="x,for=66.249.66.1";proto=https',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * A quoted address with an escaped character in it is read as written.
	 */
	public function test_a_quoted_address_is_unescaped(): void {
		$this->given_proxy( 'Forwarded', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_FORWARDED' => 'for="[2606:4700::\\1111]:443"',
			)
		);

		self::assertSame( $this->found( '2606:4700::1111' ), $this->resolve() );
	}

	/**
	 * Headers whose start a visitor wrote to swallow the element the proxy added, each followed by
	 * what the proxy added.
	 *
	 * @return array<string, array{string}>
	 */
	public function quotation_marks_left_open(): array {
		return array(
			'a quotation mark left open'          => array( 'for=66.249.66.1;host="' ),
			'an escape before the proxy\'s comma' => array( 'for=66.249.66.1;host="x\\' ),
			'a backslash escaped, then left open' => array( 'for=66.249.66.1;host="x\\\\"y"' ),
			'two elements, the second left open'  => array( 'for=66.249.66.1, for="' ),
			'an escaped quotation mark'           => array( 'for=66.249.66.1;host="x\\"' ),
		);
	}

	/**
	 * A quotation mark the visitor left open does not reach into the element the proxy added, so
	 * the proxy's own record of the visitor is the one read.
	 *
	 * @dataProvider quotation_marks_left_open
	 *
	 * @param string $written What the visitor sent.
	 */
	public function test_a_quotation_mark_left_open_does_not_reach_the_proxys_element( string $written ): void {
		$this->given_proxy( 'Forwarded', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_FORWARDED' => $written . ', for="[2606:4700::1111]:443";proto=https',
			)
		);

		self::assertSame( $this->found( '2606:4700::1111' ), $this->resolve() );
	}

	/**
	 * A quoted value ending in an escaped backslash closes where it says.
	 */
	public function test_a_quoted_value_ending_in_a_backslash_closes(): void {
		$this->given_proxy( 'Forwarded', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_FORWARDED' => 'for=' . self::VISITOR . ';host="x\\\\", for=10.0.0.9',
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * Lists that name nobody past the proxies.
	 *
	 * @return array<string, array{string}>
	 */
	public function lists_naming_nobody(): array {
		return array(
			'no list at all'                   => array( '' ),
			'only proxies'                     => array( '10.0.0.9, 10.0.0.10' ),
			'something that is not an address' => array( self::VISITOR . ', unknown, 10.0.0.9' ),
		);
	}

	/**
	 * A list that names nobody past the proxies, or something that is not an address before
	 * reaching anybody, says nothing about the visitor.
	 *
	 * @dataProvider lists_naming_nobody
	 *
	 * @param string $named The list the proxy sent.
	 */
	public function test_a_list_naming_nobody_is_no_visitor( string $named ): void {
		$this->given_proxy( 'X-Forwarded-For', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'          => self::PROXY,
				'HTTP_X_FORWARDED_FOR' => $named,
			)
		);

		self::assertSame( self::refused( Visitor_Address::NO_VISITOR ), $this->resolve() );
	}

	/**
	 * The standard header names each address in a parameter, perhaps quoted and in brackets.
	 */
	public function test_the_standard_header_is_read_by_its_parameters(): void {
		$this->given_proxy( 'Forwarded', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_FORWARDED' => 'for=66.249.66.1;proto=https, for="[2606:4700::1111]:443";proto=https, for=10.0.0.9',
			)
		);

		self::assertSame( $this->found( '2606:4700::1111' ), $this->resolve() );
	}

	/**
	 * A header holding one address is a list of one.
	 */
	public function test_a_header_holding_one_address_names_it(): void {
		$this->given_proxy( 'X-Real-IP', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_X_REAL_IP' => self::VISITOR,
			)
		);

		self::assertSame( $this->found( self::VISITOR ), $this->resolve() );
	}

	/**
	 * What a proxy names is still not sent when it is not on the public internet.
	 */
	public function test_a_private_address_a_proxy_names_is_not_sent(): void {
		$this->given_proxy( 'X-Real-IP', array( '10.0.0.0/8' ) );
		$this->given_request(
			array(
				'REMOTE_ADDR'    => self::PROXY,
				'HTTP_X_REAL_IP' => '192.168.1.20',
			)
		);

		self::assertSame( self::refused( Visitor_Address::PRIVATE_ADDRESS ), $this->resolve() );
	}

	/**
	 * The address is worked out once for a request.
	 */
	public function test_the_address_is_worked_out_once(): void {
		$this->given_request( array( 'REMOTE_ADDR' => self::VISITOR ) );
		$visitor = new Visitor_Address( new Settings(), new Cloudflare_Ranges() );

		$first = $visitor->resolve();
		$this->given_request( array( 'REMOTE_ADDR' => '198.51.100.9' ) );

		self::assertSame( $first, $visitor->resolve() );
	}

	/**
	 * Makes the settings say visitors arrive through the given proxies.
	 *
	 * @param string $header  The header the proxies name the visitor in.
	 * @param array  $trusted The proxies' addresses.
	 * @phpstan-param list<string> $trusted
	 */
	private function given_proxy( string $header, array $trusted ): void {
		$this->proxy = array(
			'proxy_mode'    => Settings::THROUGH_PROXY,
			'proxy_header'  => $header,
			'proxy_trusted' => $trusted,
		);
	}

	/**
	 * Works the address out for the request as given.
	 *
	 * @return array{address: string, cause: string}
	 */
	private function resolve(): array {
		return ( new Visitor_Address( new Settings(), new Cloudflare_Ranges() ) )->resolve();
	}

	/**
	 * An address found.
	 *
	 * @param string $address The address.
	 * @return array{address: string, cause: string}
	 */
	private function found( string $address ): array {
		return array(
			'address' => $address,
			'cause'   => Visitor_Address::FOUND,
		);
	}

	/**
	 * No address, for a reason.
	 *
	 * @param string $cause The reason.
	 * @return array{address: string, cause: string}
	 */
	private static function refused( string $cause ): array {
		return array(
			'address' => '',
			'cause'   => $cause,
		);
	}
}
