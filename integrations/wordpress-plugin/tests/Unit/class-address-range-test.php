<?php
/**
 * Tests for reading addresses and ranges.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Dewiride\Analytics\WordPress\Address_Range;

/**
 * Addresses as forwarding headers write them, ranges as settings and Cloudflare write them, and
 * which addresses anybody on the internet can be reached at.
 */
final class Address_Range_Test extends Test_Case {

	/**
	 * Addresses as written, and as the plugin compares them.
	 *
	 * @return array<string, array{string, string}>
	 */
	public function addresses(): array {
		return array(
			'IPv4'                         => array( '203.0.113.7', '203.0.113.7' ),
			'IPv4 with a port'             => array( '203.0.113.7:51234', '203.0.113.7' ),
			'IPv6, written in full'        => array( '2001:0DB8:0000:0000:0000:0000:0000:0001', '2001:db8::1' ),
			'IPv6 in brackets'             => array( '[2001:db8::1]', '2001:db8::1' ),
			'IPv6 in brackets, with port'  => array( '[2001:db8::1]:443', '2001:db8::1' ),
			'IPv6 carrying an IPv4 one'    => array( '::ffff:203.0.113.7', '203.0.113.7' ),
			'surrounding space'            => array( ' 203.0.113.7 ', '203.0.113.7' ),
			'a name'                       => array( 'example.com', '' ),
			'an IPv4 address too long'     => array( '203.0.113.700', '' ),
			'an IPv6 zone'                 => array( 'fe80::1%eth0', '' ),
			'nothing'                      => array( '', '' ),
			'unknown, as proxies write it' => array( 'unknown', '' ),
		);
	}

	/**
	 * Every spelling of one address is the same address, and what is not one is nothing.
	 *
	 * @dataProvider addresses
	 *
	 * @param string $written  The address as written.
	 * @param string $expected The address as compared.
	 */
	public function test_an_address_is_read_in_one_spelling( string $written, string $expected ): void {
		self::assertSame( $expected, Address_Range::normalise( $written ) );
	}

	/**
	 * Ranges, the addresses at their edges, and whether each is inside.
	 *
	 * @return array<string, array{string, string, bool}>
	 */
	public function ranges(): array {
		return array(
			'the first address of an IPv4 range'           => array( '173.245.48.0/20', '173.245.48.0', true ),
			'the last address of an IPv4 range'            => array( '173.245.48.0/20', '173.245.63.255', true ),
			'just past an IPv4 range'                      => array( '173.245.48.0/20', '173.245.64.0', false ),
			'a whole byte of prefix'                       => array( '10.0.0.0/8', '10.255.255.255', true ),
			'an IPv4 range of one'                         => array( '203.0.113.7', '203.0.113.7', true ),
			'another address than a range of one'          => array( '203.0.113.7', '203.0.113.8', false ),
			'inside an IPv6 range'                         => array( '2a06:98c0::/29', '2a06:98c7:ffff::1', true ),
			'just past an IPv6 range'                      => array( '2a06:98c0::/29', '2a06:98c8::', false ),
			'an IPv4 address and an IPv6 range'            => array( '2400:cb00::/32', '203.0.113.7', false ),
			'a range of IPv6 addresses carrying IPv4 ones' => array( '::ffff:203.0.113.0/120', '203.0.113.200', true ),
		);
	}

	/**
	 * An address is inside a range exactly up to its edges.
	 *
	 * @dataProvider ranges
	 *
	 * @param string $range   The range.
	 * @param string $address The address.
	 * @param bool   $inside  Whether the address is inside.
	 */
	public function test_an_address_is_inside_a_range_up_to_its_edges( string $range, string $address, bool $inside ): void {
		$parsed = Address_Range::parse( $range );

		self::assertNotNull( $parsed );
		self::assertSame( $inside, Address_Range::contains( $parsed, $address ) );
	}

	/**
	 * What is not a range is refused.
	 *
	 * @return array<string, array{string}>
	 */
	public function not_ranges(): array {
		return array(
			'a prefix too long for IPv4' => array( '203.0.113.0/33' ),
			'a prefix that is not one'   => array( '203.0.113.0/x' ),
			'a name'                     => array( 'example.com/24' ),
			'an address with a port'     => array( '203.0.113.7:80' ),
			'an address in brackets'     => array( '[2001:db8::1]/64' ),
			'nothing'                    => array( '' ),
		);
	}

	/**
	 * Nothing that is not a range is read as one.
	 *
	 * @dataProvider not_ranges
	 *
	 * @param string $text The text.
	 */
	public function test_what_is_not_a_range_is_refused( string $text ): void {
		self::assertNull( Address_Range::parse( $text ) );
	}

	/**
	 * A list of ranges holds an address when any of them does, and a range that cannot be read
	 * holds none.
	 */
	public function test_a_list_holds_what_any_of_its_ranges_holds(): void {
		self::assertTrue( Address_Range::in_any( array( 'nonsense', 7, '198.51.100.0/24', '2400:cb00::/32' ), '2400:cb00::1' ) );
		self::assertFalse( Address_Range::in_any( array( 'nonsense', '198.51.100.0/24' ), '203.0.113.7' ) );
		self::assertFalse( Address_Range::in_any( array( '0.0.0.0/0' ), '' ) );
	}

	/**
	 * Addresses, and whether anybody on the internet can be reached at each.
	 *
	 * @return array<string, array{string, bool}>
	 */
	public function reachability(): array {
		return array(
			'a public IPv4 address'    => array( '8.8.8.8', true ),
			'a public IPv6 address'    => array( '2606:4700::1111', true ),
			'a private IPv4 address'   => array( '192.168.1.20', false ),
			'a private IPv6 address'   => array( 'fd00::1', false ),
			'this computer'            => array( '127.0.0.1', false ),
			'this computer, over IPv6' => array( '::1', false ),
			'a link-local address'     => array( '169.254.10.1', false ),
			'a carrier-shared address' => array( '100.64.0.1', false ),
			'just past carrier-shared' => array( '100.128.0.1', true ),
		);
	}

	/**
	 * Private, local and shared addresses are not on the public internet.
	 *
	 * @dataProvider reachability
	 *
	 * @param string $address   The address.
	 * @param bool   $reachable Whether anybody on the internet can be reached at it.
	 */
	public function test_only_public_addresses_are_public( string $address, bool $reachable ): void {
		self::assertSame( $reachable, Address_Range::is_public( $address ) );
	}

	/**
	 * This computer's addresses are recognised, and nobody else's.
	 */
	public function test_this_computers_addresses_are_recognised(): void {
		self::assertTrue( Address_Range::is_loopback( '127.0.0.1' ) );
		self::assertTrue( Address_Range::is_loopback( '127.8.9.10' ) );
		self::assertTrue( Address_Range::is_loopback( '::1' ) );
		self::assertFalse( Address_Range::is_loopback( '128.0.0.1' ) );
	}
}
