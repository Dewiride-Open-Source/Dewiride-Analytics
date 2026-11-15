<?php
/**
 * Tests for telling addresses on this computer or a private network from the rest.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Dewiride\Analytics\WordPress\Private_Host;

/**
 * Which hosts nobody on the public internet can reach.
 */
final class Private_Host_Test extends Test_Case {

	/**
	 * Addresses whose host only this computer or a private network reaches.
	 *
	 * @return array<string, array{string}>
	 */
	public function private_addresses(): array {
		return array(
			'this computer by name'          => array( 'http://localhost:3000' ),
			'a name under localhost'         => array( 'http://analytics.localhost' ),
			'loopback'                       => array( 'http://127.0.0.1:3000' ),
			'elsewhere in loopback'          => array( 'http://127.8.9.10' ),
			'a 10. address'                  => array( 'http://10.1.2.3' ),
			'a 172.16. address'              => array( 'http://172.16.0.5' ),
			'the top of 172.16.0.0/12'       => array( 'http://172.31.255.254' ),
			'a 192.168. address'             => array( 'http://192.168.1.20:8080' ),
			'a link-local address'           => array( 'http://169.254.10.20' ),
			'a local network name'           => array( 'http://analytics.local' ),
			'a home network name'            => array( 'http://nas.home.arpa' ),
			'a name kept for private use'    => array( 'http://analytics.internal' ),
			'a name kept for testing'        => array( 'http://analytics.test' ),
			'capitals in a private name'     => array( 'http://Analytics.LOCAL' ),
			'a path after a private address' => array( 'http://192.168.1.20/analytics' ),
		);
	}

	/**
	 * Addresses anybody on the internet could reach, or whose host cannot be told.
	 *
	 * @return array<string, array{string}>
	 */
	public function public_addresses(): array {
		return array(
			'a public name'                      => array( 'http://analytics.example.com' ),
			'a public IPv4 address'              => array( 'http://8.8.8.8' ),
			'just outside 172.16.0.0/12'         => array( 'http://172.32.0.1' ),
			'a private name inside a public one' => array( 'http://localhost.example.com' ),
			'a private ending without its dot'   => array( 'http://analyticslocal' ),
			'no host'                            => array( 'not an address' ),
		);
	}

	/**
	 * This computer and private networks are recognised.
	 *
	 * @dataProvider private_addresses
	 *
	 * @param string $address The address.
	 */
	public function test_a_private_host_matches( string $address ): void {
		self::assertTrue( Private_Host::matches( $address ) );
	}

	/**
	 * Anything the internet reaches is not.
	 *
	 * @dataProvider public_addresses
	 *
	 * @param string $address The address.
	 */
	public function test_a_public_host_does_not_match( string $address ): void {
		self::assertFalse( Private_Host::matches( $address ) );
	}
}
