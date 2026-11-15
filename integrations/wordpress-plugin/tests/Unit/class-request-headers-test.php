<?php
/**
 * Tests for reading the request.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Dewiride\Analytics\WordPress\Request_Headers;

/**
 * Headers are read as sent, cleaned only of what no header can mean, and the path as an address
 * may hold it.
 */
final class Request_Headers_Test extends Test_Case {

	/**
	 * A header is read by its name, with WordPress's slashes taken off.
	 */
	public function test_a_header_is_read_by_its_name(): void {
		$this->given_request( array( 'HTTP_SEC_FETCH_DEST' => 'document' ) );

		self::assertSame( 'document', Request_Headers::header( 'Sec-Fetch-Dest' ) );
		self::assertSame( '', Request_Headers::header( 'Sec-Purpose' ) );
	}

	/**
	 * Control characters are dropped, and what a crawler wrote is otherwise kept as written.
	 */
	public function test_control_characters_are_dropped_and_nothing_else_is_rewritten(): void {
		$this->given_request( array( 'HTTP_USER_AGENT' => "Mozilla/5.0 <script>\x00\x1b (compatible; ExampleBot/1.0)" ) );

		self::assertSame( 'Mozilla/5.0 <script> (compatible; ExampleBot/1.0)', Request_Headers::header( 'User-Agent' ) );
	}

	/**
	 * A value that is not UTF-8 is emptied, and a long one is cut.
	 */
	public function test_invalid_text_is_emptied_and_long_text_cut(): void {
		$this->given_request(
			array(
				'HTTP_REFERER'    => "https://example.org/\xff\xfe",
				'HTTP_USER_AGENT' => str_repeat( 'a', 2000 ),
			)
		);

		self::assertSame( '', Request_Headers::header( 'Referer' ) );
		self::assertSame( 1024, strlen( Request_Headers::header( 'User-Agent' ) ) );
		self::assertSame( 'aaa', Request_Headers::header( 'User-Agent', 3 ) );
	}

	/**
	 * Something that is not text reads as absent.
	 */
	public function test_a_value_that_is_not_text_reads_as_absent(): void {
		$this->given_request( array( 'REMOTE_ADDR' => array( '203.0.113.7' ) ) );

		self::assertSame( '', Request_Headers::server( 'REMOTE_ADDR' ) );
	}

	/**
	 * The method is read in capitals.
	 */
	public function test_the_method_is_read_in_capitals(): void {
		$this->given_request( array( 'REQUEST_METHOD' => 'head' ) );

		self::assertSame( 'HEAD', Request_Headers::method() );
	}

	/**
	 * Paths, as requested and as the report holds them.
	 *
	 * @return array<string, array{mixed, string}>
	 */
	public function paths(): array {
		return array(
			'an ordinary path'               => array( '/posts/hello/', '/posts/hello/' ),
			'its query and fragment'         => array( '/posts/hello/?utm_source=mail&email=a@b.example#top', '/posts/hello/' ),
			'a probe, kept as it was sent'   => array( '/.env', '/.env' ),
			'characters a path may not hold' => array( '/<script>alert(1)</script> x', '/%3Cscript%3Ealert(1)%3C/script%3E%20x' ),
			'bytes that are not ASCII'       => array( "/caf\xc3\xa9", '/caf%C3%A9' ),
			'escapes already written'        => array( '/caf%C3%A9', '/caf%C3%A9' ),
			'a % that starts no escape'      => array( '/100%', '/100%25' ),
			'a whole address'                => array( 'http://example.com/wp-login.php?x=1', '/wp-login.php' ),
			'a path without its first slash' => array( 'posts', '/posts' ),
			'nothing'                        => array( '', '/' ),
			'something that is not text'     => array( array( '/posts' ), '/' ),
		);
	}

	/**
	 * The path is kept as asked for, and written as an address may hold it.
	 *
	 * @dataProvider paths
	 *
	 * @param mixed  $requested The request's path, as the web server gives it.
	 * @param string $expected  The path the report holds.
	 */
	public function test_the_path_is_kept_as_asked_for( $requested, string $expected ): void {
		$this->given_request( array( 'REQUEST_URI' => $requested ) );

		self::assertSame( $expected, Request_Headers::requested_path() );
	}

	/**
	 * A request without a path asked for the home page.
	 */
	public function test_a_request_without_a_path_asked_for_the_home_page(): void {
		$this->given_request( array() );

		self::assertSame( '/', Request_Headers::requested_path() );
	}

	/**
	 * A list of addresses longer than the limit keeps its end, which is what the proxies wrote, and
	 * loses its first entries, the one cut in two among them.
	 */
	public function test_a_long_list_keeps_its_end(): void {
		$this->given_request( array( 'HTTP_X_FORWARDED_FOR' => str_repeat( '1', 30 ) . ', 66.249.66.1, 203.0.113.7' ) );

		self::assertSame( ' 203.0.113.7', Request_Headers::list_end( 'X-Forwarded-For', 20 ) );
		self::assertSame( str_repeat( '1', 30 ) . ', 66.249.66.1, 203.0.113.7', Request_Headers::list_end( 'X-Forwarded-For', 100 ) );
	}

	/**
	 * A byte that is not UTF-8 leaves the rest of a list as it was.
	 */
	public function test_a_list_is_not_emptied_by_a_byte_that_is_not_utf8(): void {
		$this->given_request( array( 'HTTP_X_FORWARDED_FOR' => "\xff\xfe, 203.0.113.7" ) );

		self::assertSame( "\xff\xfe, 203.0.113.7", Request_Headers::list_end( 'X-Forwarded-For', 100 ) );
		self::assertSame( '', Request_Headers::list_end( 'X-Real-IP', 100 ) );
	}
}
